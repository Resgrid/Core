using System;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Twilio;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;
using Call = Resgrid.Model.Call;

namespace Resgrid.Providers.NumberProvider
{
	public class OutboundVoiceProvider : IOutboundVoiceProvider
	{
		public void CommunicateCall(string phoneNumber, UserProfile profile, Call call)
		{
			if (profile == null)
				return;

			TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);

			if (!profile.VoiceForCall)
				return;

			string number = phoneNumber;

			if (String.IsNullOrWhiteSpace(number) || NumberHelper.IsNexmoNumber(number))
				number = Config.NumberProviderConfig.TwilioResgridNumber;

			if (number.Length == 11 && number[0] != Char.Parse("1"))
				number = "+" + number;

			if (profile.VoiceCallMobile)
			{
				if (!String.IsNullOrWhiteSpace(profile.GetPhoneNumber()))
				{
					var options = new CreateCallOptions(new PhoneNumber(profile.GetPhoneNumber()), new PhoneNumber(number));
					options.Url = new Uri(string.Format(Config.NumberProviderConfig.TwilioVoiceCallApiUrl, profile.UserId, call.CallId));
					options.Method = "GET";
					//options.IfMachine = "Continue";

					var phoneCall = CallResource.Create(options);
				}
			}

			if (profile.VoiceCallHome)
			{
				if (!String.IsNullOrWhiteSpace(profile.GetHomePhoneNumber()))
				{
					var options = new CreateCallOptions(new PhoneNumber(profile.GetHomePhoneNumber()), new PhoneNumber(number));
					options.Url = new Uri(string.Format(Config.NumberProviderConfig.TwilioVoiceCallApiUrl, profile.UserId, call.CallId));
					options.Method = "GET";
					//options.IfMachine = "Continue";

					var phoneCall = CallResource.Create(options);
				}
			}
		}
	}
}
