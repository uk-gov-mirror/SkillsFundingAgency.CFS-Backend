﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CalculateFunding.Common.Utility;
using CalculateFunding.Models.Graph;
using CalculateFunding.Services.Graph.Interfaces;
using Microsoft.AspNetCore.Mvc;
using CalculateFunding.Services.Core.Extensions;
using CalculateFunding.Services.Core.Helpers;
using Serilog;
using Neo4jDriver = Neo4j.Driver;

namespace CalculateFunding.Services.Graph
{
    public class GraphService : IGraphService
    {
        private readonly ILogger _logger;
        private readonly ICalculationRepository _calcRepository;
        private readonly ISpecificationRepository _specRepository;
        private readonly IDatasetRepository _datasetRepository;
        
        public GraphService(ILogger logger, 
            ICalculationRepository calcRepository, 
            ISpecificationRepository specRepository, 
            IDatasetRepository datasetRepository)
        {
            Guard.ArgumentNotNull(logger, nameof(logger));
            Guard.ArgumentNotNull(calcRepository, nameof(calcRepository));
            Guard.ArgumentNotNull(specRepository, nameof(specRepository));
            Guard.ArgumentNotNull(datasetRepository, nameof(datasetRepository));

            _logger = logger;
            _calcRepository = calcRepository;
            _specRepository = specRepository;
            _datasetRepository = datasetRepository;
        }

        public async Task<IActionResult> UpsertDataset(Dataset dataset)
        {
            return await ExecuteRepositoryAction(() => _datasetRepository.UpsertDataset(dataset),
                $"Unable to create dataset {dataset.AsJson()}");
        }

        public async Task<IActionResult> DeleteDataset(string datasetId)
        {
            return await ExecuteRepositoryAction(() => _datasetRepository.DeleteDataset(datasetId),
                $"Unable to delete dataset {datasetId}");
        }
        
        public async Task<IActionResult> UpsertDatasetDefinition(DatasetDefinition definition)
        {
            return await ExecuteRepositoryAction(() => _datasetRepository.UpsertDatasetDefinition(definition),
                $"Unable to create dataset definition {definition.AsJson()}");
        }

        public async Task<IActionResult> DeleteDatasetDefinition(string definitionId)
        {
            return await ExecuteRepositoryAction(() => _datasetRepository.DeleteDatasetDefinition(definitionId),
                $"Unable to delete dataset definition {definitionId}");
        }
        
        public async Task<IActionResult> UpsertDataField(DataField field)
        {
            return await ExecuteRepositoryAction(() => _datasetRepository.UpsertDataField(field),
                $"Unable to create data field {field.AsJson()}");
        }

        public async Task<IActionResult> DeleteDataField(string fieldId)
        {
            return await ExecuteRepositoryAction(() => _datasetRepository.DeleteDataField(fieldId),
                $"Unable to delete data field {fieldId}");
        }

        public async Task<IActionResult> UpsertDataDefinitionDatasetRelationship(string definitionId, string datasetId)
        {
            return await ExecuteRepositoryAction(() => _datasetRepository.CreateDataDefinitionDatasetRelationship(definitionId, datasetId),
                $"Unable to create dataset defition -> dataset relationship {definitionId} -> {datasetId}");
        }
        
        public async Task<IActionResult> DeleteDataDefinitionDatasetRelationship(string definitionId, string datasetId)
        {
            return await ExecuteRepositoryAction(() => _datasetRepository.DeleteDataDefinitionDatasetRelationship(definitionId, datasetId),
                $"Unable to delete dataset defition -> dataset relationship {definitionId} -> {datasetId}");
        }

        public async Task<IActionResult> UpsertDatasetDataFieldRelationship(string datasetId, string fieldId)
        {
            return await ExecuteRepositoryAction(() => _datasetRepository.CreateDatasetDataFieldRelationship(datasetId, fieldId),
                $"Unable to create dataset -> datafield relationship {datasetId} -> {fieldId}");
        }
        
        public async Task<IActionResult> DeleteDatasetDataFieldRelationship(string datasetId, string fieldId)
        {
            return await ExecuteRepositoryAction(() => _datasetRepository.DeleteDatasetDataFieldRelationship(datasetId, fieldId),
                $"Unable to delete dataset -> datafield relationship {datasetId} -> {fieldId}");
        }

