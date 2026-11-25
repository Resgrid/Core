using System;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Localization.Areas.User.SystemMessages;
using Resgrid.Model;

namespace Resgrid.Services.Signatures
{
	/// <summary>
	/// The anonymous Resgrid signing page (/Sign/{token}). The link token is single use: a GET only shows the landing step (mail
	/// scanners prefetch links), and the POST that continues exchanges it once for a short signing session. Consent to do
	/// business electronically (its text version stored), then a Resgrid one-time code by e-mail or SMS, and only then is the
	/// embedded signer's URL (which carries the platform's recipient token) ever produced.
	/// </summary>
	public partial class SignatureRequestService
	{
		public async Task<SigningPageState> OpenLinkAsync(string linkToken)
		{
			var (signer, request, refusal) = await ResolveLinkAsync(linkToken);
			if (refusal != SignatureRequestRefusals.None)
				return Closed(refusal);

			var state = await StateAsync(request, signer);
			state.Step = SigningPageSteps.Landing;
			return state;
		}

		public async Task<(SigningPageState State, string SessionToken)> StartSessionAsync(string linkToken, string ipAddress, CancellationToken cancellationToken = default)
		{
			var (signer, request, refusal) = await ResolveLinkAsync(linkToken);
			if (refusal != SignatureRequestRefusals.None)
				return (Closed(refusal), null);

			var now = DateTime.UtcNow;
			var session = SignatureTokens.NewToken();
			var sessionExpires = now.AddMinutes(Math.Clamp(ESignConfig.SessionMinutes, 5, 240));
			if (sessionExpires > request.ExpiresOn)
				sessionExpires = request.ExpiresOn;

			// One conditional update: a second click (or a replayed link) finds the token used and gets nothing.
			if (!await _signers.ConsumeLinkTokenAsync(signer.SignatureRequestSignerId, SignatureTokens.Hash(linkToken), SignatureTokens.Hash(session), sessionExpires, now, cancellationToken))
				return (Closed(SignatureRequestRefusals.RequestClosed), null);

			signer.LinkTokenUsedOn = now;
			signer.SessionHash = SignatureTokens.Hash(session);
			signer.SessionExpiresOn = sessionExpires;
			if (signer.Status == (int)SignatureSignerStatuses.Pending)
				signer.Status = (int)SignatureSignerStatuses.Opened;

			return (await StateAsync(request, signer), session);
		}

		public async Task<SigningPageState> GetSessionStateAsync(string sessionToken)
		{
			var (signer, request, refusal) = await ResolveSessionAsync(sessionToken);
			return refusal != SignatureRequestRefusals.None ? Closed(refusal) : await StateAsync(request, signer);
		}

		public async Task<SigningPageState> ConsentAsync(string sessionToken, string consentTextVersion, string ipAddress, CancellationToken cancellationToken = default)
		{
			var (signer, request, refusal) = await ResolveSessionAsync(sessionToken);
			if (refusal != SignatureRequestRefusals.None)
				return Closed(refusal);

			if (!string.Equals(consentTextVersion, SignatureTokens.ConsentTextVersion, StringComparison.Ordinal))
				return await StateAsync(request, signer, SignatureRequestRefusals.InvalidRequest);

			if (!signer.ConsentedOn.HasValue)
			{
				signer.ConsentTextVersion = SignatureTokens.ConsentTextVersion;
				signer.ConsentedOn = DateTime.UtcNow;
				signer.ConsentIpAddress = Clean(ipAddress, 64);
				await _signers.UpdateAsync(signer, cancellationToken, true);
			}

			return await StateAsync(request, signer);
		}

		public async Task<SigningPageState> SendCodeAsync(string sessionToken, string culture, CancellationToken cancellationToken = default)
		{
			var (signer, request, refusal) = await ResolveSessionAsync(sessionToken);
			if (refusal != SignatureRequestRefusals.None)
				return Closed(refusal);
			if (!signer.ConsentedOn.HasValue || signer.OtpVerifiedOn.HasValue)
				return await StateAsync(request, signer);

			var now = DateTime.UtcNow;
			var window = now.Ticks / TimeSpan.TicksPerHour;
			var sends = await _cache.IncrementAsync("ESignOtpSend:" + signer.SignatureRequestSignerId.ToString(CultureInfo.InvariantCulture) + ":" + window.ToString(CultureInfo.InvariantCulture), TimeSpan.FromHours(2));
			// IncrementAsync returns 0 when the cache is unavailable; then the stored send time still spaces codes a minute apart.
			if (sends > Math.Max(1, ESignConfig.MaxCodesPerHour) || (sends == 0 && signer.OtpSentOn.HasValue && now - signer.OtpSentOn.Value < TimeSpan.FromMinutes(1)))
				return await StateAsync(request, signer, SignatureRequestRefusals.InvalidRequest);

			var code = SignatureTokens.NewCode();
			signer.OtpHash = SignatureTokens.CodeHash(signer.SignatureRequestSignerId, code);
			signer.OtpExpiresOn = now.AddMinutes(CodeMinutes);
			signer.OtpAttempts = 0;
			signer.OtpSentOn = now;
			signer.OtpChannel = signer.AuthMethod == (int)SignatureSignerAuthMethods.SmsOtp ? (int)SignatureSignerAuthMethods.SmsOtp : (int)SignatureSignerAuthMethods.EmailOtp;
			await _signers.UpdateAsync(signer, cancellationToken, true);

			var sent = await SendCodeMessageAsync(request, signer, code, culture);
			var state = await StateAsync(request, signer, sent ? SignatureRequestRefusals.None : SignatureRequestRefusals.ProviderFailed);
			state.CodeSent = sent;
			return state;
		}

