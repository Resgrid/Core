using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Twilio.TwiML;
using Twilio.TwiML.Voice;

namespace Resgrid.Web.Services.Twilio
{
	/// <summary>
	/// Inbound phone calls for the dispatcher desk (plan D8). Only callers with no member profile reach it (members keep the
	/// voice menu): a call to a department's Text-To-Call number whose desk takes phone calls is put on the desk and the caller
	/// held in its own Twilio queue until a dispatcher answers, or sent to the overflow handling when nobody can.
	/// </summary>
	public interface IDispatcherInboundVoiceHandler
	{
		/// <summary>The TwiML for the caller (hold, or overflow); null when the desk does not take the call, which keeps today's handling.</summary>
		Task<string> HandleAsync(string from, string to, string callSid, CancellationToken cancellationToken = default);
	}

	public class DispatcherInboundVoiceHandler : IDispatcherInboundVoiceHandler
	{
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDispatcherDeskConfigService _dispatcherDeskConfigService;
		private readonly IDispatcherRoutingService _dispatcherRoutingService;
		private readonly IDispatcherVoiceService _dispatcherVoiceService;
		private readonly ILimitsService _limitsService;

		public DispatcherInboundVoiceHandler(IDepartmentSettingsService departmentSettingsService, IDispatcherDeskConfigService dispatcherDeskConfigService,
			IDispatcherRoutingService dispatcherRoutingService, IDispatcherVoiceService dispatcherVoiceService, ILimitsService limitsService)
		{
			_departmentSettingsService = departmentSettingsService;
			_dispatcherDeskConfigService = dispatcherDeskConfigService;
			_dispatcherRoutingService = dispatcherRoutingService;
			_dispatcherVoiceService = dispatcherVoiceService;
			_limitsService = limitsService;
		}

		public async Task<string> HandleAsync(string from, string to, string callSid, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(callSid))
				return null;

			try
			{
				var departmentId = await _departmentSettingsService.GetDepartmentIdByTextToCallNumberAsync(DeskPhoneNumbers.DigitsOnly(to));
				if (!departmentId.HasValue || departmentId.Value <= 0)
					return null;

				var config = await _dispatcherDeskConfigService.GetConfigAsync(departmentId.Value);
				if (config == null || !config.Routes(DispatcherQueueItemKinds.Voice))
					return null;

				// The same plan gate as the member voice menu on a department number.
				if (!await _limitsService.CanDepartmentProvisionNumberAsync(departmentId.Value))
					return null;

				// Twilio re-requests the voice URL when a webhook times out: the same call is never put on the desk twice.
				var item = await _dispatcherRoutingService.GetItemByExternalReferenceAsync(callSid);
				DispatcherRouteOutcomes outcome;

				if (item != null)
				{
					outcome = item.State == (int)DispatcherQueueItemStates.Overflowed ? DispatcherRouteOutcomes.Overflow
						: item.IsOpen() ? DispatcherRouteOutcomes.Duplicate : DispatcherRouteOutcomes.NotRouted;
				}
				else
				{
					var result = await _dispatcherRoutingService.EnqueueAsync(new DispatcherEnqueueRequest
					{
						DepartmentId = departmentId.Value,
						Kind = DispatcherQueueItemKinds.Voice,
						SourceAddress = DeskPhoneNumbers.ToE164(from),
						TargetAddress = DeskPhoneNumbers.ToE164(to),
						ExternalReference = callSid,
						Subject = "Phone call"
					}, CancellationToken.None);

					item = result?.Item;
					outcome = result?.Outcome ?? DispatcherRouteOutcomes.NotRouted;
				}

				if (item == null)
					return null;

				switch (outcome)
				{
					case DispatcherRouteOutcomes.Offered:
					case DispatcherRouteOutcomes.Queued:
					case DispatcherRouteOutcomes.Duplicate:
					case DispatcherRouteOutcomes.Joined:
						return HoldTwiml(item);

					case DispatcherRouteOutcomes.Overflow:
						// The overflow TwiML (and marking the item overflowed) live in one place, DispatcherVoiceController.Overflow.
						return OverflowTwiml(item);

					default:
						return null;
				}
			}
			catch (Exception ex)
			{
				// The caller gets today's handling rather than dead air.
				Logging.LogException(ex, $"Dispatcher desk: inbound call {callSid} could not be put on the desk.");
				return null;
			}
		}

		/// <summary>
		/// Holds the caller in the item's own queue: the wait URL plays the hold prompts and re-checks the item, the action URL hears
		/// how the caller left (hung up, left for overflow, or was bridged to a dispatcher).
		/// </summary>
		public string HoldTwiml(DispatcherQueueItem item)
		{
			var response = new VoiceResponse();
			response.Enqueue(new Enqueue(
				name: _dispatcherVoiceService.QueueNameFor(item),
				action: new Uri(DispatcherVoiceRoutes.Url(DispatcherVoiceRoutes.EnqueueAction, item.DispatcherQueueItemId)),
				method: "POST",
				waitUrl: new Uri(DispatcherVoiceRoutes.Url(DispatcherVoiceRoutes.Hold, item.DispatcherQueueItemId)),
				waitUrlMethod: "POST"));

			return response.ToString();
		}

		public static string OverflowTwiml(DispatcherQueueItem item)
		{
			var response = new VoiceResponse();
			response.Redirect(new Uri(DispatcherVoiceRoutes.OverflowUrl(item.DispatcherQueueItemId)), "POST");

			return response.ToString();
		}
	}
}
