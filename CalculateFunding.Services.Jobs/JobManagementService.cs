﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CalculateFunding.Common.Models;
using CalculateFunding.Common.Models.HealthCheck;
using CalculateFunding.Common.ServiceBus.Interfaces;
using CalculateFunding.Common.Utility;
using CalculateFunding.Models.Jobs;
using CalculateFunding.Models.Messages;
using CalculateFunding.Services.Core.Extensions;
using CalculateFunding.Services.Core.Helpers;
using CalculateFunding.Services.Jobs.Interfaces;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.ServiceBus;
using Polly;
using Serilog;

namespace CalculateFunding.Services.Jobs
{
    public class JobManagementService : IJobManagementService, IHealthChecker
    {
        private const string SfaCorrelationId = "sfa-correlationId";
        private readonly IJobRepository _jobRepository;
        private readonly INotificationService _notificationService;
        private readonly IJobDefinitionsService _jobDefinitionsService;
        private readonly AsyncPolicy _jobsRepositoryPolicy;
        private readonly AsyncPolicy _jobDefinitionsRepositoryPolicy;
        private readonly AsyncPolicy _messengerServicePolicy;
        private readonly ILogger _logger;
        private readonly IValidator<CreateJobValidationModel> _createJobValidator;
        private readonly IMessengerService _messengerService;
        private readonly static SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1, 1);

        public JobManagementService(
            IJobRepository jobRepository,
            INotificationService notificationService,
            IJobDefinitionsService jobDefinitionsService,
            IJobsResiliencePolicies resiliencePolicies,
            ILogger logger,
            IValidator<CreateJobValidationModel> createJobValidator,
            IMessengerService messengerService)
        {
            Guard.ArgumentNotNull(jobRepository, nameof(jobRepository));
            Guard.ArgumentNotNull(notificationService, nameof(notificationService));
            Guard.ArgumentNotNull(jobDefinitionsService, nameof(jobDefinitionsService));
            Guard.ArgumentNotNull(logger, nameof(logger));
            Guard.ArgumentNotNull(createJobValidator, nameof(createJobValidator));
            Guard.ArgumentNotNull(messengerService, nameof(messengerService));
            Guard.ArgumentNotNull(resiliencePolicies?.JobRepository, nameof(resiliencePolicies.JobRepository));
            Guard.ArgumentNotNull(resiliencePolicies?.JobDefinitionsRepository, nameof(resiliencePolicies.JobDefinitionsRepository));
            Guard.ArgumentNotNull(resiliencePolicies?.MessengerServicePolicy, nameof(resiliencePolicies.MessengerServicePolicy));

            _jobRepository = jobRepository;
            _notificationService = notificationService;
            _jobDefinitionsService = jobDefinitionsService;
            _jobsRepositoryPolicy = resiliencePolicies.JobRepository;
            _jobDefinitionsRepositoryPolicy = resiliencePolicies.JobDefinitionsRepository;
            _logger = logger;
            _createJobValidator = createJobValidator;
            _messengerService = messengerService;
            _messengerServicePolicy = resiliencePolicies.MessengerServicePolicy;
        }

        public async Task<ServiceHealth> IsHealthOk()
        {
            ServiceHealth jobsRepoHealth = await ((IHealthChecker)_jobRepository).IsHealthOk();

            ServiceHealth health = new ServiceHealth
            {
                Name = nameof(JobManagementService)
            };
            health.Dependencies.AddRange(jobsRepoHealth.Dependencies);
            return health;
        }

