using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Providers;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace Resgrid.Providers.NumberProvider
{
	/// <summary>
	/// Twilio REST calls for the dispatcher desk voice flow (M0324, plan D8): ringing a dispatcher's phone to connect them to a held
	/// caller (or to call one back), redirecting a held caller, and ending a call. The TwiML for every leg is served by
	/// Web.Services <c>DispatcherVoiceController</c> (<see cref="Resgrid.Model.DispatcherVoiceRoutes"/>).
	/// </summary>
	public class DispatcherVoiceProvider : IDispatcherVoiceProvider
	{
		public async Task<string> PlaceCallAsync(string to, string from, string twimlUrl, string statusCallbackUrl, int timeoutSeconds,
			CancellationToken cancellationToken = default)
		{
			var toNumber = ToE164(to);
			var fromNumber = ToE164(string.IsNullOrWhiteSpace(from) ? Config.NumberProviderConfig.TwilioResgridNumber : from);

			if (string.IsNullOrWhiteSpace(toNumber) || string.IsNullOrWhiteSpace(fromNumber) || !Uri.TryCreate(twimlUrl, UriKind.Absolute, out var twiml))
				return null;

			if (cancellationToken.IsCancellationRequested)
				return null;

			try
			{
				TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);

				var options = new CreateCallOptions(new PhoneNumber(toNumber), new PhoneNumber(fromNumber))
				{
					Url = twiml,
					Method = "POST",
					Timeout = timeoutSeconds > 0 ? Math.Min(timeoutSeconds, 600) : (int?)null
				};

				// Status callbacks fire on completion (busy, no-answer, failed, canceled, completed), which is all the desk needs.
				if (Uri.TryCreate(statusCallbackUrl, UriKind.Absolute, out var statusCallback))
				{
					options.StatusCallback = statusCallback;
					options.StatusCallbackMethod = "POST";
				}

				var call = await CallResource.CreateAsync(options);
				return call?.Sid;
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "Dispatcher desk: the dispatcher's phone could not be called.");
				return null;
			}
		}

		public async Task<bool> RedirectCallAsync(string callSid, string twimlUrl, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(callSid) || !Uri.TryCreate(twimlUrl, UriKind.Absolute, out var twiml) || cancellationToken.IsCancellationRequested)
				return false;

			try
			{
				TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);

				var call = await CallResource.UpdateAsync(new UpdateCallOptions(callSid) { Url = twiml, Method = "POST" });
				return call != null;
			}
			catch (Exception ex)
			{
				// Most often the caller hung up a moment before: the call is no longer live.
				Resgrid.Framework.Logging.LogException(ex, $"Dispatcher desk: call {callSid} could not be redirected.");
				return false;
			}
		}

		public async Task<bool> EndCallAsync(string callSid, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(callSid) || cancellationToken.IsCancellationRequested)
				return false;

			try
			{
				TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);

				var call = await CallResource.UpdateAsync(new UpdateCallOptions(callSid) { Status = CallResource.UpdateStatusEnum.Completed });
				return call != null;
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, $"Dispatcher desk: call {callSid} could not be ended.");
				return false;
			}
		}

		public async Task<bool> DeleteQueueAsync(string queueSid, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(queueSid) || !queueSid.StartsWith("QU", StringComparison.Ordinal) || cancellationToken.IsCancellationRequested)
				return false;

			try
			{
				TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);

				return await QueueResource.DeleteAsync(queueSid);
			}
			catch (Exception ex)
			{
				// A queue that still holds a caller (or is already gone) cannot be deleted; nothing to do.
				Resgrid.Framework.Logging.LogException(ex, $"Dispatcher desk: hold queue {queueSid} could not be deleted.");
				return false;
			}
		}

		/// <summary>Twilio wants E.164: "+15551234567" from digits with or without the plus.</summary>
		public static string ToE164(string number)
		{
			if (string.IsNullOrWhiteSpace(number))
				return null;

			var digits = new string(number.Where(char.IsDigit).ToArray());
			return digits.Length == 0 ? null : "+" + digits;
		}
	}
}
