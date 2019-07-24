using System;
using System.Threading.Tasks;
using CalculateFunding.Common.Utility;
using CalculateFunding.Services.Core.Constants;
using CalculateFunding.Services.Specs.Interfaces;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Serilog;

namespace CalculateFunding.Functions.Specs.ServiceBus
{
    public class OnAddRelationshipEvent
    {
        private readonly ILogger _logger;
        private readonly ISpecificationsService _specificationsService;

        public OnAddRelationshipEvent(
            ILogger logger,
            ISpecificationsService specificationsService)
        {
            Guard.ArgumentNotNull(logger, nameof(logger));
            Guard.ArgumentNotNull(specificationsService, nameof(specificationsService));

            _logger = logger;
            _specificationsService = specificationsService;
        }

        [FunctionName("on-add-relationship-event")]
        public async Task Run([ServiceBusTrigger(ServiceBusConstants.QueueNames.AddDefinitionRelationshipToSpecification, Connection = ServiceBusConstants.ConnectionStringConfigurationKey)] Message message)
        {
            try
            {
                await _specificationsService.AssignDataDefinitionRelationship(message);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, $"An error occurred getting message from queue: {ServiceBusConstants.QueueNames.AddDefinitionRelationshipToSpecification}");
                throw;
            }
        }
    }
}
