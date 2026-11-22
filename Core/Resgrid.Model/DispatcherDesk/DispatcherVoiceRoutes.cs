using System;

namespace Resgrid.Model
{
	/// <summary>
	/// The Twilio webhooks of the dispatcher desk voice flow (M0324, plan D8), served by Web.Services
	/// <c>DispatcherVoiceController</c> as <c>{api}/api/Twilio/Desk{Action}</c> (beside TwilioController's own actions). The
	/// dispatcher-leg paths (<see cref="DispatcherConnect"/>, <see cref="CallBack"/>, <see cref="Status"/>) and
	/// <see cref="Overflow"/> are also built by <c>DispatcherVoiceService</c> / <c>DispatcherRoutingService</c>: change them together.
	/// </summary>
	public static class DispatcherVoiceRoutes
	{
		public const string RoutePrefix = "api/Twilio";

		/// <summary>The held caller's <c>&lt;Enqueue waitUrl&gt;</c>: hold prompt, then re-check (leave when overflowed or held too long).</summary>
		public const string Hold = "DeskHold";

		/// <summary>The held caller's <c>&lt;Enqueue action&gt;</c>: QueueResult hangup (abandoned), leave (overflow), bridged (ended).</summary>
		public const string EnqueueAction = "DeskEnqueueAction";

		/// <summary>Overflow TwiML for a caller no dispatcher can take (voicemail until Enhanced AI voice intake exists).</summary>
		public const string Overflow = "DeskOverflow";

		/// <summary>The <c>&lt;Record action&gt;</c> of the voicemail: creates the pending call.</summary>
		public const string Voicemail = "DeskVoicemail";

		/// <summary>The voicemail's <c>transcribeCallback</c>: adds the transcription to the pending call.</summary>
		public const string VoicemailTranscription = "DeskVoicemailTranscription";

		/// <summary>TwiML for the dispatcher's leg when they answer: press 1 to connect to the held caller.</summary>
		public const string DispatcherConnect = "DeskConnect";

		/// <summary>The dispatcher's key press on <see cref="DispatcherConnect"/>.</summary>
		public const string DispatcherConnectAction = "DeskConnectConfirm";

		/// <summary>TwiML for the dispatcher's leg of a call back: press 1 to call the caller back from the department number.</summary>
		public const string CallBack = "DeskCallBack";

		/// <summary>The dispatcher's key press on <see cref="CallBack"/>.</summary>
		public const string CallBackAction = "DeskCallBackConfirm";

		/// <summary>
		/// The <c>&lt;Queue url&gt;</c> of the dispatcher's <c>&lt;Dial&gt;</c>: fetched on the held caller's leg the moment the two are
		/// bridged, so VoiceConnected is recorded when the caller is really connected.
		/// </summary>
		public const string Bridge = "DeskBridge";

		/// <summary>The <c>&lt;Dial action&gt;</c> of a call back: records how the call back to the caller ended.</summary>
		public const string CallBackEnded = "DeskCallBackEnded";

		/// <summary>
		/// Status callback of a leg the desk placed (<see cref="Legs"/>). DispatcherVoiceService sends <c>leg=dispatcher</c> for both
		/// Answer and Call back legs; the controller only returns an item to the queue when a held caller is still waiting for it.
		/// </summary>
		public const string Status = "DeskLegStatus";

		/// <summary>Leg names for <see cref="StatusUrl"/>.</summary>
		public static class Legs
		{
			/// <summary>The dispatcher's phone rung to connect them to a held caller.</summary>
			public const string Dispatcher = "dispatcher";

			/// <summary>The dispatcher's phone rung to call an abandoned caller back.</summary>
			public const string CallBack = "callback";
		}

		public static string BaseUrl => (Config.SystemBehaviorConfig.ResgridApiBaseUrl ?? string.Empty).TrimEnd('/');

		public static string Url(string action, string itemId, string userId = null, string leg = null)
		{
			var url = $"{BaseUrl}/{RoutePrefix}/{action}?itemId={Uri.EscapeDataString(itemId ?? string.Empty)}";

			if (!string.IsNullOrWhiteSpace(userId))
				url += $"&userId={Uri.EscapeDataString(userId)}";

			if (!string.IsNullOrWhiteSpace(leg))
				url += $"&leg={Uri.EscapeDataString(leg)}";

			return url;
		}

		/// <summary>The twimlUrl to place the dispatcher's leg with (<c>IDispatcherVoiceProvider.PlaceCallAsync</c>) for Answer.</summary>
		public static string DispatcherConnectUrl(string itemId, string userId) => Url(DispatcherConnect, itemId, userId);

		/// <summary>The twimlUrl to place the dispatcher's leg with for Call back.</summary>
		public static string CallBackUrl(string itemId, string userId) => Url(CallBack, itemId, userId);

		/// <summary>The statusCallbackUrl of a leg the desk places.</summary>
		public static string StatusUrl(string itemId, string userId, string leg) => Url(Status, itemId, userId, leg);

		/// <summary>Where to redirect a held caller (<c>IDispatcherVoiceProvider.RedirectCallAsync</c>) who must overflow now.</summary>
		public static string OverflowUrl(string itemId) => Url(Overflow, itemId);

		/// <summary>Twilio queue names are at most 64 characters; the desk uses one queue per held caller.</summary>
		public static string QueueName(string itemId) => $"rgdesk-{itemId}";
	}
}