        public async Task<IActionResult> CreateJobs(IEnumerable<JobCreateModel> jobs, Reference user)
        {
            Guard.ArgumentNotNull(jobs, nameof(jobs));

            if (!jobs.Any())
            {
                string message = "Empty collection of job create models was provided";
                _logger.Warning(message);

                return new BadRequestObjectResult(message);
            }

            IEnumerable<JobDefinition> jobDefinitions = await _jobDefinitionsService.GetAllJobDefinitions();

            if (jobDefinitions.IsNullOrEmpty())
            {
                string message = "Failed to retrieve job definitions";
                _logger.Error(message);
                return new InternalServerErrorResult(message);
            }

            IList<ValidationResult> validationResults = new List<ValidationResult>();

            //ensure all jobs in batch have the correct job definition
            foreach (JobCreateModel jobCreateModel in jobs)
            {
                Guard.IsNullOrWhiteSpace(jobCreateModel.JobDefinitionId, nameof(jobCreateModel.JobDefinitionId));

                JobDefinition jobDefinition = jobDefinitions?.FirstOrDefault(m => m.Id == jobCreateModel.JobDefinitionId);

                if (jobDefinition == null)
                {
                    string message = $"A job definition could not be found for id: {jobCreateModel.JobDefinitionId}";
                    _logger.Warning(message);

                    return new PreconditionFailedResult(message);
                }

                CreateJobValidationModel createJobValidationModel = new CreateJobValidationModel
                {
                    JobCreateModel = jobCreateModel,
                    JobDefinition = jobDefinition
                };

                ValidationResult validationResult = _createJobValidator.Validate(createJobValidationModel);
                if (validationResult != null && !validationResult.IsValid)
                {
                    validationResults.Add(validationResult);
                }
            }

            if (validationResults.Any())
            {
                return new BadRequestObjectResult(validationResults);
            }

            return new OkObjectResult(await CreateAllJobs(jobs, user, jobDefinitions));
        }

        private async Task<IList<Job>> CreateAllJobs(IEnumerable<JobCreateModel> jobs, Reference user, IEnumerable<JobDefinition> jobDefinitions)
        {
            IList<Job> createdJobs = new List<Job>();

            foreach (JobCreateModel job in jobs)
            {
                Job newJobResult = await JobFromJobCreateModel(job, user);

                if (newJobResult == null)
                {
                    string message = $"Failed to create a job for job definition id: {job.JobDefinitionId}";
                    _logger.Error(message);
                    throw new Exception(message);
                }

                createdJobs.Add(newJobResult);
            }

            IEnumerable<JobDefinition> jobDefinitionsToSupersede = await SupersedeJobs(createdJobs, jobDefinitions);

            await QueueNotifications(createdJobs, jobDefinitionsToSupersede);

            return createdJobs;
        }

        private async Task<Job> JobFromJobCreateModel(JobCreateModel job, Reference user)
        {
            Guard.ArgumentNotNull(job.Trigger, nameof(job.Trigger));

            if (string.IsNullOrWhiteSpace(job.InvokerUserId) || string.IsNullOrWhiteSpace(job.InvokerUserDisplayName))
            {
                job.InvokerUserId = user?.Id;
                job.InvokerUserDisplayName = user?.Name;
            }

            Job newJobResult = await CreateJob(job);
            return newJobResult;
        }

        private async Task QueueNotifications(IList<Job> createdJobs, IEnumerable<JobDefinition> jobDefinitionsToSupersede)
        {
            List<Task> allTasks = new List<Task>();
            SemaphoreSlim throttler = new SemaphoreSlim(initialCount: 30);
            foreach (Job job in createdJobs)
            {
                await throttler.WaitAsync();
                allTasks.Add(
                    Task.Run(async () =>
                    {
                        try
                        {
                            JobDefinition jobDefinition = jobDefinitionsToSupersede.First(m => m.Id == job.JobDefinitionId);

                            await QueueNewJob(job, jobDefinition);

                            JobNotification jobNotification = CreateJobNotificationFromJob(job);

                            await _notificationService.SendNotification(jobNotification);
                        }
                        finally
                        {
                            throttler.Release();
                        }
                    }));
            }

            await TaskHelper.WhenAllAndThrow(allTasks.ToArray());
        }

