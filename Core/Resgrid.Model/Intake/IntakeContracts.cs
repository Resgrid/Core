using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Resgrid.Model
{
	/// <summary>
	/// Address formats of intake channels (call-intake-and-ai-destinations-plan.md §4). Shared by the channel service (what is
	/// stored) and the transports (what an inbound message is looked up by), so both normalize the same way. Null = not a valid
	/// address for the transport.
	/// </summary>
	public static class IntakeChannelAddresses
	{
		private static readonly Regex E164 = new Regex(@"^\+[1-9][0-9]{6,14}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
		private static readonly Regex EmailLocalPart = new Regex(@"^[a-z0-9](?:[a-z0-9._+-]{0,62}[a-z0-9])?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
		private static readonly Regex Slack = new Regex(@"^[TE][A-Z0-9]{2,20}/[CG][A-Z0-9]{2,20}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
		private static readonly Regex TeamsConversation = new Regex(@"^[^\s/]{3,200}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
		private static readonly Regex Telegram = new Regex(@"^-?[0-9]{1,20}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
		private static readonly Regex Discord = new Regex(@"^[0-9]{15,22}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
		private static readonly Regex GoogleSpace = new Regex(@"^spaces/[A-Za-z0-9_-]{3,100}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
		private static readonly Regex WebhookToken = new Regex(@"^[A-Za-z0-9_-]{24,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

		public static string Normalize(IntakeChannelTypes channelType, string address)
		{
			if (string.IsNullOrWhiteSpace(address))
				return null;

			var value = address.Trim();
			switch (channelType)
			{
				case IntakeChannelTypes.Sms:
				case IntakeChannelTypes.Voice:
				case IntakeChannelTypes.WhatsApp:
					return PhoneNumber(value);

				case IntakeChannelTypes.Email:
					var at = value.IndexOf('@');
					var local = (at >= 0 ? value.Substring(0, at) : value).Trim().ToLowerInvariant();
					return EmailLocalPart.IsMatch(local) && !local.Contains("..") ? local : null;

				case IntakeChannelTypes.Slack:
					var slack = value.ToUpperInvariant().Replace(" ", "");
					return Slack.IsMatch(slack) ? slack : null;

				case IntakeChannelTypes.Teams:
					var slash = value.IndexOf('/');
					if (slash <= 0 || !Guid.TryParse(value.Substring(0, slash), out var tenant))
						return null;
					var conversation = TeamsChannelId(value.Substring(slash + 1));
					return conversation != null && TeamsConversation.IsMatch(conversation) ? tenant.ToString("D") + "/" + conversation : null;

				case IntakeChannelTypes.Telegram:
					return Telegram.IsMatch(value) ? value : null;

				case IntakeChannelTypes.Discord:
					return Discord.IsMatch(value) ? value : null;

				case IntakeChannelTypes.GoogleChat:
					var space = value.StartsWith("spaces/", StringComparison.OrdinalIgnoreCase) ? "spaces/" + value.Substring(7) : "spaces/" + value;
					return GoogleSpace.IsMatch(space) ? space : null;

				case IntakeChannelTypes.Webhook:
					return WebhookToken.IsMatch(value) ? value : null;

				default:
					return null;
			}
		}

		/// <summary>
		/// E.164 from what carriers and people type: "whatsapp:+1 (775) 555-0100", "17755550100" or a 10-digit North American
		/// number all become "+17755550100". Null when it cannot be a phone number.
		/// </summary>
		public static string PhoneNumber(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var text = value.Trim();
			if (text.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase))
				text = text.Substring(9);

			var plus = text.TrimStart().StartsWith("+", StringComparison.Ordinal);
			var digits = new string(text.Where(char.IsDigit).ToArray());
			if (digits.Length == 0 || text.Any(c => char.IsLetter(c)))
				return null;

			string e164;
			if (plus)
				e164 = "+" + digits;
			else if (digits.Length == 10)
				e164 = "+1" + digits;
			else if (digits.Length == 11 && digits[0] == '1')
				e164 = "+" + digits;
			else
				e164 = "+" + digits;

			return E164.IsMatch(e164) ? e164 : null;
		}

		/// <summary>A Teams channel conversation id without the thread suffix ("19:abc@thread.tacv2;messageid=123" → "19:abc@thread.tacv2").</summary>
		public static string TeamsChannelId(string conversationId)
		{
			if (string.IsNullOrWhiteSpace(conversationId))
				return null;

			var value = conversationId.Trim();
			var suffix = value.IndexOf(";messageid=", StringComparison.OrdinalIgnoreCase);
			return suffix > 0 ? value.Substring(0, suffix) : value;
		}

		/// <summary>A new webhook channel token (URL path segment): 32 random bytes, base64url.</summary>
		public static string NewWebhookToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

		/// <summary>A new webhook signing secret: 32 random bytes, base64url, shown to the admin once.</summary>
		public static string NewWebhookSecret() => "whsec_" + Base64Url(RandomNumberGenerator.GetBytes(32));

		private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}

	/// <summary>Outcome of checking a webhook intake signature.</summary>
	public enum IntakeWebhookSignatureResults
	{
		Valid = 0,
		Missing = 1,
		Malformed = 2,
		Stale = 3,
		Invalid = 4
	}

	/// <summary>
	/// Signed webhook intake: header <c>X-Resgrid-Signature: t=&lt;unix seconds&gt;,v1=&lt;hex&gt;</c> where v1 is the lower-case hex
	/// HMAC-SHA256 of <c>"{t}.{raw body}"</c> keyed with the channel secret (UTF-8). Several v1 values may be sent (secret rotation).
	/// </summary>
	public static class IntakeWebhookSignature
	{
		public const string HeaderName = "X-Resgrid-Signature";

		public static string Compute(string secret, long timestamp, string rawBody)
		{
			using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? string.Empty));
			var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + "." + (rawBody ?? string.Empty)));
			return Convert.ToHexString(hash).ToLowerInvariant();
		}

		public static string Header(string secret, long timestamp, string rawBody) =>
			"t=" + timestamp.ToString(CultureInfo.InvariantCulture) + ",v1=" + Compute(secret, timestamp, rawBody);

		public static IntakeWebhookSignatureResults Verify(string header, string secret, string rawBody, DateTime nowUtc, int toleranceSeconds)
		{
			if (string.IsNullOrWhiteSpace(header))
				return IntakeWebhookSignatureResults.Missing;
			if (string.IsNullOrEmpty(secret))
				return IntakeWebhookSignatureResults.Invalid;

			long? timestamp = null;
			var signatures = new List<string>();
			foreach (var part in header.Split(','))
			{
				var pair = part.Split(new[] { '=' }, 2);
				if (pair.Length != 2)
					return IntakeWebhookSignatureResults.Malformed;

				var key = pair[0].Trim();
				var value = pair[1].Trim();
				if (key == "t")
				{
					if (timestamp.HasValue || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var t))
						return IntakeWebhookSignatureResults.Malformed;
					timestamp = t;
				}
				else if (key == "v1")
				{
					if (value.Length != 64 || !value.All(Uri.IsHexDigit))
						return IntakeWebhookSignatureResults.Malformed;
					signatures.Add(value.ToLowerInvariant());
				}
			}

			if (!timestamp.HasValue || signatures.Count == 0)
				return IntakeWebhookSignatureResults.Malformed;

			var now = new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
			if (Math.Abs(now - timestamp.Value) > Math.Max(1, toleranceSeconds))
				return IntakeWebhookSignatureResults.Stale;

			var expected = Encoding.ASCII.GetBytes(Compute(secret, timestamp.Value, rawBody));
			return signatures.Any(s => CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(s)))
				? IntakeWebhookSignatureResults.Valid
				: IntakeWebhookSignatureResults.Invalid;
		}
	}

	/// <summary><see cref="IntakeResult.Reason"/> values a transport acts on.</summary>
	public static class IntakeResultReasons
	{
		/// <summary>A <see cref="IntakeMessageStates.Duplicate"/> of a message that was already handled (CallId set when it made a call).</summary>
		public const string Duplicate = "Duplicate";

		/// <summary>
		/// A <see cref="IntakeMessageStates.Duplicate"/> whose first delivery is still being processed (or a failed attempt is being
		/// retried) and has no call yet: the transport should ask the provider to deliver again later rather than acknowledge it.
		/// </summary>
		public const string DuplicateInProgress = "DuplicateInProgress";
	}

	/// <summary>Where an intake call came from, for the pending list and View Call ("via SMS: Hospital line").</summary>
	public sealed class IntakeCallSource
	{
		public int CallId { get; set; }

		/// <summary>An <see cref="IntakeChannelTypes"/> value.</summary>
		public int ChannelType { get; set; }

		public string ChannelName { get; set; }
	}

	/// <summary>Rows removed by one worker 80 purge.</summary>
	public sealed class IntakePurgeResult
	{
		public int Messages { get; set; }

		public int VoiceSessions { get; set; }
	}

	/// <summary>Who answers a call to a department's dedicated number (plan D2).</summary>
	public enum VoiceIntakeRoutes
	{
		/// <summary>A member of the number's department: the member IVR, in that department.</summary>
		MemberMenu = 1,

		/// <summary>Anyone else, with voice intake on, Enhanced AI voice intake entitled and under the monthly limit.</summary>
		Intake = 2,

		/// <summary>Voice intake off, not entitled or over the limit: "this line takes text messages".</summary>
		TextOnly = 3,

		/// <summary>The number lapsed: intake has stopped.</summary>
		NumberInactive = 4
	}

	/// <summary>The routing decision for one inbound call to a dedicated number.</summary>
	public sealed class VoiceIntakeRouting
	{
		public VoiceIntakeRoutes Route { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The member, for <see cref="VoiceIntakeRoutes.MemberMenu"/>.</summary>
		public string MemberUserId { get; set; }

		public string Reason { get; set; }
	}

	/// <summary>One spoken (or keyed) answer Twilio posted to the voice intake action.</summary>
	public sealed class VoiceIntakeAnswer
	{
		public string ProviderCallId { get; set; }

		public string FromNumber { get; set; }

		public string ToNumber { get; set; }

		/// <summary>The step the caller was answering (from the action URL), so a stale retry does not answer the wrong question.</summary>
		public int Step { get; set; }

		public string SpeechResult { get; set; }

		public double? Confidence { get; set; }

		public string Digits { get; set; }
	}

	/// <summary>A transcript of one answer.</summary>
	public sealed class VoiceTranscript
	{
		public string Text { get; set; }

		public double? Confidence { get; set; }
	}

	/// <summary>What the voice intake line says and does next; the Twilio controller renders it as TwiML.</summary>
	public sealed class VoiceIntakeTurn
	{
		public VoiceIntakeSteps Step { get; set; }

		/// <summary>Catalog prompts (TwilioVoicePromptCatalog), spoken in order (inside the gather when <see cref="Gather"/>).</summary>
		public List<string> Prompts { get; set; } = new List<string>();

		/// <summary>Listen for the answer to <see cref="Step"/>.</summary>
		public bool Gather { get; set; }

		/// <summary>The callback question also takes digits (star = caller id).</summary>
		public bool AcceptDigits { get; set; }

		public bool HangUp { get; set; }

		public int? CallId { get; set; }
	}
}