        public async Task<IActionResult> CreateSpecificationDatasetRelationship(string specificationId, string datasetId)
        {
            return await ExecuteRepositoryAction(() => _specRepository.CreateSpecificationDatasetRelationship(specificationId, datasetId),
                $"Unable to create specification -> dataset relationship {specificationId} -> {datasetId}");   
        }
        
        public async Task<IActionResult> DeleteSpecificationDatasetRelationship(string specificationId, string datasetId)
        {
            return await ExecuteRepositoryAction(() => _specRepository.DeleteSpecificationDatasetRelationship(specificationId, datasetId),
                $"Unable to delete specification -> dataset relationship {specificationId} -> {datasetId}");   
        }
        
        public async Task<IActionResult> CreateCalculationDataFieldRelationship(string calculationId, string fieldId)
        {
            return await ExecuteRepositoryAction(() => _calcRepository.CreateCalculationDataFieldRelationship(calculationId, fieldId),
                $"Unable to create calculation -> datafield relationship {calculationId} -> {fieldId}");   
        }
        
        public async Task<IActionResult> DeleteCalculationDataFieldRelationship(string calculationId, string fieldId)
        {
            return await ExecuteRepositoryAction(() => _calcRepository.DeleteCalculationDataFieldRelationship(calculationId, fieldId),
                $"Unable to delete calculation -> datafield relationship {calculationId} -> {fieldId}");   
        }
        
        public async Task<IActionResult> DeleteAllForSpecification(string specificationId)
        {
            return await ExecuteRepositoryAction(() => _specRepository.DeleteAllForSpecification(specificationId),
                $"Delete all for specification failed for specification:'{specificationId}'");
        }

        public async Task<IActionResult> DeleteSpecification(string specificationId)
        {
            return await ExecuteRepositoryAction(() => _specRepository.DeleteSpecification(specificationId),
                $"Delete specification failed for specification:'{specificationId}'");
        }
        public async Task<IActionResult> UpsertDatasetField(IEnumerable<DatasetField> datasetFields)
        {
            return await ExecuteRepositoryAction(() => _datasetRepository.UpsertDatasetField(datasetFields),
                $"Save datasetfields failed for specifications:'{datasetFields.AsJson()}'");
        }


        public async Task<IActionResult> UpsertSpecifications(IEnumerable<Specification> specifications)
        {
            return await ExecuteRepositoryAction(() => _specRepository.UpsertSpecifications(specifications),
                $"Save specifications failed for specifications:'{specifications.AsJson()}'");
        }

        public async Task<IActionResult> DeleteCalculation(string calculationId)
        {
            return await ExecuteRepositoryAction(() => _calcRepository.DeleteCalculation(calculationId),
                $"Delete calculation failed for calculation:'{calculationId}'");
        }

        public async Task<IActionResult> UpsertCalculations(IEnumerable<Calculation> calculations)
        {
            return await ExecuteRepositoryAction(() => _calcRepository.UpsertCalculations(calculations),
                $"Upsert calculations failed for calculations:'{calculations.AsJson()}'");
        }

        public async Task<IActionResult> UpsertCalculationSpecificationRelationship(string calculationId, string specificationId)
        {
            return await ExecuteRepositoryAction(() => _calcRepository.UpsertCalculationSpecificationRelationship(calculationId, specificationId),
                $"Upsert calculation relationship between specification failed for calculation:'{calculationId}'" +
                $" and specification:'{specificationId}'");
        }

        public async Task<IActionResult> GetCalculationCircularDependencies(string specificationId)
        {
            return await ExecuteRepositoryAction(() => _calcRepository.GetCalculationCircularDependencies(specificationId), "Unable to retrieve calculation circulardependencies.");
        }