		public async Task<SigningPageState> VerifyCodeAsync(string sessionToken, string code, CancellationToken cancellationToken = default)
		{
			var (signer, request, refusal) = await ResolveSessionAsync(sessionToken);
			if (refusal != SignatureRequestRefusals.None)
				return Closed(refusal);
			if (signer.OtpVerifiedOn.HasValue)
				return await StateAsync(request, signer);

			var now = DateTime.UtcNow;
			if (string.IsNullOrEmpty(signer.OtpHash) || !signer.OtpExpiresOn.HasValue || signer.OtpExpiresOn.Value <= now)
				return await StateAsync(request, signer, SignatureRequestRefusals.InvalidRequest);

			var digits = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
			if (digits.Length == 6 && SignatureTokens.HashEquals(SignatureTokens.CodeHash(signer.SignatureRequestSignerId, digits), signer.OtpHash))
			{
				signer.OtpVerifiedOn = now;
				signer.OtpHash = null;
				signer.OtpExpiresOn = null;
				signer.Status = (int)SignatureSignerStatuses.Verified;
				await _signers.UpdateAsync(signer, cancellationToken, true);
				return await StateAsync(request, signer);
			}

			signer.OtpAttempts++;
			if (signer.OtpAttempts >= Math.Max(1, ESignConfig.MaxCodeAttempts))
			{
				// Too many guesses: the code is wiped and a new one must be sent.
				signer.OtpHash = null;
				signer.OtpExpiresOn = null;
			}
			await _signers.UpdateAsync(signer, cancellationToken, true);
			return await StateAsync(request, signer, SignatureRequestRefusals.InvalidRequest);
		}

		public async Task<SigningPageState> CheckCompletionAsync(string sessionToken, CancellationToken cancellationToken = default)
		{
			var (signer, request, refusal) = await ResolveSessionAsync(sessionToken);
			if (refusal != SignatureRequestRefusals.None)
				return Closed(refusal);
			if (!signer.OtpVerifiedOn.HasValue)
				return await StateAsync(request, signer);

			try
			{
				await ReconcileAsync(request.DepartmentId, request.SignatureRequestId, cancellationToken);
			}
			catch (Exception ex)
			{
				// The worker re-fetches on its own schedule; the page says the signature is being confirmed.
				Logging.LogException(ex, "An e-sign envelope could not be re-fetched from the signing page.");
			}

			var fresh = await GetAsync(request.DepartmentId, request.SignatureRequestId) ?? request;
			var freshSigner = (await GetSignersAsync(request.DepartmentId, request.SignatureRequestId)).FirstOrDefault(s => s.SignatureRequestSignerId == signer.SignatureRequestSignerId) ?? signer;
			return await StateAsync(fresh, freshSigner);
		}

		// ---- state ------------------------------------------------------------------------------------------------------------

		private async Task<(SignatureRequestSigner, SignatureRequestRecord, SignatureRequestRefusals)> ResolveLinkAsync(string linkToken)
		{
			if (!SignatureTokens.LooksLikeToken(linkToken))
				return (null, null, SignatureRequestRefusals.InvalidRequest);

			var signer = await _signers.GetByLinkTokenHashAsync(SignatureTokens.Hash(linkToken));
			if (signer == null)
				return (null, null, SignatureRequestRefusals.InvalidRequest);

			var request = await GetAsync(signer.DepartmentId, signer.SignatureRequestId);
			var now = DateTime.UtcNow;
			if (request == null || !request.IsOpen || request.ExpiresOn <= now || request.Channel != (int)SignatureRequestChannels.Remote)
				return (signer, request, SignatureRequestRefusals.RequestClosed);
			if (signer.LinkTokenUsedOn.HasValue || !signer.LinkTokenExpiresOn.HasValue || signer.LinkTokenExpiresOn.Value <= now)
				return (signer, request, SignatureRequestRefusals.RequestClosed);
			if (signer.Status == (int)SignatureSignerStatuses.Signed || signer.Status == (int)SignatureSignerStatuses.Declined)
				return (signer, request, SignatureRequestRefusals.RequestClosed);

			return (signer, request, SignatureRequestRefusals.None);
		}

