using System;
using System.Threading.Tasks;
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
		public async Task<bool> CommunicateCallAsync(string phoneNumber, UserProfile profile, Call call)
		{
			if (profile == null)
				return false;

			TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);

			if (!profile.VoiceForCall)
				return false;

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
					options.MachineDetection = "Enable";
					// Async AMD: without it Twilio holds the webhook (and the callee hears
					// silence for several seconds) until machine detection completes. Nothing
					// branches on AnsweredBy, so run detection asynchronously and start the
					// dispatch TwiML the moment the call is answered.
					options.AsyncAmd = "true";
					//options.IfMachine = "Continue";

					var phoneCall = await CallResource.CreateAsync(options);
					return true;
				}
			}

			if (profile.VoiceCallHome)
			{
				if (!String.IsNullOrWhiteSpace(profile.GetHomePhoneNumber()))
				{
					var options = new CreateCallOptions(new PhoneNumber(profile.GetHomePhoneNumber()), new PhoneNumber(number));
					options.Url = new Uri(string.Format(Config.NumberProviderConfig.TwilioVoiceCallApiUrl, profile.UserId, call.CallId));
					options.Method = "GET";
					options.MachineDetection = "Enable";
					// Async AMD — see the mobile branch above.
					options.AsyncAmd = "true";
					//options.IfMachine = "Continue";

					var phoneCall = await CallResource.CreateAsync(options);
					return true;
				}
			}

			return false;
		}

		public async Task<bool> SendVoiceVerificationCallAsync(string phoneNumber, string userId, int contactType)
		{
			if (string.IsNullOrWhiteSpace(phoneNumber) || string.IsNullOrWhiteSpace(userId))
				return false;

			try
			{
				TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);

				string fromNumber = Config.NumberProviderConfig.TwilioResgridNumber;
				if (string.IsNullOrWhiteSpace(fromNumber))
					return false;

				var options = new CreateCallOptions(new PhoneNumber(phoneNumber), new PhoneNumber(fromNumber));
				options.Url = new Uri(string.Format(Config.NumberProviderConfig.TwilioVoiceVerificationApiUrl, userId, contactType));
				options.Method = "GET";

				var phoneCall = await CallResource.CreateAsync(options);
				return phoneCall != null;
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				return false;
			}
		}
	}
}