        public async Task<IActionResult> GetAllEntities<TNode>(string nodeId)
            where TNode : class
        {
            if(typeof(TNode).IsAssignableFrom(typeof(Specification)))
            {
                return await ExecuteRepositoryAction(() => _specRepository.GetAllEntities(nodeId), $"Unable to retrieve all entities for {typeof(TNode).Name.ToLowerInvariant()}.");
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public async Task<IActionResult> UpsertCalculationCalculationRelationship(string calculationIdA, string calculationIdB)
        {
            return await ExecuteRepositoryAction(() => _calcRepository.UpsertCalculationCalculationRelationship(calculationIdA, calculationIdB),
                $"Upsert calculation relationship call to calculation failed for calculation:'{calculationIdA}'" +
                $" calling calculation:'{calculationIdB}'");
        }

        public async Task<IActionResult> UpsertCalculationCalculationsRelationships(string calculationId, string[] calculationIds)
        {
            try
            {
                IEnumerable<Task> tasks = calculationIds.Select(async(_) =>
                {
                    await _calcRepository.UpsertCalculationCalculationRelationship(calculationId, _);
                });

                await TaskHelper.WhenAllAndThrow(tasks.ToArray());

                return new OkResult();
            }
            catch (Neo4jDriver.Neo4jException ex)
            {
                _logger.Error(ex, $"Upsert calculation relationship call to calculation failed for calculation:'{calculationId}'" +
                                  $" calling calculations:'{calculationIds.AsJson()}'");

                throw;
            }
        }

        public async Task<IActionResult> DeleteCalculationSpecificationRelationship(string calculationId, string specificationId)
        {
            return await ExecuteRepositoryAction(() => _calcRepository.DeleteCalculationSpecificationRelationship(calculationId, specificationId),
                $"Delete calculation relationship between specification failed for calculation:'{calculationId}'" +
                $" and specification:'{specificationId}'");
        }

        public async Task<IActionResult> DeleteCalculationCalculationRelationship(string calculationIdA, string calculationIdB)
        {
            return await ExecuteRepositoryAction(() => _calcRepository.DeleteCalculationCalculationRelationship(calculationIdA, calculationIdB),
                $"Delete calculation relationship call to calculation failed for calculation:'{calculationIdA}'" +
                $" calling calculation:'{calculationIdB}'");
        }

        public async Task<IActionResult> UpsertCalculationDatasetFieldRelationship(string calculationId, string datasetFieldId)
        {
            return await ExecuteRepositoryAction(() => _datasetRepository.UpsertCalculationDatasetFieldRelationship(calculationId, datasetFieldId),
                $"Upsert datasetField relationship call to calculation failed for calculation:'{calculationId}'" +
                $" calling calculation:'{datasetFieldId}'");
        }

        public async Task<IActionResult> UpsertCalculationDatasetFieldsRelationships(string calculationId, string[] datasetFieldIds)
        {
            try
            {
                IEnumerable<Task> tasks = datasetFieldIds.Select(async (_) =>
                {
                    await _datasetRepository.UpsertCalculationDatasetFieldRelationship(calculationId, _);
                });

                await TaskHelper.WhenAllAndThrow(tasks.ToArray());

                return new OkResult();
            }
            catch (Neo4jDriver.Neo4jException ex)
            {
                _logger.Error(ex, $"Upsert datasetfield relationship call to calculation failed for calculation:'{calculationId}'" +
                                  $" calling calculations:'{datasetFieldIds.AsJson()}'");

                throw;
            }
        }

        public async Task<IActionResult> DeleteCalculationDatasetFieldRelationship(string calculationId, string datasetFieldId)
        {
            return await ExecuteRepositoryAction(() => _datasetRepository.DeleteCalculationDatasetFieldRelationship(calculationId, datasetFieldId),
                $"Delete calculation relationship call to datasetfield failed for calculation:'{calculationId}'" +
                $" calling calculation:'{datasetFieldId}'");
        }

        public async Task<IActionResult> DeleteDatasetField(string datasetFieldId)
        {
            return await ExecuteRepositoryAction(() => _datasetRepository.DeleteDatasetField(datasetFieldId),
                $"Unable to delete datasetField {datasetFieldId}");
        }

        private async Task<IActionResult> ExecuteRepositoryAction<T>(Func<Task<T>> action, string errorMessage)
        {
            try
            {
                T results =  await action();

                return new OkObjectResult(results);
            }
            catch (Exception e)
            {
                _logger.Error(e, errorMessage);

                throw;
            }
        }

        private async Task<IActionResult> ExecuteRepositoryAction(Func<Task> action, string errorMessage)
        {
            try
            {
                await action();

                return new OkResult();
            }
            catch (Exception e)
            {
                _logger.Error(e, errorMessage);

                throw;
            }
        }
    }
}