        private async Task<IEnumerable<JobDefinition>> SupersedeJobs(IList<Job> createdJobs, IEnumerable<JobDefinition> jobDefinitions)
        {
            IEnumerable<IGrouping<string, Job>> jobDefinitionGroups = createdJobs
                .GroupBy(j => j.JobDefinitionId);

            IEnumerable<JobDefinition> jobDefinitionsToSupersede = jobDefinitions
                .GroupBy(x => x.Id)
                .Select(x => x.First(y => y.Id == x.Key));

            foreach (IGrouping<string, Job> jobDefinitionKvp in jobDefinitionGroups)
            {
                Job jobToSupersedeOthers = jobDefinitionKvp.First();

                JobDefinition jobDefinition = jobDefinitionsToSupersede.First(m => m.Id == jobToSupersedeOthers.JobDefinitionId);

                await CheckForSupersededAndCancelOtherJobs(jobToSupersedeOthers, jobDefinition);
            }

            return jobDefinitionsToSupersede;
        }

        public async Task<IActionResult> AddJobLog(string jobId, JobLogUpdateModel jobLogUpdateModel)
        {
            Guard.IsNullOrWhiteSpace(jobId, nameof(jobId));
            Guard.ArgumentNotNull(jobLogUpdateModel, nameof(jobLogUpdateModel));

            Job job = await _jobsRepositoryPolicy.ExecuteAsync(() => _jobRepository.GetJobById(jobId));

            if (job == null)
            {
                _logger.Error($"A job could not be found for job id: '{jobId}'");

                return new NotFoundObjectResult($"A job could not be found for job id: '{jobId}'");
            }

            bool saveJob = false;

            if (jobLogUpdateModel.CompletedSuccessfully.HasValue)
            {
                job.Completed = DateTimeOffset.UtcNow;
                job.RunningStatus = RunningStatus.Completed;
                job.CompletionStatus = jobLogUpdateModel.CompletedSuccessfully.Value ? CompletionStatus.Succeeded : CompletionStatus.Failed;
                job.Outcome = jobLogUpdateModel.Outcome;
                saveJob = true;
            }
            else
            {
                if (job.RunningStatus != RunningStatus.InProgress)
                {
                    job.RunningStatus = RunningStatus.InProgress;
                    saveJob = true;
                }
            }

            if (saveJob)
            {
                HttpStatusCode statusCode = await _jobsRepositoryPolicy.ExecuteAsync(() => _jobRepository.UpdateJob(job));

                if (!statusCode.IsSuccess())
                {
                    _logger.Error($"Failed to update job id: '{jobId}' with status code '{(int)statusCode}'");
                    return new InternalServerErrorResult($"Failed to update job id: '{jobId}' with status code '{(int)statusCode}'");
                }
            }

            JobLog jobLog = new JobLog
            {
                Id = Guid.NewGuid().ToString(),
                JobId = jobId,
                ItemsProcessed = jobLogUpdateModel.ItemsProcessed,
                ItemsSucceeded = jobLogUpdateModel.ItemsSucceeded,
                ItemsFailed = jobLogUpdateModel.ItemsFailed,
                Outcome = jobLogUpdateModel.Outcome,
                CompletedSuccessfully = jobLogUpdateModel.CompletedSuccessfully,
                Timestamp = DateTimeOffset.UtcNow
            };

            HttpStatusCode createJobLogStatus = await _jobsRepositoryPolicy.ExecuteAsync(() => _jobRepository.CreateJobLog(jobLog));

            if (!createJobLogStatus.IsSuccess())
            {
                _logger.Error($"Failed to create a job log for job id: '{jobId}'");
                throw new Exception($"Failed to create a job log for job id: '{jobId}'");
            }

            await SendJobLogNotification(job, jobLog);

            return new OkObjectResult(jobLog);
        }

        /// <summary>
        /// Cancel job based on internal state management conditions
        /// </summary>
        /// <param name="jobId">Job ID</param>
        /// <returns></returns>
        public async Task<IActionResult> CancelJob(string jobId)
        {
            // Set running status to Cancelled and CompletionStatus to Fail

            // Send notification after status logged
            await _notificationService.SendNotification(new JobNotification());

            throw new NotImplementedException();
        }

