using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Threading.Tasks;
using CalculateFunding.Services.Core;
using CalculateFunding.Services.Core.Caching.FileSystem;
using CalculateFunding.Services.Core.Constants;
using CalculateFunding.Services.Core.Interfaces;
using CalculateFunding.Services.Core.Interfaces.AzureStorage;
using CalculateFunding.Services.Publishing.Interfaces;
using CalculateFunding.Services.Publishing.Reporting.FundingLines;
using CalculateFunding.Tests.Common.Helpers;
using FluentAssertions;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.Storage.Blob;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Polly;
using Serilog.Core;

namespace CalculateFunding.Services.Publishing.UnitTests.Reporting.FundingLines
{
    [TestClass]
    public class FundingLineCsvGeneratorTests
    {
        private FundingLineCsvGenerator _service;

        private Mock<IFundingLineCsvTransformServiceLocator> _transformServiceLocator;
        private Mock<IFundingLineCsvBatchProcessorServiceLocator> _batchProcessorServiceLocator;
        private Mock<IPublishedFundingPredicateBuilder> _predicateBuilder;
        private Mock<ICsvUtils> _csvUtils;
        private Mock<IBlobClient> _blobClient;
        private Mock<ICloudBlob> _cloudBlob; 
        private Mock<IFundingLineCsvTransform> _transformation;
        private Mock<IFileSystemAccess> _fileSystemAccess;
        private Mock<IFileSystemCacheSettings> _fileSystemCacheSettings;
        private Mock<IJobTracker> _jobTracker;
        private Mock<IFundingLineCsvBatchProcessor> _batchProcessor;
        private BlobProperties _blobProperties;
        private string _rootPath;

        private Message _message;
        
        [TestInitialize]
        public void SetUp()
        {
            _predicateBuilder = new Mock<IPublishedFundingPredicateBuilder>();
            _transformServiceLocator = new Mock<IFundingLineCsvTransformServiceLocator>();
            _batchProcessorServiceLocator = new Mock<IFundingLineCsvBatchProcessorServiceLocator>();
            _batchProcessor = new Mock<IFundingLineCsvBatchProcessor>();
            _blobClient = new Mock<IBlobClient>();
            _csvUtils = new Mock<ICsvUtils>();
            _transformation = new Mock<IFundingLineCsvTransform>();
            _cloudBlob = new Mock<ICloudBlob>();
            _fileSystemAccess = new Mock<IFileSystemAccess>();
            _fileSystemCacheSettings = new Mock<IFileSystemCacheSettings>();
            _jobTracker = new Mock<IJobTracker>();
            
            _service = new FundingLineCsvGenerator(_transformServiceLocator.Object,
                _predicateBuilder.Object,
                _blobClient.Object,
                _fileSystemAccess.Object,
                _fileSystemCacheSettings.Object,
                _batchProcessorServiceLocator.Object,
                new ResiliencePolicies
                {
                    BlobClient = Policy.NoOpAsync()
                },
                _jobTracker.Object,
                Logger.None);
            
            _message = new Message();
            _rootPath = NewRandomString();

            _fileSystemCacheSettings.Setup(_ => _.Path)
                .Returns(_rootPath);

            _fileSystemAccess.Setup(_ => _.Append(It.IsAny<string>(), 
                    It.IsAny<string>(), default))
                .Returns(Task.CompletedTask);
            
            _blobProperties = new BlobProperties();

            _cloudBlob.Setup(_ => _.Properties)
                .Returns(_blobProperties);
        }

        [TestMethod]
        public void ThrowsExceptionIfNoSpecificationIdInMessageProperties()
        {
            Func<Task> invocation = WhenTheCsvIsGenerated;

            invocation
                .Should()
                .ThrowAsync<NonRetriableException>()
                .WithMessage("Specification id missing");
        }
        
