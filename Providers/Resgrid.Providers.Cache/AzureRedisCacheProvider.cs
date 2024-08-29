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
						if (cacheValue.HasValue)
							data = ObjectSerialization.Deserialize<T>(cacheValue);
					}
					catch
					{
						Remove(SetCacheKeyForEnv(cacheKey));
						throw;
					}

					if (data != null)
						return data;
				}
			}
			catch (TimeoutException)
			{ }
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
					catch (Exception ex)
					{
						Logging.LogException(ex);
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
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}


		public async Task<T> RetrieveAsync<T>(string cacheKey, Func<Task<T>> fallbackFunction, TimeSpan expiration)
			where T : class
		{
			T data = null;
			IDatabase cache = null;

			try
			{
				if (Config.SystemBehaviorConfig.CacheEnabled && _connection != null && _connection.IsConnected)
				{
					cache = _connection.GetDatabase();

					var cacheValue = await cache.StringGetAsync(SetCacheKeyForEnv(cacheKey));

					try
					{
						if (cacheValue.HasValue)
							data = ObjectSerialization.Deserialize<T>(cacheValue);
					}
					catch
					{
						await RemoveAsync(SetCacheKeyForEnv(cacheKey));
						throw;
					}

					if (data != null)
						return data;
				}
			}
			catch (TimeoutException)
			{ }
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			data = await fallbackFunction();

			if (Config.SystemBehaviorConfig.CacheEnabled && _connection != null && _connection.IsConnected)
			{
				if (data != null && cache != null)
				{
					try
					{
						await cache.StringSetAsync(SetCacheKeyForEnv(cacheKey), ObjectSerialization.Serialize(data), expiration);
					}
					catch (TimeoutException)
					{ }
					catch (Exception ex)
					{
						Logging.LogException(ex);
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
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return false;
		}

		public bool IsConnected()
		{
			EstablishRedisConnection();

			return _connection.IsConnected;
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

			try
			{
				if (_connection != null && _connection.IsConnected == false)
				{
					_connection.Close();
					_connection = null;
				}

			}
			catch { }

			while (_connection == null && retry <= 3)
			{
				retry++;

				try
				{
					_connection = ConnectionMultiplexer.Connect(Config.CacheConfig.RedisConnectionString);
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
