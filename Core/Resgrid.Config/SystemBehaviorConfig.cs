using System;
using System.Collections.Generic;

namespace Resgrid.Config
{
	/// <summary>
	/// General system behavior configs like determining if to broadcast messages, if
	/// the cache is enabled, etc.
	/// </summary>
	public static class SystemBehaviorConfig
	{
		/// <summary>
		/// The Environment context Resgrid is running under
		/// </summary>
		public static SystemEnvironment Environment = SystemEnvironment.Dev;

		/// <summary>
		/// The base url to where the Resgrid web application lives
		/// </summary>
		public static string ResgridBaseUrl = "https://resgrid.local";

		/// <summary>
		/// The base url to where the Resgrid web api lives
		/// </summary>
		public static string ResgridApiBaseUrl = "https://resgridapi.local";

		/// <summary>
		/// The base url to where the Resgrid signalr lives
		/// </summary>
		public static string ResgridEventingBaseUrl = "https://events.resgrid.com";

		/// <summary>
		/// This will prevent the system from sending any outbound messages, for example 
		/// email, push, text or call. Allows for testing the system without risk of sending
		/// out a broadcast to unknowing users.
		/// </summary>
		public static bool DoNotBroadcast = true;

		/// <summary>
		/// These departments will bypass the DoNotBroadcast value
		/// </summary>
		public static HashSet<int> BypassDoNotBroadcastDepartments = new HashSet<int>()
		{
			1
		};

		/// <summary>
		/// This will initially redirect default route landing at Home\Index to the Account\Login
		/// view to facilitate quick logins to the system. This is the default behavior for
		/// on-prem installs as there is no public part of the website that users need to access.
		/// </summary>
		public static bool RedirectHomeToLogin = true;
		
		/// <summary>
		/// Tells the system if it's running on Microsoft's Azure environment
		/// </summary>
		public static bool IsAzure = false;

		/// <summary>
		/// Should the system use the cache provider or not. If this is disabled no caching
		/// will occur and calls will bypass getting items from cache.
		/// </summary>
		public static bool CacheEnabled = true;
		
		/// <summary>
		/// Forces the use of the in memory cache provider instead of Redis
		/// </summary>
		public static bool UseInternalCache = false;

		/// <summary>
		/// Used to encrypt the publicly accessible url key values
		/// </summary>
		public static string ExternalLinkUrlParamPassphrase = "NvM28Q8EJejQSdxS";
		
		/// <summary>
		/// Used to encrypt the url parameters for the external audio links
		/// </summary>
		public static string ExternalAudioUrlParamPasshprase = "5a4tALka7bz6h4CY";
		
		/// <summary>
		/// Used to encrypt payloads for the API auth token
		/// </summary>
		public static string ApiTokenEncryptionPassphrase = "exjk3TB)&3ky`sWUH}y!r$x#6jsEX'H-UDQn6(v=w:#*#Pr";
		
		/// <summary>
		/// The length the API token will be valid for once a user logs into the app
		/// </summary>
		public static int APITokenMonthsTTL = 1;
		
		/// <summary>
		/// Sets the type of Service bus to use for the system, options are Azure or NATS
		/// </summary>
		public static ServiceBusTypes ServiceBusType = ServiceBusTypes.Rabbit;

		/// <summary>
		/// Sets the type of External error and logging provider for the system to use
		/// </summary>
		public static ErrorLoggerTypes ErrorLoggerType = ErrorLoggerTypes.Elk;

		/// <summary>
		/// Sets the type of outbound email server or provider to use
		/// </summary>
		public static OutboundEmailTypes OutboundEmailType = OutboundEmailTypes.Smtp;

		/// <summary>
		/// Sets the type of url link shortner provider to use
		/// </summary>
		public static LinksProviderTypes LinkProviderType = LinksProviderTypes.Polr;

		/// <summary>
		/// Sets the type of voip provider to use
		/// </summary>
		public static VoipProviderTypes VoipProviderType = VoipProviderTypes.Kazoo;

		/// <summary>
		/// Sets the type of sms provider to use
		/// </summary>
		public static SmsProviderTypes SmsProviderType = SmsProviderTypes.Twilio;

		/// <summary>
		/// Sets the type of backup sms provider to use
		/// </summary>
		public static SmsProviderTypes BackupSmsProviderType = SmsProviderTypes.SignalWire;

		/// <summary>
		/// If you wish to always send sms messages via the email to sms gateway as well even if
		/// the provider has a direct send option. 
		/// </summary>
		public static bool SendCallsToSmsEmailGatewayAdditionally = true;

		/// <summary>
		/// These are specific departments that will be forced to go through the om-prem SMS gateway no matter the send status, i.e. Direct or Gateway
		/// </summary>
		public static HashSet<int> DepartmentsToForceSmsGateway = new HashSet<int>()
		{
		};

		/// <summary>
		/// These are specific departments that will be forced to go through the backup sms provider
		/// </summary>
		public static HashSet<int> DepartmentsToForceBackupSmsProvider = new HashSet<int>()
		{

		};

		/// <summary>
		/// For usage with DepartmentsToForceBackupSmsProvider, this will determine if we also want to send text messages to the primary provider as well
		/// </summary>
		public static bool AlsoSendToPrimarySmsProvider = false;

		/// <summary>
		/// To send push notifications with your on-prem Resgrid installation with our apps in the App Stores (Google and Apple)
		/// you need to pay for a site key to send push notifications through our push infrastructure. To get a site key, which is
		/// an annual payment, please contact team@resgrid.com. 
		/// </summary>
		public static string SiteKey = "";

		/// <summary>
		/// A notice to display on the login page
		/// </summary>
		public static string LoginPageNotice = "";

		/// <summary>
		/// The Url to the help and support site
		/// </summary>
		public static string HelpAndSupportUrl = "";

		/// <summary>
		/// Contact Us Url
		/// </summary>
		public static string ContactUsUrl = "";

		/// <summary>
		/// Blog Url
		/// </summary>
		public static string BlogUrl = "";

		public static string GetEnvPrefix()
		{
			switch (Environment)
			{
				case SystemEnvironment.Prod:
					return String.Empty;
				case SystemEnvironment.Staging:
					return "ST";
				case SystemEnvironment.QA:
					return "QA";
				case SystemEnvironment.Dev:
					return "DEV";
				default:
					return String.Empty;
			}
		}
	}

	public enum SystemEnvironment
	{
		Prod,
		Staging,
		QA,
		Dev
	}
}
