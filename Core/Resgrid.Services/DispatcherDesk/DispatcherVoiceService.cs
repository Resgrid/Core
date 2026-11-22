using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Connecting dispatchers to callers (M0324, plan D8). No VoIP in the app: the desk rings the dispatcher's own phone (their
	/// desk callback number, else their profile mobile), they press 1, and the held caller is bridged in (Twilio queue). Works the
	/// same on a phone in a pocket and a tablet with a desk phone beside it. The TwiML lives in Web.Services'
	/// DispatcherVoiceController at the URLs below.
	/// </summary>
	public class DispatcherVoiceService : IDispatcherVoiceService
	{
		public const int RingTimeoutSeconds = 30;

		private readonly IDispatcherVoiceProvider _voiceProvider;
		private readonly IDispatcherRoutingService _routingService;
		private readonly IDispatcherPresencesRepository _presencesRepository;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public DispatcherVoiceService(IDispatcherVoiceProvider voiceProvider, IDispatcherRoutingService routingService,
			IDispatcherPresencesRepository presencesRepository, IUserProfileService userProfileService, IDepartmentSettingsService departmentSettingsService)
		{
			_voiceProvider = voiceProvider;
			_routingService = routingService;
			_presencesRepository = presencesRepository;
			_userProfileService = userProfileService;
			_departmentSettingsService = departmentSettingsService;
		}

		public string QueueNameFor(DispatcherQueueItem item) => "rgdesk-" + item?.DispatcherQueueItemId;

		public static string ConnectUrl(string itemId, string userId) => Url("DeskConnect", itemId, userId);

		public static string CallBackUrl(string itemId, string userId) => Url("DeskCallBack", itemId, userId);

		/// <summary>
		/// The status callback of a dispatcher leg. <paramref name="attemptStartedOn"/> (UTC ticks, taken before the leg is placed) names
		/// the ring attempt, so a late terminal status of an earlier attempt does not return the item while a newer leg rings.
		/// </summary>
		public static string LegStatusUrl(string itemId, string userId, DateTime? attemptStartedOn = null) =>
			Url("DeskLegStatus", itemId, userId) + "&leg=dispatcher" +
			(attemptStartedOn.HasValue ? "&attempt=" + attemptStartedOn.Value.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty);

		/// <summary>The clock of ring attempts (tests replace it).</summary>
		public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

		private static string Url(string action, string itemId, string userId) =>
			$"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/{action}?itemId={Uri.EscapeDataString(itemId ?? string.Empty)}&userId={Uri.EscapeDataString(userId ?? string.Empty)}";

		public Task<DispatcherDeskResult<DispatcherQueueItem>> ConnectAsync(int departmentId, string dispatcherQueueItemId, string userId, string callbackNumber,
			CancellationToken cancellationToken = default) =>
			RingDispatcherAsync(departmentId, dispatcherQueueItemId, userId, callbackNumber, callBack: false, cancellationToken);

		public Task<DispatcherDeskResult<DispatcherQueueItem>> CallBackAsync(int departmentId, string dispatcherQueueItemId, string userId, string callbackNumber,
			CancellationToken cancellationToken = default) =>
			RingDispatcherAsync(departmentId, dispatcherQueueItemId, userId, callbackNumber, callBack: true, cancellationToken);

		public async Task<string> ResolveCallbackNumberAsync(int departmentId, string userId, string requested)
		{
			var number = DispatcherPresenceService.NormalizePhone(requested);
			if (number != null)
				return number;

			var presence = await _presencesRepository.GetForUserAsync(departmentId, userId);
			number = DispatcherPresenceService.NormalizePhone(presence?.CallbackNumber);
			if (number != null)
				return number;

			var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
			return DispatcherPresenceService.NormalizePhone(profile?.MobileNumber);
		}

		public async Task ReleaseQueueAsync(string queueSid, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(queueSid))
				return;

			try
			{
				await _voiceProvider.DeleteQueueAsync(queueSid, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk hold queue {queueSid} cleanup");
			}
		}

		private async Task<DispatcherDeskResult<DispatcherQueueItem>> RingDispatcherAsync(int departmentId, string itemId, string userId, string callbackNumber,
			bool callBack, CancellationToken cancellationToken)
		{
			var detail = await _routingService.GetItemDetailAsync(departmentId, itemId);
			if (detail?.Item == null)
				return DispatcherDeskResult<DispatcherQueueItem>.Fail(DispatcherDeskErrors.ItemNotFound);

			var item = detail.Item;
			if (!item.IsOpen())
				return DispatcherDeskResult<DispatcherQueueItem>.Fail(DispatcherDeskErrors.ItemNotAvailable);

			if (callBack
					? !DispatcherRoutingPolicy.CanCallBack((DispatcherQueueItemKinds)item.Kind) || DispatcherPresenceService.NormalizePhone(item.SourceAddress) == null
					: item.Kind != (int)DispatcherQueueItemKinds.Voice)
				return DispatcherDeskResult<DispatcherQueueItem>.Fail(DispatcherDeskErrors.ChannelUnavailable);

			var number = await ResolveCallbackNumberAsync(departmentId, userId, callbackNumber);
			if (number == null)
				return DispatcherDeskResult<DispatcherQueueItem>.Fail(DispatcherDeskErrors.NoCallbackNumber);

			// Every prerequisite of the dial is checked before the item is taken: a department with no number to call from must
			// leave the work where it was (offered, or waiting in the queue for overflow), not held by someone who cannot call.
			var from = DispatcherPresenceService.NormalizePhone(item.TargetAddress) ??
					   DispatcherPresenceService.NormalizePhone(await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(departmentId));
			if (from == null)
				return DispatcherDeskResult<DispatcherQueueItem>.Fail(DispatcherDeskErrors.ChannelUnavailable, "The department has no number to call from.");

			// The dispatcher takes the item first (an offer to them, or the queue), so nobody else is offered it while their phone rings.
			var acquiredNow = false;
			if (!(item.State == (int)DispatcherQueueItemStates.Accepted && string.Equals(item.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase)))
			{
				var accepted = await _routingService.AcceptAsync(departmentId, itemId, userId, cancellationToken);
				if (!accepted.Success)
					return accepted;
				item = accepted.Value;
				acquiredNow = true;
			}

			var attemptStartedOn = UtcNow();
			string sid;
			try
			{
				sid = await _voiceProvider.PlaceCallAsync(number, from, callBack ? CallBackUrl(item.DispatcherQueueItemId, userId) : ConnectUrl(item.DispatcherQueueItemId, userId),
					LegStatusUrl(item.DispatcherQueueItemId, userId, attemptStartedOn), RingTimeoutSeconds, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk voice leg for {item.DispatcherQueueItemId}");
				sid = null;
			}

			if (string.IsNullOrWhiteSpace(sid))
			{
				// Work this attempt took goes back where it came from, so the held caller is offered on (or overflows) instead of
				// waiting on a dispatcher with no leg. Releasing is safe even when the provider did create the leg after all: its
				// TwiML refuses an item the dispatcher no longer holds, or takes it again from the queue on "press 1". Work the
				// dispatcher already held stays theirs.
				if (acquiredNow)
					await ReleaseAfterFailedDialAsync(departmentId, item.DispatcherQueueItemId, userId);

				return DispatcherDeskResult<DispatcherQueueItem>.Fail(DispatcherDeskErrors.ChannelUnavailable, "The call to your phone could not be placed.");
			}

			await _routingService.RecordVoiceEventAsync(item, DispatcherQueueEventTypes.VoiceConnecting, userId, (callBack ? "Call back via " : "Ringing ") + Mask(number),
				cancellationToken);

			return DispatcherDeskResult<DispatcherQueueItem>.Ok(item);
		}

		private async Task ReleaseAfterFailedDialAsync(int departmentId, string itemId, string userId)
		{
			try
			{
				// The dispatcher's request may be gone; the item must not stay held either way.
				var released = await _routingService.ReleaseAsync(departmentId, itemId, userId, CancellationToken.None);
				if (released == null || !released.Success)
					Logging.LogInfo($"[Dispatcher desk] Item {itemId} could not be returned after a failed dial ({released?.Error}).");
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk: item {itemId} could not be returned after a failed dial.");
			}
		}

		/// <summary>The last four digits only, for the item history.</summary>
		internal static string Mask(string number) =>
			string.IsNullOrEmpty(number) || number.Length <= 4 ? number : new string('•', Math.Min(6, number.Length - 4)) + number.Substring(number.Length - 4);
	}
}
