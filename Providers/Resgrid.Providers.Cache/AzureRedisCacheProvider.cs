using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Providers;
using StackExchange.Redis;

namespace Resgrid.Providers.Cache
{
	public class AzureRedisCacheProvider : ICacheProvider
	{
		private static ConnectionMultiplexer _connection = null;
		public AzureRedisCacheProvider()
		{
			if (Config.SystemBehaviorConfig.CacheEnabled)
				EstablishRedisConnection();
		}

		public T Retrieve<T>(string cacheKey, Func<T> fallbackFunction, TimeSpan expiration)
			where T : class
		{
			T data = null;
			IDatabase cache = null;

			try
			{
				if (Config.SystemBehaviorConfig.CacheEnabled && _connection != null && _connection.IsConnected)
				{
					cache = _connection.GetDatabase();

					var cacheValue = cache.StringGet(SetCacheKeyForEnv(cacheKey));

					try
					{
						// See RetrieveAsync: a zero-byte payload deserializes into a blank-but-non-null
						// instance, so it must be treated as a miss rather than a hit.
						if (cacheValue.HasValue && !cacheValue.IsNullOrEmpty)
						{
							data = ObjectSerialization.Deserialize<T>(cacheValue);
						}
						else if (cacheValue.HasValue)
						{
							Logging.LogWarning($"Retrieve(): empty cached payload for key {cacheKey}; treating as a miss and removing the key.");
							Remove(cacheKey);
						}
					}
					catch (Exception deserializeEx)
					{
						Logging.LogException(deserializeEx);
						Remove(cacheKey);
						data = null;
					}

					if (data != null)
						return data;
				}
			}
			catch (TimeoutException)
			{ }
			catch (RedisConnectionException ex)
			{
				// Transient connection drop (idle reset, failover); fallback below handles it.
				Logging.LogError(ex);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			data = fallbackFunction();

			if (Config.SystemBehaviorConfig.CacheEnabled && _connection != null && _connection.IsConnected)
			{
				if (data != null && cache != null)
				{
					try
					{
						cache.StringSet(SetCacheKeyForEnv(cacheKey), ObjectSerialization.Serialize(data), expiration);
					}
					catch (TimeoutException)
					{ }
					catch (RedisConnectionException ex)
					{
						Logging.LogError(ex);
					}
					catch (Exception ex)
					{
						// See RetrieveAsync: a failed write-back only costs the cache entry, never the request.
						Logging.LogError(ex, $"Cache write failed for key {cacheKey} (type {typeof(T).FullName}).");
					}
				}
			}

			return data;
		}

		public void Remove(string cacheKey)
		{
			try
			{
				if (Config.SystemBehaviorConfig.CacheEnabled && _connection != null && _connection.IsConnected)
				{
					IDatabase cache = _connection.GetDatabase();
					cache.KeyDelete(SetCacheKeyForEnv(cacheKey));
				}
			}
			catch (TimeoutException)
			{ }
			catch (RedisConnectionException ex)
			{
				Logging.LogError(ex);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}


		public async Task<T> RetrieveAsync<T>(string cacheKey, Func<Task<T>> fallbackFunction, TimeSpan expiration)
		{
			T data = default;
			var hasCachedData = false;
			IDatabase cache = null;

			try
			{
				if (Config.SystemBehaviorConfig.CacheEnabled && _connection != null && _connection.IsConnected)
				{
					cache = _connection.GetDatabase();

					var cacheValue = await cache.StringGetAsync(SetCacheKeyForEnv(cacheKey));

					try
					{
						// An empty payload is not a hit: protobuf-net turns zero bytes into a fully-default
						// instance (non-null, every field null/0) that reads downstream as a real record -- e.g.
						// a Department with DepartmentId 0 and a null ManagingUserId. Treat it as a miss and drop
						// the key so the fallback result replaces it.
						if (cacheValue.HasValue && !cacheValue.IsNullOrEmpty)
						{
							data = ObjectSerialization.Deserialize<T>(cacheValue);
							hasCachedData = data is not null;
						}
						else if (cacheValue.HasValue)
						{
							Logging.LogWarning($"RetrieveAsync(): empty cached payload for key {cacheKey}; treating as a miss and removing the key.");
							await RemoveAsync(cacheKey);
						}
					}
					catch (Exception deserializeEx)
					{
						Logging.LogException(deserializeEx);
						await RemoveAsync(cacheKey);
						data = default;
						hasCachedData = false;
					}

					if (hasCachedData)
						return data;
				}
			}
			catch (TimeoutException)
			{ }
			catch (RedisConnectionException ex)
			{
				// Transient connection drop (idle reset, failover); fallback below handles it.
				Logging.LogError(ex);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			data = await fallbackFunction();

			if (Config.SystemBehaviorConfig.CacheEnabled && _connection != null && _connection.IsConnected)
			{
				if (data is not null && cache != null)
				{
					try
					{
						await cache.StringSetAsync(SetCacheKeyForEnv(cacheKey), ObjectSerialization.Serialize(data), expiration);
					}
					catch (TimeoutException)
					{ }
					catch (RedisConnectionException ex)
					{
						Logging.LogError(ex);
					}
					catch (Exception ex)
					{
						// The caller already has its data, so a failed write-back is never fatal -- it just means
						// this key silently never caches. Error, not Fatal, and name the type: the usual cause is
						// a cached type missing [ProtoContract], which is otherwise only visible deep in a
						// protobuf-net stack trace.
						Logging.LogError(ex, $"Cache write failed for key {cacheKey} (type {typeof(T).FullName}).");
					}
				}
			}

			return data;
		}

		public async Task<bool> SetStringAsync(string cacheKey, string value, TimeSpan expiration)
		{
			IDatabase cache = null;

			if (Config.SystemBehaviorConfig.CacheEnabled && _connection != null && _connection.IsConnected)
			{
				cache = _connection.GetDatabase();

				if (value != null && cache != null)
				{
					try
					{
						return await cache.StringSetAsync(SetCacheKeyForEnv(cacheKey), value, expiration);
					}
					catch (TimeoutException)
					{ }
					catch (RedisConnectionException ex)
					{
						Logging.LogError(ex);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
					}
				}
			}

			return false;
		}

		public async Task<string> GetStringAsync(string cacheKey)
		{
			IDatabase cache = null;

			if (Config.SystemBehaviorConfig.CacheEnabled && _connection != null && _connection.IsConnected)
			{
				cache = _connection.GetDatabase();

				if (cache != null)
				{
					try
					{
						var cacheValue = await cache.StringGetAsync(SetCacheKeyForEnv(cacheKey));

						if (cacheValue.HasValue)
							return cacheValue.ToString();
					}
					catch (TimeoutException)
					{ }
					catch (RedisConnectionException ex)
					{
						Logging.LogError(ex);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
					}
				}
			}

			return null;
		}

		public async Task<T> GetAsync<T>(string cacheKey) where T : class
		{
			var cacheValue = await GetStringAsync(cacheKey);

			if (!String.IsNullOrWhiteSpace(cacheValue))
				return ObjectSerialization.Deserialize<T>(cacheValue);

			return null;
		}

		public async Task<bool> RemoveAsync(string cacheKey)
		{
			try
			{
				if (Config.SystemBehaviorConfig.CacheEnabled && _connection != null && _connection.IsConnected)
				{
					IDatabase cache = _connection.GetDatabase();
					await cache.KeyDeleteAsync(SetCacheKeyForEnv(cacheKey));

					return true;
				}
			}
			catch (TimeoutException)
			{ }
			catch (RedisConnectionException ex)
			{
				Logging.LogError(ex);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return false;
		}

		public async Task<long> IncrementAsync(string cacheKey, TimeSpan expiration)
		{
			try
			{
				if (Config.SystemBehaviorConfig.CacheEnabled && _connection != null && _connection.IsConnected)
				{
					IDatabase cache = _connection.GetDatabase();
					var key = SetCacheKeyForEnv(cacheKey);

					// Atomic INCR + (conditional) PEXPIRE in a single server-side script so the counter key
					// can never be left without a TTL if the connection drops between the two operations.
					// EXPIRE is applied only when the key was newly created (INCR == 1), and the script
					// returns the true counter value so a partial failure can't mask it as 0.
					const string incrementAndExpireScript =
						"local current = redis.call('INCR', KEYS[1])\n" +
						"if current == 1 then\n" +
						"  redis.call('PEXPIRE', KEYS[1], ARGV[1])\n" +
						"end\n" +
						"return current";

					var result = await cache.ScriptEvaluateAsync(
						incrementAndExpireScript,
						new RedisKey[] { key },
						new RedisValue[] { (long)expiration.TotalMilliseconds });

					return (long)result;
				}
			}
			catch (TimeoutException)
			{ }
			catch (RedisConnectionException ex)
			{
				Logging.LogError(ex);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return 0;
		}

		public bool IsConnected()
		{
			return _connection?.IsConnected ?? false;
		}

		private string SetCacheKeyForEnv(string cacheKey)
		{
			if (Config.SystemBehaviorConfig.Environment == SystemEnvironment.Dev)
				return $"DEV{cacheKey}";
			else if (Config.SystemBehaviorConfig.Environment == SystemEnvironment.QA)
				return $"QA{cacheKey}";
			else if (Config.SystemBehaviorConfig.Environment == SystemEnvironment.Staging)
				return $"ST{cacheKey}";

			return cacheKey;
		}

		private void EstablishRedisConnection()
		{
			int retry = 0;

			while (_connection == null && retry <= 3)
			{
				retry++;

				try
				{
					var options = ConfigurationOptions.Parse(Config.CacheConfig.RedisConnectionString);

					// Fail fast instead of blocking the calling thread when Redis is unreachable.
					// AbortOnConnectFail=false makes Connect return immediately and reconnect in the
					// background; the short timeouts cap any per-command wait. Without this, a dead
					// Redis blocks every request that constructs this provider for the full connect
					// timeout (multiplied by the retry loop below).
					options.AbortOnConnectFail = false;
					options.ConnectRetry = 1;
					options.ConnectTimeout = Math.Min(options.ConnectTimeout, 1000);
					options.SyncTimeout = 1000;
					options.AsyncTimeout = 1000;

					// Ping every 30s so idle connections aren't reset by intermediate
					// idle timeouts (conntrack/LB/redis server timeout) before the
					// default 60s keep-alive fires.
					options.KeepAlive = 30;

					_connection = ConnectionMultiplexer.Connect(options);
				}
				catch (Exception ex)
				{
					_connection = null;
					Logging.LogException(ex);
					Thread.Sleep(150);
				}
			}
		}
	}
}
