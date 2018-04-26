﻿using CalculateFunding.Services.Core.Interfaces.Caching;
using CalculateFunding.Services.Core.Options;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CalculateFunding.Services.Core.Caching
{
    public class StackExchangeRedisClientCacheProvider : ICacheProvider, IDisposable
    {
        readonly RedisSettings _systemCacheSettings;
        readonly Lazy<ConnectionMultiplexer> _connectionMultiplexer;

        public StackExchangeRedisClientCacheProvider(
            RedisSettings systemCacheSettings)
        {
            _systemCacheSettings = systemCacheSettings;

            _connectionMultiplexer = new Lazy<ConnectionMultiplexer>(() =>
            {
                var configurationOptions = ConfigurationOptions.Parse(_systemCacheSettings.CacheConnection);

                var multiplexer = ConnectionMultiplexer.Connect(configurationOptions);
                multiplexer.PreserveAsyncOrder = false;

                return multiplexer;
            });
        }

        async public Task<T> GetAsync<T>(string key, JsonSerializerSettings jsonSerializerSettings)
        {
            key = GenerateCacheKey<T>(key);

            try
            {
                var database = GetDatabase();
                var cachedValue = await database.StringGetAsync(key).ConfigureAwait(false);
                if (!cachedValue.HasValue)
                {
                    return default(T);
                }

                RedisCacheValue<T> redisCacheValue;
                if (jsonSerializerSettings == null)
                {
                    redisCacheValue = JsonConvert.DeserializeObject<RedisCacheValue<T>>(cachedValue);
                }
                else
                {
                    redisCacheValue = JsonConvert.DeserializeObject<RedisCacheValue<T>>(cachedValue, jsonSerializerSettings);
                }

                if (redisCacheValue == null)
                    return default(T);

                if (redisCacheValue.SlidingExpiration.HasValue)
                    await database.KeyExpireAsync(key, redisCacheValue.SlidingExpiration.Value, CommandFlags.FireAndForget).ConfigureAwait(false);

                return (T)redisCacheValue.Value;

            }
            catch (Exception ex)
            {
                return default(T);
            }
        }

        public Task KeyDeleteAsync<T>(string key)
        {
            key = GenerateCacheKey<T>(key);

            var database = GetDatabase();

            return database.KeyDeleteAsync(key);
        }

        async public Task CreateListAsync<T>(IEnumerable<T> items, string key)
        {
            key = GenerateCacheKey<T>(key);

            var database = GetDatabase();

            IList<RedisValue> redisValues = new List<RedisValue>();

            foreach (var item in items)
            {
                var valueToCache = JsonConvert.SerializeObject(item);

                redisValues.Add(valueToCache);
            }
            await database.ListRightPushAsync(key, redisValues.ToArray());
        }

        async public Task<IEnumerable<T>> ListRangeAsync<T>(string key, int start, int stop)
        {
            key = GenerateCacheKey<T>(key);

            var database = GetDatabase();

            var items = await database.ListRangeAsync(key, start, stop);

            IList<T> results = new List<T>();
            try
            {
                foreach (var item in items)
                {
                    T resultItem = JsonConvert.DeserializeObject<T>(item.ToString());
                    results.Add(resultItem);
                }
                return results;
            }
            catch (Exception ex)
            {
                return default(IEnumerable<T>);
            }
        }

        public Task<long> ListLengthAsync<T>(string key)
        {
            key = GenerateCacheKey<T>(key);

            var database = GetDatabase();

            return database.ListLengthAsync(key);
        }

        public Task<bool> KeyExists<T>(string key)
        {
            key = GenerateCacheKey<T>(key);

            var database = GetDatabase();

            return database.KeyExistsAsync(key);
        }

        public Task SetAsync<T>(string key, T item, JsonSerializerSettings jsonSerializerSettings = null)
        {
            var memoryCacheOptions = new MemoryCacheEntryOptions
            {
            };

            return SetAsyncImpl(key, item, memoryCacheOptions, jsonSerializerSettings);
        }

        public Task SetAsync<T>(string key, T item, TimeSpan expiration, bool isSliding, JsonSerializerSettings jsonSerializerSettings = null)
        {
            var memoryCacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = isSliding ? null : (TimeSpan?)expiration,
                SlidingExpiration = isSliding ? (TimeSpan?)expiration : null
            };

            return SetAsyncImpl(key, item, memoryCacheOptions, jsonSerializerSettings);
        }

        public Task SetAsync<T>(string key, T item, DateTimeOffset absoluteExpiration, JsonSerializerSettings jsonSerializerSettings = null)
        {
            var memoryCacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = absoluteExpiration
            };

            return SetAsyncImpl<T>(key, item, memoryCacheOptions, jsonSerializerSettings);
        }

        async Task SetAsyncImpl<T>(string key, T item, MemoryCacheEntryOptions memoryCacheEntryOptions, JsonSerializerSettings jsonSerializerSettings = null)
        {
            if (item == null)
                return;

            key = GenerateCacheKey(item.GetType(), key);

            try
            {

                var redisCacheValue = new RedisCacheValue<T>
                {
                    SlidingExpiration = memoryCacheEntryOptions.SlidingExpiration,
                    Value = item
                };

                if (jsonSerializerSettings == null)
                {
                    jsonSerializerSettings = new JsonSerializerSettings();
                }

                var valueToCache = JsonConvert.SerializeObject(redisCacheValue, Formatting.None, jsonSerializerSettings);

                var expirationTimespan = ConvertToTimeSpan(memoryCacheEntryOptions);
                if (expirationTimespan < TimeSpan.Zero)
                    expirationTimespan = TimeSpan.FromHours(72);

                var database = GetDatabase();
                await database.StringSetAsync(key, valueToCache, expirationTimespan, flags: CommandFlags.FireAndForget).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Write(ex);
            }
        }

        async public Task RemoveAsync<T>(string key)
        {
            key = GenerateCacheKey<T>(key);
            try
            {
                var database = GetDatabase();
                await database.KeyDeleteAsync(key, flags: CommandFlags.FireAndForget).ConfigureAwait(false);
            }

            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        static TimeSpan ConvertToTimeSpan(MemoryCacheEntryOptions memoryCacheEntryOptions)
        {
            if (!memoryCacheEntryOptions.AbsoluteExpiration.HasValue
                && !memoryCacheEntryOptions.AbsoluteExpirationRelativeToNow.HasValue
                && !memoryCacheEntryOptions.SlidingExpiration.HasValue)
                return TimeSpan.FromHours(72);

            if (memoryCacheEntryOptions.AbsoluteExpiration.HasValue)
                return memoryCacheEntryOptions.AbsoluteExpiration.Value - DateTimeOffset.UtcNow;

            if (memoryCacheEntryOptions.AbsoluteExpirationRelativeToNow.HasValue)
                return memoryCacheEntryOptions.AbsoluteExpirationRelativeToNow.Value;

            if (memoryCacheEntryOptions.SlidingExpiration.HasValue)
                return memoryCacheEntryOptions.SlidingExpiration.Value;

            return TimeSpan.FromHours(72);
        }

        static string GenerateCacheKey<T>(string key)
        {
            return GenerateCacheKey(typeof(T), key);
        }

        static string GenerateCacheKey(Type objectType, string key)
        {
            return $"{key}:{objectType.Name}".ToLower();
        }

        IDatabase GetDatabase()
        {
            var multiplexer = _connectionMultiplexer.Value;

            return multiplexer.GetDatabase();
        }

        public void Dispose()
        {
            if (_connectionMultiplexer.IsValueCreated)
                _connectionMultiplexer.Value.Dispose();
        }

        public class RedisCacheValue<T>
        {
            public TimeSpan? SlidingExpiration { get; set; }

            public T Value { get; set; }
        }
    }
}
