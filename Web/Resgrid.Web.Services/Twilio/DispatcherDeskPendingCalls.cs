using System;
using System.Collections.ObjectModel;
using System.Linq;
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
		/// before the call is saved, and a repeat finds the claim or the linked call and saves nothing. A repeat that finds the claim
		/// finishes the claim's work: a call the claim saved but could not link (found by its "DQ:{itemId}" source) is linked now, and a
		/// claim given back by a request that failed before saving is taken again.
		/// </summary>
		Task<DispatcherVoicemailResult> FromVoicemailAsync(DispatcherQueueItem item, string from, string recordingUrl, int? durationSeconds, string recordingSid,
			CancellationToken cancellationToken = default);
	}

	/// <summary>What became of a voicemail (<see cref="IDispatcherDeskPendingCalls.FromVoicemailAsync"/>).</summary>
	public enum DispatcherVoicemailOutcomes
	{
		/// <summary>Saved now as a pending call (<see cref="DispatcherVoicemailResult.Call"/>).</summary>
		Saved = 1,

		/// <summary>The item's voicemail is already a saved call: linked to the item, or saved by another request and linked now.</summary>
		AlreadyTaken = 2,

		/// <summary>Nothing was saved (no item or recording, or the save failed): the caller must not be told it was passed on.</summary>
		NotSaved = 3,

		/// <summary>
		/// Another request holds the item's claim and has not saved a call yet (it may still be saving, or have stopped): nothing is held
		/// for certain, so the caller must not be told it was passed on.
		/// </summary>
		InProgress = 4
	}

	/// <summary>The outcome of a voicemail; made only by <see cref="Of"/>, so an outcome and its call cannot drift apart.</summary>
	public sealed class DispatcherVoicemailResult
	{
		private DispatcherVoicemailResult(DispatcherVoicemailOutcomes outcome, Call call)
		{
			Outcome = outcome;
			Call = call;
		}

		public DispatcherVoicemailOutcomes Outcome { get; }

		/// <summary>The call saved now; null unless <see cref="Outcome"/> is Saved.</summary>
		public Call Call { get; }

		/// <summary>The voicemail is held somewhere a dispatcher will find it (a saved pending call).</summary>
		public bool Durable => Outcome == DispatcherVoicemailOutcomes.Saved || Outcome == DispatcherVoicemailOutcomes.AlreadyTaken;

		public static DispatcherVoicemailResult Of(DispatcherVoicemailOutcomes outcome, Call call = null) =>
			new DispatcherVoicemailResult(outcome, outcome == DispatcherVoicemailOutcomes.Saved ? call : null);
	}

	public class DispatcherDeskPendingCalls : IDispatcherDeskPendingCalls
	{
		/// <summary>The start of the item's overflow note while its voicemail is being made a call (the claim).</summary>
		public const string VoicemailClaimPrefix = "Voicemail recording ";

		/// <summary>Conditional writes tried before giving up (a webhook touching the item bumps its version).</summary>
		public const int WriteAttempts = 3;

		/// <summary>
		/// How many times a repeat that finds another request's claim looks again for that request's call before answering
		/// <see cref="DispatcherVoicemailOutcomes.InProgress"/> (the claim's request may still be saving).
		/// </summary>
		public const int ClaimChecks = 3;

		/// <summary>The wait between those looks (tests set it to zero).</summary>
		public TimeSpan ClaimCheckInterval { get; set; } = TimeSpan.FromSeconds(1);

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
			// names the recording) stays on the item if the link below fails, so a repeat never saves a second call. A repeat that
			// finds the claim finishes its work instead (links the call it saved), and takes the claim again when its request gave it
			// back without saving.
			var note = ClaimNote(recordingSid, recordingUrl);
			ClaimResult claim = null;
			for (var round = 0; round < 2; round++)
			{
				claim = await ClaimAsync(item.DispatcherQueueItemId, note);
				if (claim.Outcome != ClaimOutcomes.ClaimedByAnother)
					break;

				var resumed = await ResumeClaimAsync(item);
				if (resumed != null)
					return resumed;
			}

			switch (claim.Outcome)
			{
				case ClaimOutcomes.Claimed:
					break;
				case ClaimOutcomes.Linked:
					return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.AlreadyTaken);
				case ClaimOutcomes.ClaimedByAnother:
					return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.InProgress);
				default:
					return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.NotSaved);
			}

			Call call;
			try
			{
				call = await BuildCallAsync(item, department, from, recordingUrl, durationSeconds);
			}
			catch (Exception ex)
			{
				// Nothing was saved: give the claim back so a repeat of this voicemail can still save it.
				Logging.LogException(ex, $"Dispatcher desk: the voicemail call for item {item.DispatcherQueueItemId} could not be prepared.");
				await ReleaseClaimAsync(item.DispatcherQueueItemId, note, claim.PreviousNote);
				return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.NotSaved);
			}

			Call saved;
			try
			{
				// The caller has hung up: the save must finish whatever happens to the webhook request.
				saved = await _callsService.SaveCallAsync(call, CancellationToken.None);
			}
			catch (Exception ex)
			{
				// The save may have stored the call before it failed (a later step of saving threw): that call is this voicemail's, found
				// by its "DQ:{itemId}" source, and is finished below instead of being saved twice.
				Logging.LogException(ex, $"Dispatcher desk: the voicemail call for item {item.DispatcherQueueItemId} could not be saved.");
				saved = await FindVoicemailCallAsync(item);
			}

			if (saved == null)
			{
				await ReleaseClaimAsync(item.DispatcherQueueItemId, note, claim.PreviousNote);
				return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.NotSaved);
			}

			await FinishAsync(item, saved);
			return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.Saved, saved);
		}

		private async Task<Call> BuildCallAsync(DispatcherQueueItem item, Department department, string from, string recordingUrl, int? durationSeconds)
		{
			var priorities = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(item.DepartmentId);
			var caller = string.IsNullOrWhiteSpace(from) ? "an unknown number" : from;
			var length = durationSeconds.HasValue && durationSeconds.Value > 0 ? $" ({durationSeconds.Value} seconds)" : string.Empty;
			var now = DateTime.UtcNow;

			return new Call
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
		}

		/// <summary>
		/// The voice item becomes the voicemail call and waits in the queue again (MarkOverflowedAsync reopens an overflowed item that
		/// has a call), then dispatcher screens refresh. Linked before the call is announced, so the desk's call listener finds it on the
		/// desk already. Neither the routing pass nor the announcement can undo the save, so their failures are only logged.
		/// </summary>
		private async Task FinishAsync(DispatcherQueueItem item, Call saved)
		{
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

			try
			{
				// A pending call is not live yet: dispatcher screens refresh, "call added" waits until it is dispatched (as v4 SaveCall).
				_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent { DepartmentId = saved.DepartmentId, Call = saved });
			}
			catch (Exception ex)
			{
				// The voicemail is saved (and linked when that worked): the caller still hears it was passed on.
				Logging.LogException(ex, $"Dispatcher desk: screens could not be refreshed for voicemail call {saved.CallId}.");
			}
		}

		private enum ClaimOutcomes
		{
			/// <summary>This request holds the claim now.</summary>
			Claimed,

			/// <summary>The item already is its call.</summary>
			Linked,

			/// <summary>Another request holds the claim and the item has no call yet.</summary>
			ClaimedByAnother,

			/// <summary>No item, or it kept changing: nothing claimed.</summary>
			Failed
		}

		private sealed class ClaimResult
		{
			public ClaimOutcomes Outcome { get; init; }

			/// <summary>The overflow note the claim replaced, put back if the claim is given back.</summary>
			public string PreviousNote { get; init; }
		}

		private async Task<ClaimResult> ClaimAsync(string itemId, string note)
		{
			for (var attempt = 0; attempt < WriteAttempts; attempt++)
			{
				var current = await _itemsRepository.GetItemAsync(itemId);
				if (current == null)
					return new ClaimResult { Outcome = ClaimOutcomes.Failed };

				if (current.CallId.HasValue)
					return new ClaimResult { Outcome = ClaimOutcomes.Linked };

				if (IsClaimed(current))
					return new ClaimResult { Outcome = ClaimOutcomes.ClaimedByAnother };

				var previous = current.OverflowNote;
				var expected = current.Version;
				current.OverflowNote = Truncate(note);
				if (await _itemsRepository.TryUpdateAsync(current, expected, CancellationToken.None))
					return new ClaimResult { Outcome = ClaimOutcomes.Claimed, PreviousNote = previous };
			}

			// The item kept changing under us: saving anyway could make a second call, and nothing was saved yet.
			Logging.LogError($"Dispatcher desk: voice item {itemId} could not be claimed for its voicemail.");
			return new ClaimResult { Outcome = ClaimOutcomes.Failed };
		}

		/// <summary>
		/// A repeat that found another request's claim. The item is linked: done. A call that request saved (it could not link it, or
		/// is still linking): linked here. The claim was given back (that request failed before saving): null, so the caller claims
		/// again. Otherwise the claim's request may still be saving: looked at again a few times, then <see cref="DispatcherVoicemailOutcomes.InProgress"/>.
		/// </summary>
		private async Task<DispatcherVoicemailResult> ResumeClaimAsync(DispatcherQueueItem item)
		{
			for (var check = 0; check < ClaimChecks; check++)
			{
				if (check > 0 && ClaimCheckInterval > TimeSpan.Zero)
					await Task.Delay(ClaimCheckInterval);

				var current = await _itemsRepository.GetItemAsync(item.DispatcherQueueItemId);
				if (current == null)
					return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.NotSaved);

				if (current.CallId.HasValue)
					return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.AlreadyTaken);

				var saved = await FindVoicemailCallAsync(current);
				if (saved != null)
				{
					// Saved by the claim's request but not linked: link it now. Linking the same call twice is harmless.
					if (await LinkAsync(item, saved))
					{
						try
						{
							await _dispatcherRoutingService.RouteDepartmentAsync(item.DepartmentId, CancellationToken.None);
						}
						catch (Exception ex)
						{
							Logging.LogException(ex, $"Dispatcher desk: routing after relinking voicemail call {saved.CallId}.");
						}
					}

					return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.AlreadyTaken);
				}

				if (!IsClaimed(current))
					return null;
			}

			return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.InProgress);
		}

		/// <summary>The pending call this item's voicemail was saved as (its source is "DQ:{itemId}"), if any.</summary>
		private async Task<Call> FindVoicemailCallAsync(DispatcherQueueItem item)
		{
			try
			{
				var source = "DQ:" + item.DispatcherQueueItemId;
				var pending = await _callsService.GetPendingCallsByDepartmentIdAsync(item.DepartmentId);
				return pending?.FirstOrDefault(x => x != null && !x.IsDeleted && x.CallSource == (int)CallSources.AudioImport &&
													string.Equals(x.SourceIdentifier, source, StringComparison.OrdinalIgnoreCase));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk: the voicemail call of item {item.DispatcherQueueItemId} could not be looked up.");
				return null;
			}
		}

		/// <summary>Gives the claim back (the overflow note it replaced returns) while the item still holds this claim and no call.</summary>
		private async Task ReleaseClaimAsync(string itemId, string note, string previousNote)
		{
			var claimed = Truncate(note);
			for (var attempt = 0; attempt < WriteAttempts; attempt++)
			{
				try
				{
					var current = await _itemsRepository.GetItemAsync(itemId);
					if (current == null || current.CallId.HasValue || !string.Equals(current.OverflowNote, claimed, StringComparison.Ordinal))
						return;

					var expected = current.Version;
					current.OverflowNote = previousNote;
					if (await _itemsRepository.TryUpdateAsync(current, expected, CancellationToken.None))
						return;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Dispatcher desk: the voicemail claim on item {itemId} could not be given back (attempt {attempt + 1}).");
				}
			}

			Logging.LogError($"Dispatcher desk: the voicemail claim on item {itemId} could not be given back; a repeat of the voicemail will wait for it.");
		}

		private static string Truncate(string note) => note == null || note.Length <= 250 ? note : note.Substring(0, 250);

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
					// Only a stored row confirms the link: the item in hand is never taken as proof.
					var current = await _itemsRepository.GetItemAsync(item.DispatcherQueueItemId);
					if (current == null)
					{
						Logging.LogError($"Dispatcher desk: voice item {item.DispatcherQueueItemId} is gone; voicemail call {saved.CallId} waits as a pending call.");
						return false;
					}

					if (current.CallId == saved.CallId)
					{
						item.CallId = saved.CallId;
						return true;
					}

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

			// The call is kept (a pending call, with the recording) and the claim stays on the item, so no repeat makes another call; a
			// repeat of this voicemail finds the call by its "DQ:" source and links it then.
			Logging.LogError($"Dispatcher desk: voicemail call {saved.CallId} is not linked to item {item.DispatcherQueueItemId}; it waits as a pending call.");
			return false;
		}
	}
}