        public async Task SupersedeJob(Job runningJob, Job replacementJob)
        {
            if (CanSupersede(runningJob, replacementJob))
            {
                runningJob.Completed = DateTimeOffset.UtcNow;
                runningJob.CompletionStatus = CompletionStatus.Superseded;
                runningJob.SupersededByJobId = replacementJob.Id;
                runningJob.RunningStatus = RunningStatus.Completed;

                HttpStatusCode statusCode = await _jobsRepositoryPolicy.ExecuteAsync(() => _jobRepository.UpdateJob(runningJob));

                if (statusCode.IsSuccess())
                {
                    JobNotification jobNotification = CreateJobNotificationFromJob(runningJob);

                    await _notificationService.SendNotification(jobNotification);
                }
                else
                {
                    _logger.Error($"Failed to update superseded job, Id: {runningJob.Id}");
                }
            }
        }

        public async Task ProcessJobNotification(Message message)
        {
            Guard.ArgumentNotNull(message, nameof(message));

            // When a job completes see if the parent job can be completed
            JobNotification jobNotification = message.GetPayloadAsInstanceOf<JobNotification>();

            Guard.ArgumentNotNull(jobNotification, "message payload");

            if (jobNotification.RunningStatus == RunningStatus.Completed)
            {
                if (!message.UserProperties.ContainsKey("jobId"))
                {
                    _logger.Error("Job Notification message has no JobId");
                    return;
                }

                string jobId = message.UserProperties["jobId"].ToString();

                Job job = await _jobsRepositoryPolicy.ExecuteAsync(() => _jobRepository.GetJobById(jobId));

                if (job == null)
                {
                    _logger.Error("Could not find job with id {JobId}", jobId);
                    return;
                }

                if (!string.IsNullOrEmpty(job.ParentJobId))
                {
                    IEnumerable<Job> childJobs = await _jobsRepositoryPolicy.ExecuteAsync(() => _jobRepository.GetChildJobsForParent(job.ParentJobId));

                    if (CompletedAll(childJobs))
                    {
                        await semaphoreSlim.WaitAsync();

                        try
                        {
                            Job parentJob = await _jobsRepositoryPolicy.ExecuteAsync(() => _jobRepository.GetJobById(job.ParentJobId));

                            IEnumerable<JobDefinition> jobDefinitions = await _jobDefinitionsService.GetAllJobDefinitions();

                            JobDefinition jobDefinition = jobDefinitions.FirstOrDefault(j => j.Id == parentJob.JobDefinitionId);

                            // if the parent job has pre-completion jobs and is not completing then we need to queue the pre-completion jobs
                            if (jobDefinition != null && jobDefinition.PreCompletionJobs.AnyWithNullCheck() && parentJob.RunningStatus != RunningStatus.Completing)
                            {
                                await QueuePreCompletionJobs(parentJob);
                            }
                            else
                            {
                                // the pre-completion jobs must have completed at this point as the child jobs have completed
                                await CompleteParentJob(parentJob, childJobs, job, jobId);
                            }

                        }
                        finally
                        {
                            semaphoreSlim.Release();
                        }
                    }
                    else
                    {
                        _logger.Information("Completed Job {JobId} parent {ParentJobId} has in progress child jobs and cannot be completed", jobId, job.ParentJobId);
                    }
                }
                else
                {
                    _logger.Information("Completed Job {JobId} has no parent", jobId);
                }
            }
        }

