using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface ICacheProvider
	{
		T Retrieve<T>(string cacheKey, Func<T> fallbackFunction, TimeSpan expiration) where T : class;
		Task<T> RetrieveAsync<T>(string cacheKey, Func<Task<T>> fallbackFunction, TimeSpan expiration) where T : class;
		void Remove(string cacheKey);
		Task<bool> RemoveAsync(string cacheKey);
		bool IsConnected();
		Task<bool> SetStringAsync(string cacheKey, string value, TimeSpan expiration);
		Task<string> GetStringAsync(string cacheKey);
		Task<T> GetAsync<T>(string cacheKey) where T : class;
	}
}
