namespace Resgrid.Config
{
	/// <summary>
	/// Intake channels and Enhanced AI voice intake (call-intake-and-ai-destinations-plan.md). Environment keys:
	/// RESGRID:IntakeConfig:&lt;Field&gt;.
	/// </summary>
	public static class IntakeConfig
	{
		/// <summary>Host switch for every intake path (the Intake.Channels flag is the per-department rollout).</summary>
		public static bool Enabled = true;

		/// <summary>Days IntakeMessages audit rows are kept (worker 80 purge).</summary>
		public static int MessageRetentionDays = 90;

		/// <summary>Hours voice intake sessions (transcripts) are kept after they end (worker 80 purge).</summary>
		public static int VoiceSessionRetentionHours = 48;

		/// <summary>Seconds of silence before a spoken answer ends (Twilio speechTimeout; "auto" when 0).</summary>
		public static int VoiceSpeechTimeoutSeconds = 0;

		/// <summary>Re-asks of a voice intake question when nothing usable was heard.</summary>
		public static int VoiceMaxRetries = 2;

		/// <summary>Twilio speech model for &lt;Gather input="speech"&gt; ("phone_call", "experimental_conversations", …); blank = Twilio default.</summary>
		public static string VoiceSpeechModel = "phone_call";

		/// <summary>Maximum characters of an intake message kept on the pending call (longer text is cut with a marker).</summary>
		public static int MaxMessageLength = 4000;

		/// <summary>Webhook intake: maximum request body bytes.</summary>
		public static int MaxWebhookBodyBytes = 65536;

		/// <summary>Webhook intake: allowed clock skew, in seconds, for the signed timestamp header.</summary>
		public static int WebhookTimestampToleranceSeconds = 300;

		/// <summary>
		/// Host switch for SMS conversations (plan §8): a text to a dedicated number opens a thread answered from the master number.
		/// Off = the M0323 behaviour (pending call + optional acknowledgement only).
		/// </summary>
		public static bool ConversationsEnabled = true;

		/// <summary>The emergency number named in texts and voice prompts ("911" US/CA; an EU deployment sets "112").</summary>
		public static string EmergencyNumber = "911";

		/// <summary>Hours after the last message during which the texter's replies to the master number still reach the thread.</summary>
		public static int ConversationReplyWindowHours = 24;

		/// <summary>Minutes without any message after which worker 80 closes a thread (the pending call stays).</summary>
		public static int ConversationIdleCloseMinutes = 240;

		/// <summary>Enhanced AI replies per thread before it stops asking and leaves the thread to a dispatcher.</summary>
		public static int ConversationMaxAssistantTurns = 8;

		/// <summary>Maximum characters of one outbound text (dispatcher or AI); longer texts are refused (dispatcher) or cut (AI).</summary>
		public static int ConversationMaxOutboundLength = 640;

		/// <summary>Webhook intake: requests accepted per channel token per minute and API process (signed or not); more get 429.</summary>
		public static int WebhookRequestsPerMinute = 60;
	}
}
