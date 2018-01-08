﻿using System;
using System.Threading.Tasks;
using System.Web.Http;
using CalculateFunding.Models;
using CalculateFunding.Repositories.Common.Cosmos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace CalculateFunding.Functions.Common
{
    public class RestCommandMethods<TEntity, TCommand> : RestCommandMethods<TEntity, TCommand, TEntity> where TEntity : class, IIdentifiable where TCommand : Command<TEntity>
    {

        public RestCommandMethods(string topicName) : base(topicName)
        {

            UpdateTarget = (source, target) => target.Content;
        }
    }

    public class RestCommandMethods<TEntity, TCommand, TCommandEntity> where TEntity : class, IIdentifiable where TCommandEntity : IIdentifiable where TCommand : Command<TCommandEntity>
    {
        private readonly string _topicName;

        public RestCommandMethods(string topicName)
        {
            _topicName = topicName;
            GetEntityId = command => command.Content.Id;
        }
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented

        };
        public async Task<IActionResult> Run(HttpRequest req, ILogger logger)
        {
            var json = await req.ReadAsStringAsync();
            var command = JsonConvert.DeserializeObject<TCommand>(json, SerializerSettings);

            if (command == null)
            {
                logger.LogInformation("command is null");
                return new BadRequestErrorMessageResult("Please ensure command is passed in the request body");
            }

            switch (command.Method)
            {
                case CommandMethod.Post:
                case CommandMethod.Put:
                    return await OnPost(command, logger);
                case CommandMethod.Delete:
                    return await OnDelete(command, logger);
                default:
                    return new BadRequestErrorMessageResult($"{command.Method} is not a supported method");

            }

        }

        public async Task<IActionResult> OnPost(TCommand command, ILogger logger)
        {
            logger.LogInformation($"Processing {typeof(TCommand).Name} POST command {command.Id}");
            var repository = ServiceFactory.GetService<CosmosRepository>();
            var messenger = ServiceFactory.GetService<IMessenger>();

            await repository.EnsureCollectionExists();
            var current = await repository.ReadAsync<TEntity>(GetEntityId(command));
            if (current?.Content != null)
            {
                TEntity updatedContent = UpdateTarget(current.Content, command);
                if (!IsModified(current.Content, updatedContent))
                {
                    logger.LogInformation($"{command.TargetDocumentType}:{command.Content.Id} has not been modified");
                    return new StatusCodeResult(304);
                }
            }
            await repository.CreateAsync(command.Content);
            await repository.CreateAsync(command);
            await messenger.SendAsync(_topicName, command);
            logger.LogInformation($"{command.TargetDocumentType}:{command.Content.Id} has been updated");
            // send SB message

            return new AcceptedResult();
        }

        public Func<TEntity, TCommand, TEntity> UpdateTarget { get; set; }
        public Func<TCommand, string> GetEntityId { get; set; }


        private async Task<IActionResult> OnDelete(TCommand command, ILogger logger)
        {
            logger.LogInformation($"Processing {typeof(TCommand).Name} DELETE command {command.Id}");
            var repository = ServiceFactory.GetService<CosmosRepository>();
            var messenger = ServiceFactory.GetService<IMessenger>();
            await repository.EnsureCollectionExists();
            var current = await repository.ReadAsync<TEntity>(GetEntityId(command));
            if (current.Content != null)
            {
                if (current.Deleted)
                { 
                    return new StatusCodeResult(304);
                }
            }
            current.Deleted = true;
            await repository.CreateAsync(current.Content);
            await repository.CreateAsync(command);
            await messenger.SendAsync(_topicName, command);
            // send SB messageB

            return new AcceptedResult();
        }

        private static bool IsModified<TAny>(TAny current, TAny item)
        {
            return JsonConvert.SerializeObject(current) == JsonConvert.SerializeObject(item);
        }

    }
}