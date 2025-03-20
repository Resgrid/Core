using Twilio.AspNet.Common;

namespace Resgrid.Web.Services.Models
{
	public class TwilioGatherRequest : TwilioRequest
	{
		// I have no fucking clue why this isn't in the normal Request object.
		public string Digits { get; set; }
	}
}
