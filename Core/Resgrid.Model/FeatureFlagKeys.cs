namespace Resgrid.Model
{
	/// <summary>
	/// Well-known feature flag keys consumed by application code. Keys are matched case-insensitively by
	/// <see cref="Services.IFeatureToggleService"/>. When adding a key here, seed a matching flag row via a
	/// migration (see M0072) so the flag is immediately manageable from the admin UI/API.
	/// </summary>
	public static class FeatureFlagKeys
	{
		/// <summary>
		/// Routes inbound Twilio SMS through the new chatbot ingress pipeline. When off (globally or for a
		/// specific department) the original text-command handling in TwilioController is used instead.
		/// </summary>
		public const string ChatbotTwilioTextIntegration = "Chatbot.TwilioTextIntegration";

		/// <summary>
		/// Gates the realtime chat system (channels, DMs, incident chat, chatbot conversation) across the
		/// API, web UI and mobile apps. Free for all plans; used for staged rollout only. Seeded by M0108.
		/// </summary>
		public const string ChatSystem = "Chat.System";

		/// <summary>
		/// Gates the run card dispatch system (run cards, station-based dispatching, closest unit
		/// response, move-up recommendations) across the web UI and API. Seeded by M0116.
		/// </summary>
		public const string DispatchRunCards = "Dispatch.RunCards";
	}
}