        private async Task QueuePreCompletionJobs(Job parentJob)
        {
            IEnumerable<JobDefinition> jobDefinitions = await _jobDefinitionsService.GetAllJobDefinitions();

            JobDefinition jobDefinition = jobDefinitions.FirstOrDefault(j => j.Id == parentJob.JobDefinitionId);

            if (jobDefinition != null && jobDefinition.PreCompletionJobs.AnyWithNullCheck())
            {
                IEnumerable<JobCreateModel> jobModels = jobDefinition.PreCompletionJobs.Select(_ =>
                {
                    return new JobCreateModel {
                        CorrelationId = parentJob.CorrelationId,
                        InvokerUserId = parentJob.InvokerUserId,
                        InvokerUserDisplayName = parentJob.InvokerUserDisplayName,
                        JobDefinitionId = _,
                        MessageBody = parentJob.MessageBody,
                        Properties = parentJob.Properties,
                        ParentJobId = parentJob.JobId,
                        SpecificationId = parentJob.SpecificationId,
                        Trigger = parentJob.Trigger };

                });

                await CreateAllJobs(jobModels, new Reference { Id = parentJob.InvokerUserId, Name = parentJob.InvokerUserDisplayName }, jobDefinitions);

                parentJob.RunningStatus = RunningStatus.Completing;

                await _jobsRepositoryPolicy.ExecuteAsync(() => _jobRepository.UpdateJob(parentJob));
            }
        }

        private static bool CompletedAll(IEnumerable<Job> jobs) =>
            jobs.IsNullOrEmpty() || jobs.Any() && jobs.All(j => j.RunningStatus == RunningStatus.Completed);


        private async Task CompleteParentJob(Job parentJob, IEnumerable<Job> childJobs, Job job, string jobId)
        {
            if (parentJob.RunningStatus != RunningStatus.Completed)
            {
                parentJob.Completed = DateTimeOffset.UtcNow;
                parentJob.RunningStatus = RunningStatus.Completed;
                parentJob.CompletionStatus = DetermineCompletionStatus(childJobs);
                parentJob.Outcome = "All child jobs completed";

                await _jobsRepositoryPolicy.ExecuteAsync(() => _jobRepository.UpdateJob(parentJob));
                _logger.Information(
                    "Parent Job {ParentJobId} of Completed Job {JobId} has been completed because all child jobs are now complete",
                    job.ParentJobId, jobId);

                await _notificationService.SendNotification(CreateJobNotificationFromJob(parentJob));
            }
        }

        public async Task CheckAndProcessTimedOutJobs()
        {
            IEnumerable<Job> nonCompletedJobs = await _jobsRepositoryPolicy.ExecuteAsync(() => _jobRepository.GetNonCompletedJobs());

            if (nonCompletedJobs.IsNullOrEmpty())
            {
                _logger.Information("Zero non completed jobs to process, finished processing timed out jobs");

                return;
            }

            int countOfJobsToProcess = nonCompletedJobs.Count();

            _logger.Information($"{countOfJobsToProcess} non completed jobs to process");

            IEnumerable<JobDefinition> jobDefinitions = await _jobDefinitionsService.GetAllJobDefinitions();

            if (jobDefinitions.IsNullOrEmpty())
            {
                _logger.Error("Failed to retrieve job definitions when processing timed out jobs");
                throw new Exception("Failed to retrieve job definitions when processing timed out jobs");
            }

            foreach (Job job in nonCompletedJobs)
            {
                JobDefinition jobDefinition = jobDefinitions.FirstOrDefault(m => m.Id == job.JobDefinitionId);

                if (jobDefinition == null)
                {
                    _logger.Error($"Failed to find job definition : '{job.JobDefinitionId}' for job id: '{job.Id}'");
                }
                else
                {
                    DateTimeOffset jobStartDate = job.Created;

                    TimeSpan timeout = jobDefinition.Timeout;

                    if (DateTimeOffset.UtcNow > jobStartDate.Add(timeout))
                    {
                        _logger.Information($"Job with id: '{job.Id}' as exceeded its maximum timeout threshold");

                        await TimeoutJob(job);
                    }
                }
            }

        }

        public async Task<IActionResult> DeleteJobs(Message message)
        {
            string specificationId = message.UserProperties["specification-id"].ToString();
            if (string.IsNullOrEmpty(specificationId))
                return new BadRequestObjectResult("Null or empty specification Id provided for deleting calculation results");

            string deletionTypeProperty = message.UserProperties["deletion-type"].ToString();
            if (string.IsNullOrEmpty(deletionTypeProperty))
                return new BadRequestObjectResult("Null or empty deletion type provided for deleting calculation results");

            await _jobRepository.DeleteJobsBySpecificationId(specificationId, deletionTypeProperty.ToDeletionType());

            return new OkResult();
        }

