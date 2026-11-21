using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace Resgrid.Providers.NumberProvider
{
	/// <summary>
	/// Intake SMS conversations (plan §8 D14/D15): texts the member of the public from the region's master Resgrid number
	/// (NumberProviderConfig.TwilioResgridNumber, already registered for A2P), never from the department's dedicated number (inbound-only,
	/// D5), and returns Twilio's MessageSid for the conversation's delivery log. The same outbound net as every other SMS path
	/// (<see cref="TextMessageProvider"/> + <see cref="ProtectedTextMessageProviderDecorator"/>): ADP envelopes scrubbed, non-allowlisted
	/// links stripped, GSM-normalized, length-capped.
	/// </summary>
	public class IntakeSmsProvider : IIntakeSmsProvider
	{
		public async Task<IntakeSmsSendResult> SendAsync(string toNumber, string text, int departmentId, int maxLength, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(toNumber) || string.IsNullOrWhiteSpace(text))
				return new IntakeSmsSendResult { Error = "NothingToSend" };
			if (string.IsNullOrWhiteSpace(Config.NumberProviderConfig.TwilioResgridNumber) || string.IsNullOrWhiteSpace(Config.NumberProviderConfig.TwilioAccountSid))
				return new IntakeSmsSendResult { Error = "MasterNumberNotConfigured" };

			try
			{
				if (ProtectedOutboundGuard.MightContainEnvelope(text))
				{
					text = ProtectedOutboundGuard.Scrub(text, out var scrubbed);
					if (scrubbed > 0)
						Logging.LogError($"ADP outbound net scrubbed {scrubbed} enveloped value(s) from an intake conversation SMS for department {departmentId}.");
				}

				text = SmsContentHelper.PrepareForSms(text, maxLength > 0 ? maxLength : Config.SystemBehaviorConfig.SmsMaxLength,
					(Config.SystemBehaviorConfig.SmsAllowedUrlDomains ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries));
				if (string.IsNullOrWhiteSpace(text))
					return new IntakeSmsSendResult { Error = "NothingToSend" };

				TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);
				var message = await MessageResource.CreateAsync(
					from: new PhoneNumber(Config.NumberProviderConfig.TwilioResgridNumber),
					to: new PhoneNumber(toNumber),
					body: text);

				if (message == null)
					return new IntakeSmsSendResult { Error = "NoResponse" };

				var status = message.Status?.ToString();
				var failed = string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "undelivered", StringComparison.OrdinalIgnoreCase);
				return new IntakeSmsSendResult
				{
					Success = !failed,
					ProviderMessageId = message.Sid,
					Error = failed ? $"Twilio {message.ErrorCode}: {message.ErrorMessage}" : null
				};
			}
			catch (Twilio.Exceptions.ApiException ex)
			{
				// 21610 = the texter replied STOP to the master number (carrier-level opt-out); 21211/21614 = not a textable number.
				// Input, not a system fault: logged quietly, recorded on the message row.
				Logging.LogInfo($"Twilio refused an intake conversation text for department {departmentId} (code {ex.Code}).");
				return new IntakeSmsSendResult { Error = $"Twilio {ex.Code}: {ex.Message}" };
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Intake conversation text for department {departmentId} failed.");
				return new IntakeSmsSendResult { Error = ex.GetType().Name + ": " + ex.Message };
			}
		}
	}
}
