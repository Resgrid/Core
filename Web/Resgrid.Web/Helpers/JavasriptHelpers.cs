using CommonServiceLocator;
using Resgrid.Config;
using Resgrid.Model.Providers;
using System.Threading.Tasks;

namespace Resgrid.Web.Helpers
{
	public static class JavasriptHelpers
	{
		private static ICacheProvider _cacheProvider;
		
		public static long GetJavascriptTimestamp(System.DateTime input)
		{
			System.TimeSpan span = new System.TimeSpan(System.DateTime.Parse("1/1/1970").Ticks);
			System.DateTime time = input.Subtract(span);
			return (long) (time.Ticks/10000);
		}
		
		public static async Task<string> GetApiToken()
		{
			if (_cacheProvider == null)
				_cacheProvider = ServiceLocator.Current.GetInstance<ICacheProvider>();

			var apiToken = await _cacheProvider.GetStringAsync(CacheConfig.ApiBearerTokenKeyName + $"_${ClaimsAuthorizationHelper.GetUserId()}");

			return apiToken;
		}
	}
}
