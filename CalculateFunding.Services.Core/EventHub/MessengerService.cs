﻿using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CalculateFunding.Services.Core.Helpers;
using CalculateFunding.Services.Core.Interfaces.EventHub;
using CalculateFunding.Services.Core.Options;
using Microsoft.Azure.EventHubs;
using Newtonsoft.Json;

namespace CalculateFunding.Services.Core.EventHub
{
    public class MessengerService : IMessengerService
    {
        private readonly Dictionary<string, EventHubClient> _topicClients = new Dictionary<string, EventHubClient>();
        private readonly string _connectionString;

        private object hubLock = new object();

        public MessengerService(EventHubSettings settings)
        {
            _connectionString = settings.EventHubConnectionString;
        }

        EventHubClient GetEventHubClient(string hubName)
        {
            lock (hubLock)
            {
                if (!_topicClients.TryGetValue(hubName, out var eventHubClient))
                {
                    var connectionStringBuilder = new EventHubsConnectionStringBuilder(_connectionString)
                    {
                        EntityPath = hubName
                    };
                    eventHubClient = EventHubClient.CreateFromConnectionString(connectionStringBuilder.ToString());

                    if (!_topicClients.ContainsKey(hubName))
                    {

                        _topicClients.Add(hubName, eventHubClient);

                    }
                }
                return eventHubClient;
            }

        }


        async public Task SendAsync<T>(string hubName, T data, IDictionary<string, string> properties)
        {
            var eventHubClient = GetEventHubClient(hubName);
           
            var json = JsonConvert.SerializeObject(data);

            EventData message = new EventData(Encoding.UTF8.GetBytes(json));

            foreach (var property in properties)
                message.Properties.Add(property.Key, property.Value);

            await RetryAgent.DoAsync(() => eventHubClient.SendAsync(message));
        }

        async public Task SendBatchAsync<T>(string hubName, IEnumerable<T> items, IDictionary<string, string> properties)
        {
            var eventHubClient = GetEventHubClient(hubName);

            EventDataBatch batch = eventHubClient.CreateBatch();

            foreach (var item in items)
            {
                var json = JsonConvert.SerializeObject(item);
                EventData message = new EventData(Encoding.UTF8.GetBytes(json));

                foreach (var property in properties)
                    message.Properties.Add(property.Key, property.Value);

                if (!batch.TryAdd(message))
                {
                    // batch full? send batch and create a new one
                    await RetryAgent.DoAsync(() => eventHubClient.SendAsync(batch.ToEnumerable()));
                    batch = eventHubClient.CreateBatch();
                    batch.TryAdd(message);
                }
                
            }

            if (batch.Count > 0)
            {
                await RetryAgent.DoAsync(() => eventHubClient.SendAsync(batch.ToEnumerable()));
            }





        }
    }


}
