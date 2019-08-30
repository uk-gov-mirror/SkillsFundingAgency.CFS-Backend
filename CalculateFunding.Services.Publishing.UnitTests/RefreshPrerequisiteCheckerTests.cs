﻿using CalculateFunding.Common.ApiClient.Specifications.Models;
using CalculateFunding.Models.Publishing;
using CalculateFunding.Services.Publishing.Interfaces;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CalculateFunding.Services.Publishing.UnitTests
{
    [TestClass]
    public class RefreshPrerequisiteCheckerTests
    {
        private ISpecificationFundingStatusService _specificationFundingStatusService;
        private ISpecificationService _specificationService;
        private ICalculationEngineRunningChecker _calculationEngineRunningChecker;
        private ICalculationPrerequisiteCheckerService _calculationApprovalCheckerService;
        private ILogger _logger;
        private RefreshPrerequisiteChecker _refreshPrerequisiteChecker;

        private IEnumerable<string> _validationErrors;

        [TestInitialize]
        public void SetUp()
        {
            _specificationFundingStatusService = Substitute.For<ISpecificationFundingStatusService>();
            _specificationService = Substitute.For<ISpecificationService>();
            _calculationEngineRunningChecker = Substitute.For<ICalculationEngineRunningChecker>();
            _calculationApprovalCheckerService = Substitute.For<ICalculationPrerequisiteCheckerService>();
            _logger = Substitute.For<ILogger>();

            _refreshPrerequisiteChecker = new RefreshPrerequisiteChecker(
                _specificationFundingStatusService, 
                _specificationService, 
                _calculationEngineRunningChecker,
                _calculationApprovalCheckerService,
                _logger);
        }

        [TestMethod]
        public void ThrowsArgumentNullException()
        {
            // Arrange

            // Act
            Func<Task> invocation
                = () => WhenThePreRequisitesAreChecked(null);

            // Assert
            invocation
                .Should()
                .Throw<ArgumentNullException>()
                .Where(_ =>
                    _.Message == $"Value cannot be null.{Environment.NewLine}Parameter name: specification");
        }

        [TestMethod]
        public async Task ReturnsErrorMessageWhenSharesAlreadyChoseFundingStream()
        {
            // Arrange
            string specificationId = "specId01";
            SpecificationSummary specificationSummary = new SpecificationSummary { Id = specificationId };

            string errorMessage = $"Specification with id: '{specificationId} already shares chosen funding streams";
            
            GivenTheSpecificationFundingStatusForTheSpecification(specificationSummary, SpecificationFundingStatus.SharesAlreadyChoseFundingStream);
            
            // Act
            await WhenThePreRequisitesAreChecked(specificationSummary);

            // Assert
            _validationErrors
                .Should()
                .Contain(new[]
                {
                    errorMessage
                });

            _logger
                .Received()
                .Error(errorMessage);

        }

        [TestMethod]
        public async Task ReturnsErrorMessageWhenCanChooseSpecificationFundingAndErrorSelectingSpecificationForFunding()
        {
            // Arrange
            string specificationId = "specId01";
            SpecificationSummary specificationSummary = new SpecificationSummary { Id = specificationId };

            string errorMessage = "Generic error message";

            GivenTheSpecificationFundingStatusForTheSpecification(specificationSummary, SpecificationFundingStatus.CanChoose);
            GivenExceptionThrownForSelectSpecificationForFunding(specificationId, new Exception(errorMessage));

            // Act
            await WhenThePreRequisitesAreChecked(specificationSummary);

            // Assert
            _validationErrors
                .Should()
                .Contain(new[]
                {
                    errorMessage
                });

            _logger
                .Received()
                .Error(errorMessage);
        }

        [TestMethod]
        public async Task ReturnsErrorMessageWhenCalculationEngineRunning()
        {
            // Arrange
            string specificationId = "specId01";
            SpecificationSummary specificationSummary = new SpecificationSummary { Id = specificationId };

            string errorMessage = "Calculation engine is still running";

            GivenTheSpecificationFundingStatusForTheSpecification(specificationSummary, SpecificationFundingStatus.AlreadyChosen);
            GivenCalculationEngineRunningStatusForTheSpecification(specificationId, true);
            GivenValidationErrorsForTheSpecification(specificationSummary, Enumerable.Empty<string>());

            // Act
            await WhenThePreRequisitesAreChecked(specificationSummary);

            // Assert
            _validationErrors
                .Should()
                .Contain(new[]
                {
                    errorMessage
                });

            _logger
                .Received()
                .Error(errorMessage);
        }

        [TestMethod]
        public async Task ReturnsErrorMessageWhenCalculationPrequisitesNotMet()
        {
            // Arrange
            string specificationId = "specId01";
            SpecificationSummary specificationSummary = new SpecificationSummary { Id = specificationId };

            string errorMessage = "Error message";

            GivenTheSpecificationFundingStatusForTheSpecification(specificationSummary, SpecificationFundingStatus.AlreadyChosen);
            GivenCalculationEngineRunningStatusForTheSpecification(specificationId, false);
            GivenValidationErrorsForTheSpecification(specificationSummary, new List<string> { errorMessage });

            // Act
            await WhenThePreRequisitesAreChecked(specificationSummary);

            // Assert
            _validationErrors
                .Should()
                .Contain(new[]
                {
                    errorMessage
                });

            _logger
                .Received()
                .Error(errorMessage);
        }

        private void GivenTheSpecificationFundingStatusForTheSpecification(SpecificationSummary specification, SpecificationFundingStatus specificationFundingStatus)
        {
            _specificationFundingStatusService.CheckChooseForFundingStatus(specification)
                .Returns(specificationFundingStatus);
        }

        private void GivenExceptionThrownForSelectSpecificationForFunding(string specificationId, Exception ex)
        {
            _specificationService.SelectSpecificationForFunding(specificationId)
                .Throws(ex);
        }

        private void GivenCalculationEngineRunningStatusForTheSpecification(string specificationId, bool calculationEngineRunningStatus)
        {
            _calculationEngineRunningChecker.IsCalculationEngineRunning(specificationId, Arg.Any<string[]>())
                .Returns(calculationEngineRunningStatus);
        }

        private void GivenValidationErrorsForTheSpecification(SpecificationSummary specification, IEnumerable<string> validationErrors)
        {
            _calculationApprovalCheckerService.VerifyCalculationPrerequisites(specification)
                .Returns(validationErrors);
        }

        private async Task WhenThePreRequisitesAreChecked(SpecificationSummary specification)
        {
            _validationErrors = await _refreshPrerequisiteChecker.PerformPrerequisiteChecks(specification);
        }

    }
}
