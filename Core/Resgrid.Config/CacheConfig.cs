namespace Resgrid.Config
{
	public static class CacheConfig
	{
		public static string RedisConnectionString = "rgdevinfaserver,abortConnect=false,syncTimeout=10000";
		public static string ApiBearerTokenKeyName = "_ApiBarerToken";
		public static string WebsiteSessionKeyNamePrefix = $"RGWebAuthSession-";
	}
}
