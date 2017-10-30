using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Allocation.Models;
using Allocations.Engine;
using Allocations.Gherkin.Vocab;
using Allocations.Models.Budgets;
using Allocations.Models.Datasets;
using Allocations.Models.Framework;
using Allocations.Models.Results;
using Allocations.Repository;
using AY1718.CSharp.Allocations;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;

namespace Allocations.Functions.Engine
{
    public static class OnTimerFired
    {
        [FunctionName("OnTimerFired")]
        public static async Task Run([TimerTrigger("0 */5 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            log.Info($"C# Timer trigger function executed at: {DateTime.Now}");
            await GenerateAllocations();
        }

        private static async Task GenerateAllocations()
        {

            using (var repository = new Repository<ProviderSourceDataset>("datasets"))
            {
                var modelName = "SBS1718";


                var datasetsByUrn = repository.Query().ToArray().GroupBy(x => x.ProviderUrn);
                var allocationFactory = new AllocationFactory(typeof(SBSPrimary).Assembly);
                foreach (var urn in datasetsByUrn)
                {
                    var typedDatasets = new List<object>();

                    foreach (var dataset in urn)
                    {
                        var type = allocationFactory.GetDatasetType(dataset.DatasetName);
                        var datasetAsJson = repository.QueryAsJson($"SELECT * FROM ds WHERE ds.id='{dataset.Id}' AND ds.deleted = false").First();


                        object blah = JsonConvert.DeserializeObject(datasetAsJson, type);
                        typedDatasets.Add(blah);
                    }

                    var model =
                        allocationFactory.CreateAllocationModel(modelName);

                    var budgetDefinition = GetBudget();

                    var gherkinValidator = new GherkinValidator(new ProductGherkinVocabulary());


                    var calculationResults = model.Execute(modelName, urn.Key, typedDatasets.ToArray());

                    var providerAllocations = calculationResults.ToDictionary(x => x.ProductName);
                    using (var allocationRepository = new Repository<ProviderResult>("results"))
                    {
                        var result = new ProviderResult
                        {
                            Provider = new Reference(urn.Key, urn.Key),
                            Budget = new Reference(budgetDefinition.Id, budgetDefinition.Name),
                            SourceDatasets = typedDatasets.ToArray()
                        };
                        var productResults = new List<ProductResult>();
                        foreach (var fundingPolicy in budgetDefinition.FundingPolicies)
                        {
                            foreach (var allocationLine in fundingPolicy.AllocationLines)
                            {
                                foreach (var productFolder in allocationLine.ProductFolders)
                                {
                                    foreach (var product in productFolder.Products)
                                    {
                                        var productResult = new ProductResult
                                        {
                                            FundingPolicy = new Reference(fundingPolicy.Id, fundingPolicy.Name),
                                            AllocationLine = new Reference(allocationLine.Id, allocationLine.Name),
                                            ProductFolder = new Reference(productFolder.Id, productFolder.Name),
                                            Product = product

                                        };
                                        if (providerAllocations.ContainsKey(product.Name))
                                        {
                                            productResult.Value = providerAllocations[product.Name].Value;
                                        }

                                        if (product.FeatureFile != null)
                                        {
                                            var errors = gherkinValidator.Validate(GetBudget(), product.FeatureFile).ToArray();
                                        }
                                        productResults.Add(productResult);
                                    }
                                }
                            }
                        }
                        result.ProductResults = productResults.ToArray();
                        await allocationRepository.CreateAsync(result);
                    }



                }
            }
        }

        private static Budget GetBudget()
        {
            using (var repository = new Repository<Budget>("specs"))
            {
                return repository.Read().FirstOrDefault();
            }
        }
    }
}
