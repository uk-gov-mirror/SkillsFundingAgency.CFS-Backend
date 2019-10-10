﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CalculateFunding.Common.Utility;
using CalculateFunding.Models.Aggregations;
using CalculateFunding.Models.Calcs;
using CalculateFunding.Models.Results;
using CalculateFunding.Services.CalcEngine.Interfaces;
using Serilog;
using CalculationResult = CalculateFunding.Models.Results.CalculationResult;

namespace CalculateFunding.Services.CalcEngine
{
    public class CalculationEngine : ICalculationEngine
    {
        private readonly IAllocationFactory _allocationFactory;
        private readonly ICalculationsRepository _calculationsRepository;
        private readonly ILogger _logger;

        public CalculationEngine(IAllocationFactory allocationFactory, ICalculationsRepository calculationsRepository, ILogger logger)
        {
            Guard.ArgumentNotNull(allocationFactory, nameof(allocationFactory));
            Guard.ArgumentNotNull(calculationsRepository, nameof(calculationsRepository));
            Guard.ArgumentNotNull(logger, nameof(logger));

            _allocationFactory = allocationFactory;
            _calculationsRepository = calculationsRepository;
            _logger = logger;
        }

        public IAllocationModel GenerateAllocationModel(Assembly assembly)
        {
            return _allocationFactory.CreateAllocationModel(assembly);
        }

        async public Task<IEnumerable<ProviderResult>> GenerateAllocations(BuildProject buildProject, IEnumerable<ProviderSummary> providers, Func<string, string, Task<IEnumerable<ProviderSourceDataset>>> getProviderSourceDatasets)
        {
            var assembly = Assembly.Load(buildProject.Build.Assembly);

            var allocationModel = _allocationFactory.CreateAllocationModel(assembly);

            ConcurrentBag<ProviderResult> providerResults = new ConcurrentBag<ProviderResult>();

            IEnumerable<CalculationSummaryModel> calculations = await _calculationsRepository.GetCalculationSummariesForSpecification(buildProject.SpecificationId);

            Parallel.ForEach(providers, new ParallelOptions { MaxDegreeOfParallelism = 5 }, provider =>
            {
               var stopwatch = new Stopwatch();
               stopwatch.Start();

               IEnumerable<ProviderSourceDataset> providerSourceDatasets = getProviderSourceDatasets(provider.Id, buildProject.SpecificationId).Result;

               if (providerSourceDatasets == null)
               {
                   providerSourceDatasets = Enumerable.Empty<ProviderSourceDataset>();
               }

               ProviderResult result = CalculateProviderResults(allocationModel, buildProject, calculations, provider, providerSourceDatasets.ToList());

               providerResults.Add(result);

               stopwatch.Stop();
               _logger.Debug($"Generated result for {provider.Name} in {stopwatch.ElapsedMilliseconds}ms");
           });

            return providerResults;
        }

        public ProviderResult CalculateProviderResults(IAllocationModel model, BuildProject buildProject, IEnumerable<CalculationSummaryModel> calculations,
            ProviderSummary provider, IEnumerable<ProviderSourceDataset> providerSourceDatasets, IEnumerable<CalculationAggregation> aggregations = null)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            IEnumerable<CalculationResult> calculationResults = model.Execute(providerSourceDatasets != null ? providerSourceDatasets.ToList() : new List<ProviderSourceDataset>(), provider, aggregations).ToArray();

            var providerCalcResults = calculationResults.ToDictionary(x => x.Calculation?.Id);
            stopwatch.Stop();

            if (providerCalcResults.Count > 0)
            {
                _logger.Debug($"{providerCalcResults.Count} calcs in {stopwatch.ElapsedMilliseconds}ms ({stopwatch.ElapsedMilliseconds / providerCalcResults.Count: 0.0000}ms)");
            }
            else
            {
                _logger.Information("There are no calculations to executed for specification ID {specificationId}", buildProject.SpecificationId);
            }

            ProviderResult providerResult = new ProviderResult
            {
                Provider = provider,
                SpecificationId = buildProject.SpecificationId
            };

            byte[] plainTextBytes = System.Text.Encoding.UTF8.GetBytes($"{providerResult.Provider.Id}-{providerResult.SpecificationId}");
            providerResult.Id = Convert.ToBase64String(plainTextBytes);

            List<CalculationResult> results = new List<CalculationResult>();

            if (calculations != null)
            {
                foreach (CalculationSummaryModel calculation in calculations)
                {
                    CalculationResult result = new CalculationResult
                    {
                        Calculation = calculation.GetReference(),
                        CalculationType = calculation.CalculationType,
                        Version = 0 // This is no longer required for new publishing. Hard coded to 0 for change detection. Previously was calculation.Version
                    };

                    if (providerCalcResults.TryGetValue(calculation.Id, out CalculationResult calculationResult))
                    {
                        result.Calculation.Id = calculationResult.Calculation?.Id;

                        // The default for the calculation is to return Decimal.MinValue - if this is the case, then subsitute a 0 value as the result, instead of the negative number.
                        if (calculationResult.Value != decimal.MinValue)
                        {
                            result.Value = calculationResult.Value;
                        }
                        else
                        {
                            result.Value = 0;
                        }

                        result.ExceptionType = calculationResult.ExceptionType;
                        result.ExceptionMessage = calculationResult.ExceptionMessage;
                        result.ExceptionStackTrace = calculationResult.ExceptionStackTrace;
                    }

                    results.Add(result);
                }
            }

            //we need a stable sort of results to enable the cache checks by overall SHA hash on the results json
            providerResult.CalculationResults = results.OrderBy(_ => _.Calculation.Id).ToList();

            return providerResult;
        }
    }
}