		private async Task<(SignatureRequestSigner, SignatureRequestRecord, SignatureRequestRefusals)> ResolveSessionAsync(string sessionToken)
		{
			if (!SignatureTokens.LooksLikeToken(sessionToken))
				return (null, null, SignatureRequestRefusals.InvalidRequest);

			var signer = await _signers.GetBySessionHashAsync(SignatureTokens.Hash(sessionToken));
			var now = DateTime.UtcNow;
			if (signer == null || !signer.SessionExpiresOn.HasValue || signer.SessionExpiresOn.Value <= now)
				return (null, null, SignatureRequestRefusals.RequestClosed);

			var request = await GetAsync(signer.DepartmentId, signer.SignatureRequestId);
			if (request == null)
				return (null, null, SignatureRequestRefusals.RequestClosed);

			// A finished signer may still see the "done" step; an expired or withdrawn request shows closed.
			if (!request.IsOpen && request.Status != (int)SignatureRequestStatuses.Completed)
				return (signer, request, SignatureRequestRefusals.RequestClosed);

			return (signer, request, SignatureRequestRefusals.None);
		}

		private async Task<SigningPageState> StateAsync(SignatureRequestRecord request, SignatureRequestSigner signer, SignatureRequestRefusals refusal = SignatureRequestRefusals.None)
		{
			var department = await _departments.GetDepartmentByIdAsync(request.DepartmentId, false);
			var state = new SigningPageState
			{
				Refusal = refusal,
				DepartmentId = request.DepartmentId,
				DepartmentName = department?.Name,
				Title = request.Title,
				SignerName = signer.ExternalName,
				StatementText = request.StatementText,
				ArtifactSha256 = request.ArtifactSha256,
				ExpiresOn = request.ExpiresOn,
				ContactMasked = signer.ContactMasked,
				OtpChannel = (SignatureSignerAuthMethods)signer.AuthMethod,
				CodeSent = !string.IsNullOrEmpty(signer.OtpHash) && signer.OtpExpiresOn.HasValue && signer.OtpExpiresOn.Value > DateTime.UtcNow,
				AttemptsLeft = Math.Max(0, Math.Max(1, ESignConfig.MaxCodeAttempts) - signer.OtpAttempts)
			};

			if (signer.Status == (int)SignatureSignerStatuses.Signed || request.Status == (int)SignatureRequestStatuses.Completed)
				state.Step = SigningPageSteps.Done;
			else if (signer.Status == (int)SignatureSignerStatuses.Declined || !request.IsOpen)
				state.Step = SigningPageSteps.Closed;
			else if (!signer.ConsentedOn.HasValue)
				state.Step = SigningPageSteps.Consent;
			else if (!signer.OtpVerifiedOn.HasValue)
				state.Step = SigningPageSteps.Code;
			else
			{
				state.Step = SigningPageSteps.Sign;
				// Only past the consent and the one-time code does the platform's recipient token leave the server.
				var token = Decrypt(signer.ProviderSigningTokenEncrypted);
				state.EmbedUrl = token == null ? null : _provider?.BuildEmbedUrl(token, signer.ExternalName);
				state.EmbedOrigin = _provider?.EmbedOrigin;
			}

			return state;
		}

		private static SigningPageState Closed(SignatureRequestRefusals refusal) => new SigningPageState { Refusal = refusal, Step = SigningPageSteps.Closed };

		private async Task<bool> SendCodeMessageAsync(SignatureRequestRecord request, SignatureRequestSigner signer, string code, string culture)
		{
			try
			{
				var department = await _departments.GetDepartmentByIdAsync(request.DepartmentId, false);
				if (signer.AuthMethod == (int)SignatureSignerAuthMethods.SmsOtp)
				{
					var phone = Decrypt(signer.ExternalPhoneEncrypted);
					if (phone == null)
						return false;
					var number = await _departmentSettings.GetTextToCallNumberForDepartmentAsync(request.DepartmentId);
					// A direct provider send, never an e-mail-to-SMS gateway (deterministic delivery for a security code).
					return await _textMessages.SendTextMessage(phone, SystemMessagesResources.Get("SigningCodeSmsBody", culture, code, CodeMinutes, department?.Name),
						number, MobileCarriers.None, 0, false, false);
				}

				var email = Decrypt(signer.ExternalEmailEncrypted);
				if (email == null)
					return false;
				using var mail = new MailMessage();
				mail.To.Add(email);
				mail.From = new MailAddress(OutboundEmailServerConfig.FromMail, "Resgrid");
				mail.Subject = SystemMessagesResources.Get("SigningCodeEmailSubject", culture, department?.Name);
				mail.Body = SystemMessagesResources.Get("SigningCodeEmailBody", culture, signer.ExternalName, code, CodeMinutes, department?.Name);
				mail.IsBodyHtml = false;
				return await _emailSender.SendEmail(mail);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "A signing code could not be sent.");
				return false;
			}
		}
	}
}
