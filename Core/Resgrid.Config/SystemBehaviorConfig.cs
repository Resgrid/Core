using System.Collections.Generic;

namespace Resgrid.Config
{
	/// <summary>
	/// General system behavior configs like determing if to broadcast messages, if
	/// the cache is enabled, etc.
	/// </summary>
	public static class SystemBehaviorConfig
	{
		/// <summary>
		/// 
		/// </summary>
		public static string ResgridBaseUrl = "https://resgrid.local";

		/// <summary>
		/// 
		/// </summary>
		public static string ResgridApiBaseUrl = "https://resgridapi.local";

		/// <summary>
		/// This will prevent the system from sending any outbound messages, for example 
		/// email, push, text or call. Allows for testing the system without risk of sending
		/// out a broadcast to unknowning users.
		/// </summary>
		public static bool DoNotBroadcast = true;

		/// <summary>
		/// This will instally redirect default route landing at Home\Index to the Account\Login
		/// view to facilitate quick logins to the system. This is the default behavior for
		/// on-prem installs as there is no public part of the website that users need to access.
		/// </summary>
		public static bool RedirectHomeToLogin = true;

		/// <summary>
		/// Should the system use the cache provider or not. If this is disabled no caching
		/// will occur and calls will bypass getting items from cache.
		/// </summary>
		public static bool CacheEnabled = true;

		/// <summary>
		/// 
		/// </summary>
		public static string ExternalLinkUrlParamPassphrase = "NvM28Q8EJejQSdxS";

		/// <summary>
		/// These are specific departments that will be forced to go through the om-prem SMS gateway no matter the send status, i.e. Direct or Gateway
		/// </summary>
		public static HashSet<int> DepartmentsToForceSmsGateway = new HashSet<int>()
		{
		};
	}
}
