﻿using System;
using System.Threading.Tasks;
using CalculateFunding.Common.Utility;
using CalculateFunding.Services.Core.Constants;
using CalculateFunding.Services.Core.Functions;
using CalculateFunding.Services.DeadletterProcessor;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Serilog;

namespace CalculateFunding.Functions.Policy.ServiceBus
{
    public class OnReIndexTemplatesFailure : Failure
    {
        private readonly ILogger _logger;
        private readonly IJobHelperService _jobHelperService;
        private const string FunctionName = "on-dataset-validation-event-poisoned";
        private const string QueueName = ServiceBusConstants.QueueNames.PolicyReIndexTemplatesPoisoned;

        public OnReIndexTemplatesFailure(
            ILogger logger,
            IJobHelperService jobHelperService) : base(logger, jobHelperService, QueueName)
        {
        }

        [FunctionName(FunctionName)]
        public async Task Run([ServiceBusTrigger(QueueName, Connection = ServiceBusConstants.ConnectionStringConfigurationKey)] Message message) => await Process(message);
    }
}
