namespace Resgrid.Web.Options
{
	public class AppOptions
	{
		public string ResgridApiUrl { get; set; }
		public string StripeClientKey { get; set; }
		public string StripeApiKey { get; set; }
		// Twilio
		public string TwilioAccountSid { get; set; }
		public string TwilioApiKey { get; set; }
		public string TwilioApiSecret { get; set; }
		public string TwilioIpmServiceSid { get; set; }
		public string TwilioAuthToken { get; set; }
	}
}