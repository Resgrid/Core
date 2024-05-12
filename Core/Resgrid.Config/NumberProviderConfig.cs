using System.Collections.Generic;

namespace Resgrid.Config
{
	public static class NumberProviderConfig
	{
		// Nexmo (https://www.nexmo.com)
		public static string NexmoApiKey = "";
		public static string NexmoApiSecret = "";
		public static string BaseNexmoUrl = "http://rest.nexmo.com/";

		// Twilio (https://www.twilio.com)
		public static string TwilioAccountSid = "";
		public static string TwilioAuthToken = "";
		public static string TwilioResgridNumber = "";
		public static string TwilioApiUrl = SystemBehaviorConfig.ResgridApiBaseUrl + "/api/Twilio/IncomingMessage";
		public static string TwilioVoiceCallApiUrl = SystemBehaviorConfig.ResgridApiBaseUrl + "/api/Twilio/VoiceCall?userId={0}&callId={1}";
		public static string TwilioVoiceApiUrl = SystemBehaviorConfig.ResgridApiBaseUrl + "/api/Twilio/InboundVoice";

		// Diafaan (https://www.diafaan.com)
		public static string DiafaanSmsGatewayUrl = "";
		public static string DiafaanSmsGatewayUserName = "";
		public static string DiafaanSmsGatewayPassword = "";

		// SignalWire (https://signalwire.com)
		public static string SignalWireApiUrl = "";
		public static string SignalWireResgridNumber = "";    // Main prod
		public static string SignalWireAccountSid = "";
		public static string SignalWireApiKey = "";


		/// <summary>
		/// Nexemo supports some countries that Twilio doesn't, if you need routing for specific numbers that may be Nexmo only put them in here.
		/// </summary>
		public static HashSet<string> NexemoNumbers = new HashSet<string>()
		{

		};

		public static Dictionary<int, HashSet<string>> SignalWireZones = new Dictionary<int, HashSet<string>>()
		{

		};

		public static HashSet<int> TwilioPlans = new HashSet<int>()
		{

		};

		public static HashSet<int> TwilioDepartments = new HashSet<int>()
		{

		};
	}

	/// <summary>
	/// Possible providers for sending sms and mms messages
	/// </summary>
	public enum SmsProviderTypes
	{
		Twilio = 0,
		SignalWire = 1,
		Nexmo = 2,
		Email = 3,
		Diafaan = 4
	}
}
