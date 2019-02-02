using System.Collections.Generic;

namespace Resgrid.Config
{
	public static class NumberProviderConfig
	{
		public static string NexmoApiKey = "";
		public static string NexmoApiSecret = "";
		public static string BaseNexmoUrl = "http://rest.nexmo.com/";

		public static string TwilioAccountSid = "";
		public static string TwilioAuthToken = "";
		public static string TwilioResgridNumber = "";
		public static string TwilioApiUrl = "https://resgridapi.local/Twilio/IncomingMessage";
		public static string TwilioVoiceCallApiTurl = "https://resgridapi.local/Twilio/VoiceCall?userId={0}&callId={1}";
		public static string TwilioVoiceApiUrl = "https://resgridapi.local/Twilio/InboundVoice";

		public static string DiafaanSmsGatewayUrl = "http://diafaan.yourcompany.local/";
		public static string DiafaanSmsGatewayUserName = "";
		public static string DiafaanSmsGatewayPassword = "";
		
		/// <summary>
		/// Nexemo supports some countries that Twilio doesn't, if you need routing for specific numbers that may be Nexmo only put them in here.
		/// </summary>
		public static HashSet<string> NexemoNumbers = new HashSet<string>()
		{
		};
	}
}