        private async Task TimeoutJob(Job runningJob)
        {
            runningJob.Completed = DateTimeOffset.UtcNow;
            runningJob.CompletionStatus = CompletionStatus.TimedOut;
            runningJob.RunningStatus = RunningStatus.Completed;

            HttpStatusCode statusCode = await _jobsRepositoryPolicy.ExecuteAsync(() => _jobRepository.UpdateJob(runningJob));

            if (statusCode.IsSuccess())
            {
                JobNotification jobNotification = CreateJobNotificationFromJob(runningJob);

                await _notificationService.SendNotification(jobNotification);
            }
            else
            {
                _logger.Error($"Failed to update timeout job, Id: '{runningJob.Id}' with status code {(int)statusCode}");
            }
        }

        private CompletionStatus? DetermineCompletionStatus(IEnumerable<Job> jobs)
        {
            if (jobs.Any(j => !j.CompletionStatus.HasValue))
            {
                // There are still some jobs in progress so there is no completion status
                return null;
            }

            if (jobs.Any(j => j.CompletionStatus == CompletionStatus.TimedOut))
            {
                // At least one job timed out so that is the overall completion status for the group of jobs
                return CompletionStatus.TimedOut;
            }

            if (jobs.Any(j => j.CompletionStatus == CompletionStatus.Cancelled))
            {
                // At least one job was cancelled so that is the overall completion status for the group of jobs
                return CompletionStatus.Cancelled;
            }

            if (jobs.Any(j => j.CompletionStatus == CompletionStatus.Superseded))
            {
                // At least one job was superseded so that is the overall completion status for the group of jobs
                return CompletionStatus.Superseded;
            }

            if (jobs.Any(j => j.CompletionStatus == CompletionStatus.Failed))
            {
                // At least one job failed so that is the overall completion status for the group of jobs
                return CompletionStatus.Failed;
            }

            // Got to here so that must mean all jobs succeeded
            return CompletionStatus.Succeeded;
        }

        private JobNotification CreateJobNotificationFromJob(Job job)
        {
            return new JobNotification
            {
                CompletionStatus = job.CompletionStatus,
                InvokerUserDisplayName = job.InvokerUserDisplayName,
                InvokerUserId = job.InvokerUserId,
                ItemCount = job.ItemCount,
                JobId = job.Id,
                JobType = job.JobDefinitionId,
                ParentJobId = job.ParentJobId,
                Outcome = job.Outcome,
                RunningStatus = job.RunningStatus,
                SpecificationId = job.SpecificationId,
                StatusDateTime = DateTimeOffset.UtcNow,
                SupersededByJobId = job.SupersededByJobId,
                Trigger = job.Trigger
            };
        }

        private async Task<bool> CheckForSupersededAndCancelOtherJobs(Job currentJob, JobDefinition jobDefinition)
        {
            bool isSuperseding = false;

            if (jobDefinition.SupersedeExistingRunningJobOnEnqueue)
            {
                IEnumerable<Job> runningJobs = await _jobsRepositoryPolicy.ExecuteAsync(() =>
                    _jobRepository.GetRunningJobsForSpecificationAndJobDefinitionId(currentJob.SpecificationId, jobDefinition.Id));

                if (!runningJobs.IsNullOrEmpty())
                {
                    isSuperseding = true;

                    foreach (Job runningJob in runningJobs)
                    {
                        await SupersedeJob(runningJob, currentJob);
                    }
                }
            }

            return isSuperseding;
        }