        [TestMethod]
        public void ThrowsExceptionIfNoJobTypeInMessageProperties()
        {
            GivenTheMessageProperties(("specification-id", NewRandomString()));
            
            Func<Task> invocation = WhenTheCsvIsGenerated;

            invocation
                .Should()
                .ThrowAsync<NonRetriableException>()
                .WithMessage("Specification id missing");
        }
        [TestMethod]
        public void ThrowsExceptionIfNoJobIdInMessageProperties()
        {
            GivenTheMessageProperties(("specification-id", NewRandomString()), ("job-type", "History"));
            
            Func<Task> invocation = WhenTheCsvIsGenerated;

            invocation
                .Should()
                .ThrowAsync<NonRetriableException>()
                .WithMessage("Job id missing");
        }

        [TestMethod]
        public async Task ExitsEarlyIfNoProvidersMatchForTheJobTypePredicate()
        {
            string specificationId = NewRandomString();
            string fundingLineCode = NewRandomString();
            string jobId = NewRandomString();
            string expectedInterimFilePath = Path.Combine(_rootPath, $"funding-lines-{specificationId}-Released-{fundingLineCode}.csv");
            FundingLineCsvGeneratorJobType jobType = FundingLineCsvGeneratorJobType.Released;

            GivenTheMessageProperties(("specification-id", specificationId), ("job-type", jobType.ToString()), ("jobId", jobId), ("funding-line-code", fundingLineCode));
            AndTheFileExists(expectedInterimFilePath);
            AndTheJobExists(jobId);
            AndTheBatchProcessorForJobType(jobType);

            await WhenTheCsvIsGenerated();
            
            _jobTracker.Verify(_ => _.TryStartTrackingJob(jobId, JobConstants.DefinitionNames.GeneratePublishedFundingCsvJob),
                Times.Once);

            _fileSystemAccess
                .Verify(_ => _.Delete(expectedInterimFilePath),
                    Times.Once);

            _fileSystemAccess
                .Verify(_ => _.Append(expectedInterimFilePath,
                        It.IsAny<string>(),
                        default),
                    Times.Never);

            _blobClient
                .Verify(_ => _.UploadAsync(_cloudBlob.Object, It.IsAny<Stream>()),
                    Times.Never);

            _jobTracker.Verify(_ => _.CompleteTrackingJob(jobId),
                Times.Once);
        }

        [TestMethod]
        [DataRow(FundingLineCsvGeneratorJobType.CurrentState, "spec1", null, null, "AY-1920",
            "funding-lines-spec1-CurrentState.csv",
            " AY-1920 Provider Funding Lines Current State")]
        [DataRow(FundingLineCsvGeneratorJobType.Released, "spec2", "FL1", "DSG", "AY-2020",
            "funding-lines-spec2-Released-FL1-DSG.csv",
            "DSG AY-2020 Provider Funding Lines Released Only")]
        [DataRow(FundingLineCsvGeneratorJobType.CurrentProfileValues, "spec3", null, "PSG", "AY-2021",
            "funding-lines-spec3-CurrentProfileValues-PSG.csv",
            "PSG AY-2021  Profile Current State")]
        public async Task TransformsPublishedProvidersForSpecificationInBatchesAndCreatesCsvWithResults(
            FundingLineCsvGeneratorJobType jobType,
            string specificationId,
            string fundingLineCode,
            string fundingStreamId,
            string fundingPeriodId,
            string expectedFileName,
            string expectedContentDisposition)
        {
            string jobId = NewRandomString();
            string expectedInterimFilePath = Path.Combine(_rootPath, expectedFileName);
            
            MemoryStream incrementalFileStream = new MemoryStream();

            string predicate = NewRandomString();

            GivenTheMessageProperties(("specification-id", specificationId), 
                ("job-type", jobType.ToString()), 
                ("jobId", jobId), 
                ("funding-line-code", fundingLineCode), 
                ("funding-period-id", fundingPeriodId), 
                ("funding-stream-id", fundingStreamId));
            AndTheCloudBlobForFileName(expectedFileName);
            AndTheFileStream(expectedInterimFilePath, incrementalFileStream);
            AndTheFileExists(expectedInterimFilePath);
            AndTheTransformForJobType(jobType);
            AndThePredicate(jobType, predicate);
            AndTheJobExists(jobId);
            AndTheBatchProcessorForJobType(jobType);
            AndTheBatchProcessorProcessedResults(jobType, specificationId, expectedInterimFilePath, fundingLineCode, fundingStreamId);

            await WhenTheCsvIsGenerated();
            
            _jobTracker.Verify(_ => _.TryStartTrackingJob(jobId, JobConstants.DefinitionNames.GeneratePublishedFundingCsvJob),
                Times.Once);
            
            _fileSystemAccess
                .Verify(_ => _.Delete(expectedInterimFilePath),
                    Times.Once);

            _blobProperties?.ContentDisposition
                .Should()
                .StartWith($"attachment; filename={expectedContentDisposition} {DateTimeOffset.UtcNow:yyyy-MM-dd}");
            
            _blobClient
                .Verify(_ => _.UploadAsync(_cloudBlob.Object, incrementalFileStream),
                    Times.Once);
            
            _jobTracker.Verify(_ => _.CompleteTrackingJob(jobId),
                Times.Once);
        }

