using System;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CalculateFunding.Functions.Common;
using CalculateFunding.Repositories.Common.Sql;
using CalculateFunding.Repositories.Providers;
using CalculateFunding.Services.DataImporter;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.EntityFrameworkCore;
using ProviderCommand = CalculateFunding.Models.Providers.ProviderCommand;

namespace CalculateFunding.Functions.Providers
{
    public static class OnSourceUpdated
    {
        [FunctionName("OnSourceUpdated")]
        public static async Task RunAsync([BlobTrigger("edubase/{name}", Connection = "ProvidersStorage")]Stream blob, string name, TraceWriter log)
        {


            var stopWatch = new Stopwatch();
            stopWatch.Start();
            var importer = new EdubaseImporterService();
            using (var reader = new StreamReader(blob))
            {
                var providers = importer.ImportEdubaseCsv(name, reader);

                var dbContext = ServiceFactory.GetService<ProvidersDbContext>();

                var command = new Repositories.Providers.ProviderCommandEntity();


                var addResult = await dbContext.ProviderCommands.AddAsync(command);
                await dbContext.SaveChangesAsync();
                stopWatch.Stop();
                log.Info($"Read {name} in {stopWatch.ElapsedMilliseconds}ms");
                stopWatch.Restart();

                var events = (await dbContext.Upsert(addResult.Entity.Id, providers.Select(x =>
                    new ProviderCommandCandidateEntity
                    {
                        ProviderCommandId = command.Id,
                        CreatedAt = DateTimeOffset.Now,
                        UpdatedAt = DateTimeOffset.Now,
                        URN = x.URN,
                        Name = x.Name,
                        Address3 = x.Address3,
                        Deleted = false
                    }))).ToList();

                stopWatch.Stop();
                log.Info($"Bulk Inserted with {events.Count} changes in {stopWatch.ElapsedMilliseconds}ms");
            }

            log.Info($"C# Blob trigger function Processed blob\n Name:{name} \n Size: {blob.Length} Bytes");

        }
    }
}