        private async Task<Job> CreateJob(JobCreateModel job)
        {
            Job newJob = new Job
            {
                JobDefinitionId = job.JobDefinitionId,
                InvokerUserId = job.InvokerUserId,
                InvokerUserDisplayName = job.InvokerUserDisplayName,
                ItemCount = job.ItemCount,
                SpecificationId = job.SpecificationId,
                Trigger = job.Trigger,
                ParentJobId = job.ParentJobId,
                CorrelationId = job.CorrelationId,
                Properties = job.Properties,
                MessageBody = job.MessageBody
            };

            Job newJobResult = null;

            try
            {
                newJobResult = await _jobDefinitionsRepositoryPolicy.ExecuteAsync(() => _jobRepository.CreateJob(newJob));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to save new job with definition id {job.JobDefinitionId}");
            }

            return newJobResult;
        }

        private async Task SendJobLogNotification(Job job, JobLog jobLog)
        {
            JobNotification jobNotification = new JobNotification
            {
                JobId = job.Id,
                JobType = job.JobDefinitionId,
                RunningStatus = job.RunningStatus,
                CompletionStatus = job.CompletionStatus,
                SpecificationId = job.SpecificationId,
                InvokerUserDisplayName = job.InvokerUserDisplayName,
                InvokerUserId = job.InvokerUserId,
                ItemCount = job.ItemCount,
                Trigger = job.Trigger,
                ParentJobId = job.ParentJobId,
                SupersededByJobId = job.SupersededByJobId,
                Outcome = jobLog.Outcome,
                StatusDateTime = DateTimeOffset.UtcNow,
                OverallItemsFailed = jobLog.ItemsFailed,
                OverallItemsProcessed = jobLog.ItemsProcessed,
                OverallItemsSucceeded = jobLog.ItemsSucceeded
            };

            await _notificationService.SendNotification(jobNotification);
        }

        private async Task QueueNewJob(Job job, JobDefinition jobDefinition)
        {
            string queueOrTopic = !string.IsNullOrWhiteSpace(jobDefinition.MessageBusQueue) ? jobDefinition.MessageBusQueue : jobDefinition.MessageBusTopic;

            //support parent jobs with no queue / consumers
            if (queueOrTopic.IsNullOrWhitespace()) return;

            string data = !string.IsNullOrWhiteSpace(job.MessageBody) ? job.MessageBody : null;
            IDictionary<string, string> messageProperties = job.Properties;

            string sessionId = null;

            const string jobId = "jobId";
            
            if (messageProperties == null)
            {
                messageProperties = new Dictionary<string, string>
                {
                    { jobId, job.Id },
                    { SfaCorrelationId, job.CorrelationId }
                };
            }
            else
            {
                if (!messageProperties.ContainsKey(jobId))
                {
                    messageProperties.Add(jobId, job.Id);
                }

                if (!messageProperties.ContainsKey(SfaCorrelationId))
                {
                    messageProperties.Add(SfaCorrelationId, job.CorrelationId);
                }

                if (!string.IsNullOrWhiteSpace(jobDefinition.SessionMessageProperty))
                {
                    //Shouldn't happen as already validated
                    if (!job.Properties.ContainsKey(jobDefinition.SessionMessageProperty))
                    {
                        string errorMessage = $"Missing session property on job with id '{job.Id}";
                        _logger.Error(errorMessage);
                        throw new Exception(errorMessage);
                    }

                    sessionId = job.Properties[jobDefinition.SessionMessageProperty];
                }
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(jobDefinition.MessageBusQueue))
                {
                    await _messengerService.SendToQueueAsJson(jobDefinition.MessageBusQueue, data, messageProperties, sessionId: sessionId);
                }
                else
                {
                    await _messengerService.SendToTopicAsJson(jobDefinition.MessageBusTopic, data, messageProperties);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to queue job with id: {job.Id} on Queue/topic {queueOrTopic}");
            }
        }

        private bool CanSupersede(Job runningJob, Job replacementJob)
        {
            return string.CompareOrdinal(runningJob.Id, replacementJob.Id) != 0 && (string.IsNullOrWhiteSpace(runningJob.ParentJobId) ||
                   string.CompareOrdinal(runningJob.ParentJobId, replacementJob.ParentJobId) != 0);
        }
    }
}