        private void AndThePredicate(FundingLineCsvGeneratorJobType jobType, string predicate)
        {
            _predicateBuilder.Setup(_ => _.BuildPredicate(jobType))
                .Returns(predicate);
        }

        private void AndTheJobExists(string jobId)
        {
            _jobTracker.Setup(_ => _.TryStartTrackingJob(jobId, JobConstants.DefinitionNames.GeneratePublishedFundingCsvJob))
                .ReturnsAsync(true);
        }

        private void AndTheBatchProcessorProcessedResults(FundingLineCsvGeneratorJobType jobType,
            string specificationId,
            string filePath,
            string fundingLineCode,
            string fundingStreamId)
        {
            _batchProcessor.Setup(_ => _.GenerateCsv(jobType, specificationId, filePath, _transformation.Object, fundingLineCode, fundingStreamId))
                .ReturnsAsync(true)
                .Verifiable();
        }

        private void AndTheBatchProcessorForJobType(FundingLineCsvGeneratorJobType jobType)
        {
            _batchProcessorServiceLocator.Setup(_ => _.GetService(jobType))
                .Returns(_batchProcessor.Object);
        }

        private void AndTheTransformForJobType(FundingLineCsvGeneratorJobType jobType)
        {
            _transformServiceLocator.Setup(_ => _.GetService(jobType))
                .Returns(_transformation.Object);
        }

        private static IEnumerable<object[]> JobTypeExamples()
        {
            yield return new object [] {FundingLineCsvGeneratorJobType.CurrentState};
            yield return new object [] {FundingLineCsvGeneratorJobType.Released};
            yield return new object [] {FundingLineCsvGeneratorJobType.History};
        }

        private void AndTheCloudBlobForFileName(string fileName)
        {
            _blobClient
                .Setup(_ => _.GetBlockBlobReference(fileName))
                .Returns(_cloudBlob.Object);
        }

        private void AndTheFileStream(string path, Stream stream)
        {
            _fileSystemAccess.Setup(_ => _.OpenRead(path))
                .Returns(stream);
        }

        private void AndTheFileExists(string path)
        {
            _fileSystemAccess.Setup(_ => _.Exists(path))
                .Returns(true);
        }

        private void AndTheCsvRowTransformation(IEnumerable<dynamic> publishedProviders, ExpandoObject[] transformedRows, string csv, bool outputHeaders)
        {
            GivenTheCsvRowTransformation(publishedProviders, transformedRows, csv, outputHeaders);
        }

        private void GivenTheCsvRowTransformation(IEnumerable<dynamic> publishedProviders, IEnumerable<ExpandoObject> transformedRows, string csv, bool outputHeaders)
        {
            _transformation
                .Setup(_ => _.Transform(publishedProviders))
                .Returns(transformedRows);

            _csvUtils
                .Setup(_ => _.AsCsv(transformedRows, outputHeaders))
                .Returns(csv);
        }

        private static RandomString NewRandomString()
        {
            return new RandomString();
        }

        private async Task WhenTheCsvIsGenerated()
        {
            await _service.Run(_message);
        }

        private void GivenTheMessageProperties(params (string,string)[] properties)
        {
            _message.AddUserProperties(properties);
        }   
    }
}