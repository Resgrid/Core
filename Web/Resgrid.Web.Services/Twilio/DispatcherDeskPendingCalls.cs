using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Twilio
{
	/// <summary>
	/// The pending call a voicemail left on the desk becomes (plan D8 voice overflow). Text and chat overflow is the routing
	/// service's own (it makes the pending call from the conversation), so only the voicemail, which exists only on this webhook,
	/// is made here.
	/// </summary>
	public interface IDispatcherDeskPendingCalls
	{
		/// <summary>
		/// Saves the voicemail as a <see cref="CallStates.Pending"/> call (nobody paged, recording in its notes) and makes the
		/// overflowed voice item that call: its CallId is set and <see cref="IDispatcherRoutingService.MarkOverflowedAsync"/> puts it
		/// back in the queue, so it waits for a dispatcher as the voicemail call with the caller's number (Call back works from it).
		/// No second desk item is made, and an item gets at most one voicemail call: the item is claimed (its recording noted on it)
		/// before the call is saved, and a repeat finds the claim or the linked call and saves nothing.
		/// </summary>
		Task<DispatcherVoicemailResult> FromVoicemailAsync(DispatcherQueueItem item, string from, string recordingUrl, int? durationSeconds, string recordingSid,
			CancellationToken cancellationToken = default);
	}

	/// <summary>What became of a voicemail (<see cref="IDispatcherDeskPendingCalls.FromVoicemailAsync"/>).</summary>
	public enum DispatcherVoicemailOutcomes
	{
		/// <summary>Saved now as a pending call (<see cref="DispatcherVoicemailResult.Call"/>).</summary>
		Saved = 1,

		/// <summary>The item's voicemail was already taken: it has its call, or another request claimed it.</summary>
		AlreadyTaken = 2,

		/// <summary>Nothing was saved (no item or recording, or the save failed): the caller must not be told it was passed on.</summary>
		NotSaved = 3
	}

	public sealed class DispatcherVoicemailResult
	{
		public DispatcherVoicemailOutcomes Outcome { get; set; }

		/// <summary>The call saved now; null unless <see cref="Outcome"/> is Saved.</summary>
		public Call Call { get; set; }

		/// <summary>The voicemail is held somewhere a dispatcher will find it (a pending call).</summary>
		public bool Durable => Outcome == DispatcherVoicemailOutcomes.Saved || Outcome == DispatcherVoicemailOutcomes.AlreadyTaken;

		public static DispatcherVoicemailResult Of(DispatcherVoicemailOutcomes outcome, Call call = null) =>
			new DispatcherVoicemailResult { Outcome = outcome, Call = call };
	}

	public class DispatcherDeskPendingCalls : IDispatcherDeskPendingCalls
	{
		/// <summary>The start of the item's overflow note while its voicemail is being made a call (the claim).</summary>
		public const string VoicemailClaimPrefix = "Voicemail recording ";

		/// <summary>Conditional writes tried before giving up (a webhook touching the item bumps its version).</summary>
		public const int WriteAttempts = 3;

		private readonly IDepartmentsService _departmentsService;
		private readonly ICallsService _callsService;
		private readonly IDispatcherRoutingService _dispatcherRoutingService;
		private readonly IDispatcherQueueItemsRepository _itemsRepository;
		private readonly IEventAggregator _eventAggregator;

		public DispatcherDeskPendingCalls(IDepartmentsService departmentsService, ICallsService callsService, IDispatcherRoutingService dispatcherRoutingService,
			IDispatcherQueueItemsRepository itemsRepository, IEventAggregator eventAggregator)
		{
			_departmentsService = departmentsService;
			_callsService = callsService;
			_dispatcherRoutingService = dispatcherRoutingService;
			_itemsRepository = itemsRepository;
			_eventAggregator = eventAggregator;
		}

		/// <summary>The claim written on the item: the recording, so a voicemail whose save failed can still be found on the item.</summary>
		public static string ClaimNote(string recordingSid, string recordingUrl) =>
			$"{VoicemailClaimPrefix}{(string.IsNullOrWhiteSpace(recordingSid) ? "(no id)" : recordingSid.Trim())}: {recordingUrl}";

		public static bool IsClaimed(DispatcherQueueItem item) =>
			item?.OverflowNote != null && item.OverflowNote.StartsWith(VoicemailClaimPrefix, StringComparison.Ordinal);

		public async Task<DispatcherVoicemailResult> FromVoicemailAsync(DispatcherQueueItem item, string from, string recordingUrl, int? durationSeconds, string recordingSid,
			CancellationToken cancellationToken = default)
		{
			if (item == null || string.IsNullOrWhiteSpace(recordingUrl))
				return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.NotSaved);

			if (item.CallId.HasValue)
				return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.AlreadyTaken);

			var department = await _departmentsService.GetDepartmentByIdAsync(item.DepartmentId);
			if (department == null)
				return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.NotSaved);

			// Claim the item before the save: of two requests for the same voicemail only one saves a call, and the claim (which
			// names the recording) stays on the item if the link below fails, so a repeat never saves a second call.
			var claim = await ClaimAsync(item.DispatcherQueueItemId, ClaimNote(recordingSid, recordingUrl));
			if (claim != DispatcherVoicemailOutcomes.Saved)
				return DispatcherVoicemailResult.Of(claim);

			var priorities = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(item.DepartmentId);
			var caller = string.IsNullOrWhiteSpace(from) ? "an unknown number" : from;
			var length = durationSeconds.HasValue && durationSeconds.Value > 0 ? $" ({durationSeconds.Value} seconds)" : string.Empty;
			var now = DateTime.UtcNow;

			var call = new Call
			{
				DepartmentId = item.DepartmentId,
				ReportingUserId = department.ManagingUserId,
				Priority = CallPriorityMatch.DefaultForImport(priorities),
				Name = $"Voicemail {now.TimeConverter(department):g}",
				NatureOfCall = $"Voicemail from {caller}",
				Notes = $"Voicemail from {caller}{length}. Recording: {recordingUrl}",
				ContactNumber = from,
				SourceIdentifier = "DQ:" + item.DispatcherQueueItemId,
				CallSource = (int)CallSources.AudioImport,
				LoggedOn = now,
				// As v4 SaveCall and the web's Save as Pending store a pending call: no dispatch time, never dispatched (null, not
				// false, which marks a scheduled call the scheduler still has to send). Nobody is proposed.
				State = (int)CallStates.Pending,
				DispatchOn = null,
				HasBeenDispatched = null,
				Dispatches = new Collection<CallDispatch>()
			};

			// The caller has hung up: the save must finish whatever happens to the webhook request.
			var saved = await _callsService.SaveCallAsync(call, CancellationToken.None);
			if (saved == null)
				return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.NotSaved);

			// The voice item becomes the voicemail call and waits in the queue again (MarkOverflowedAsync reopens an overflowed item
			// that has a call). Done before the call is announced, so the desk's call listener finds it on the desk already.
			if (await LinkAsync(item, saved))
			{
				try
				{
					await _dispatcherRoutingService.RouteDepartmentAsync(item.DepartmentId, CancellationToken.None);
				}
				catch (Exception ex)
				{
					// The item waits in the queue; the worker offers it on its next pass.
					Logging.LogException(ex, $"Dispatcher desk: routing after voicemail call {saved.CallId}.");
				}
			}

			// A pending call is not live yet: dispatcher screens refresh, "call added" waits until it is dispatched (as v4 SaveCall).
			_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent { DepartmentId = saved.DepartmentId, Call = saved });

			return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.Saved, saved);
		}

		/// <summary>Saved = this request holds the claim now; AlreadyTaken = the item has its call or another request's claim.</summary>
		private async Task<DispatcherVoicemailOutcomes> ClaimAsync(string itemId, string note)
		{
			for (var attempt = 0; attempt < WriteAttempts; attempt++)
			{
				var current = await _itemsRepository.GetItemAsync(itemId);
				if (current == null)
					return DispatcherVoicemailOutcomes.NotSaved;

				if (current.CallId.HasValue || IsClaimed(current))
					return DispatcherVoicemailOutcomes.AlreadyTaken;

				var expected = current.Version;
				current.OverflowNote = note.Length <= 250 ? note : note.Substring(0, 250);
				if (await _itemsRepository.TryUpdateAsync(current, expected, CancellationToken.None))
					return DispatcherVoicemailOutcomes.Saved;
			}

			// The item kept changing under us: saving anyway could make a second call, and nothing was saved yet.
			Logging.LogError($"Dispatcher desk: voice item {itemId} could not be claimed for its voicemail.");
			return DispatcherVoicemailOutcomes.NotSaved;
		}

		/// <summary>
		/// Links the saved call to the voice item, checked by reading the item back: MarkOverflowedAsync gives up quietly when another
		/// webhook changed the item meanwhile, so each attempt starts from the stored row. False when the item still is not the call.
		/// </summary>
		private async Task<bool> LinkAsync(DispatcherQueueItem item, Call saved)
		{
			var note = $"Voicemail saved as pending call {(string.IsNullOrWhiteSpace(saved.Number) ? saved.CallId.ToString() : saved.Number)}";

			for (var attempt = 0; attempt < WriteAttempts; attempt++)
			{
				try
				{
					var current = await _itemsRepository.GetItemAsync(item.DispatcherQueueItemId) ?? item;
					if (current.CallId == saved.CallId)
						return true;

					current.CallId = saved.CallId;
					await _dispatcherRoutingService.MarkOverflowedAsync(current, note, CancellationToken.None);

					var linked = await _itemsRepository.GetItemAsync(item.DispatcherQueueItemId);
					if (linked?.CallId == saved.CallId)
					{
						item.CallId = saved.CallId;
						return true;
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Dispatcher desk: voicemail call {saved.CallId} could not be linked to item {item.DispatcherQueueItemId} (attempt {attempt + 1}).");
				}
			}

			// The call is kept (a pending call, with the recording) and the claim stays on the item, so no repeat makes another call.
			Logging.LogError($"Dispatcher desk: voicemail call {saved.CallId} is not linked to item {item.DispatcherQueueItemId}; it waits as a pending call.");
			return false;
		}
	}
}
