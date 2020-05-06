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
		public static string TwilioApiUrl = "https://resgridapi.local/Twilio/IncomingMessage";
		public static string TwilioVoiceCallApiTurl = "https://resgridapi.local/Twilio/VoiceCall?userId={0}&callId={1}";
		public static string TwilioVoiceApiUrl = "https://resgridapi.local/Twilio/InboundVoice";

		// Diafaan (https://www.diafaan.com)
		public static string DiafaanSmsGatewayUrl = "http://diafaan.yourcompany.local/";
		public static string DiafaanSmsGatewayUserName = "";
		public static string DiafaanSmsGatewayPassword = "";

		// SignalWire (https://signalwire.com)
		public static string SignalWireApiUrl = "";
		public static string SignalWireResgridNumber = "";
		public static string SignalWireAccountSid = "";
		public static string SignalWireApiKey = "";

		/// <summary>
		/// Nexemo supports some countries that Twilio doesn't, if you need routing for specific numbers that may be Nexmo only put them in here.
		/// </summary>
		public static HashSet<string> NexemoNumbers = new HashSet<string>()
		{
		};

		/// <summary>
		/// SignalWire zones allow you to segment outbound numbers based on a zone id. For example this can allow you have outbound numbers per country, per state\province, etc.
		/// </summary>
		public static Dictionary<int, HashSet<string>> SignalWireZones = new Dictionary<int, HashSet<string>>()
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
