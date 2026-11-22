using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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
		/// finishes the claim's work: a call the claim saved but could not link (found by its "DQ:{itemId}" source, in any state) is
		/// linked now, and a claim given back by a request that failed before saving is taken again. A claim older than
		/// <see cref="DispatcherDeskPendingCalls.ClaimExpiryMinutes"/> (its request stopped) is taken over: the call that request may have
		/// saved is looked for first, and of two calls saved for one item the one linked first stays and the other is deleted.
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
		InProgress = 4,

		/// <summary>
		/// The save failed and whether it stored the call could not be checked: the claim stays on the item (a repeat finds the call, or
		/// once the claim expires looks for it before saving), and the caller must not be told it was passed on.
		/// </summary>
		Unconfirmed = 5
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

		/// <summary>
		/// How long a claim may stand with no call before a repeat of the voicemail takes it over: its request stopped (the host went
		/// down between the claim and the save). Far longer than any save, so a live request is never overtaken in practice; if one is,
		/// the first of the two calls linked to the item stays and the other is deleted.
		/// </summary>
		public const int ClaimExpiryMinutes = 5;

		/// <summary>Who deletes a second call saved for an item that already is its voicemail call.</summary>
		public const string SystemUserId = "system";

		/// <summary>The clock of claims (tests replace it).</summary>
		public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

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

		/// <summary>
		/// The claim written on the item: the recording, so a voicemail whose save failed can still be found on the item, and when it was
		/// claimed (before the URL, so the 250-character note keeps it), which also makes each claim's note its own.
		/// </summary>
		public static string ClaimNote(string recordingSid, string recordingUrl, DateTime claimedOn) =>
			$"{VoicemailClaimPrefix}{(string.IsNullOrWhiteSpace(recordingSid) ? "(no id)" : recordingSid.Trim())} " +
			$"(claimed {DateTime.SpecifyKind(claimedOn, DateTimeKind.Utc).ToString(ClaimStampFormat, CultureInfo.InvariantCulture)}): {recordingUrl}";

		public static bool IsClaimed(DispatcherQueueItem item) =>
			item?.OverflowNote != null && item.OverflowNote.StartsWith(VoicemailClaimPrefix, StringComparison.Ordinal);

		private const string ClaimStampFormat = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

		private static readonly Regex ClaimStamp = new Regex(@" \(claimed (\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z)\): ", RegexOptions.CultureInvariant,
			TimeSpan.FromMilliseconds(100));

		/// <summary>When the item's voicemail claim was written; null when the note is no claim or carries no time.</summary>
		public static DateTime? ClaimedOn(DispatcherQueueItem item)
		{
			if (!IsClaimed(item))
				return null;

			var match = ClaimStamp.Match(item.OverflowNote);
			return match.Success && DateTime.TryParseExact(match.Groups[1].Value, ClaimStampFormat, CultureInfo.InvariantCulture,
				DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var claimedOn)
				? DateTime.SpecifyKind(claimedOn, DateTimeKind.Utc)
				: (DateTime?)null;
		}

		/// <summary>
		/// The item's claim has stood for <see cref="ClaimExpiryMinutes"/> with no call: its request stopped. A claim with no readable
		/// time counts as stale (it can only come from a request that no longer runs this code).
		/// </summary>
		public static bool ClaimExpired(DispatcherQueueItem item, DateTime now)
		{
			if (!IsClaimed(item) || item.CallId.HasValue)
				return false;

			var claimedOn = ClaimedOn(item);
			return !claimedOn.HasValue || claimedOn.Value <= now.AddMinutes(-ClaimExpiryMinutes);
		}

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
			var note = ClaimNote(recordingSid, recordingUrl, UtcNow());
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

			if (claim.TookOver)
			{
				// The stopped request may have saved its call before it stopped (and never linked it): that call is this voicemail's.
				var earlier = await FindVoicemailCallAsync(item);
				if (!earlier.Known)
				{
					// Saving now could make a second call. The claim goes back as it was, for a repeat to look again.
					await ReleaseClaimAsync(item.DispatcherQueueItemId, note, claim.PreviousNote);
					return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.NotSaved);
				}

				if (earlier.Call != null)
				{
					await LinkFoundAsync(item, earlier.Call);
					return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.AlreadyTaken);
				}
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
				var stored = await FindVoicemailCallAsync(item);
				if (!stored.Known)
				{
					// The save may have stored the call: giving the claim back would let a repeat save a second one. The claim stays; a
					// repeat finds the call by its source, or, once the claim expires, looks for it before it saves.
					Logging.LogError($"Dispatcher desk: whether the voicemail call for item {item.DispatcherQueueItemId} was saved is unknown; its claim is kept.");
					return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.Unconfirmed);
				}

				saved = stored.Call;
			}

			if (saved == null)
			{
				await ReleaseClaimAsync(item.DispatcherQueueItemId, note, claim.PreviousNote);
				return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.NotSaved);
			}

			return await FinishAsync(item, saved)
				? DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.Saved, saved)
				: DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.AlreadyTaken);
		}

		private async Task<Call> BuildCallAsync(DispatcherQueueItem item, Department department, string from, string recordingUrl, int? durationSeconds)
		{
			var priorities = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(item.DepartmentId);
			var caller = string.IsNullOrWhiteSpace(from) ? "an unknown number" : from;
			var length = durationSeconds.HasValue && durationSeconds.Value > 0 ? $" ({durationSeconds.Value} seconds)" : string.Empty;
			var now = UtcNow();

			return new Call
			{
				DepartmentId = item.DepartmentId,
				ReportingUserId = department.ManagingUserId,
				Priority = CallPriorityMatch.DefaultForImport(priorities),
				Name = $"Voicemail {now.TimeConverter(department):g}",
				NatureOfCall = $"Voicemail from {caller}",
				Notes = $"Voicemail from {caller}{length}. Recording: {recordingUrl}",
				ContactNumber = from,
				SourceIdentifier = SourceOf(item),
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
		/// desk already. Neither the routing pass nor the announcement can undo the save, so their failures are only logged. False when
		/// the item already was another voicemail call of its own (a request whose claim was taken over saved one too): this second call
		/// is deleted, never announced.
		/// </summary>
		private async Task<bool> FinishAsync(DispatcherQueueItem item, Call saved)
		{
			var link = await LinkAsync(item, saved);
			if (link.Result == LinkResults.Linked)
			{
				await RouteAfterLinkAsync(item, saved);
			}
			else if (link.Result == LinkResults.LinkedToAnother && await DiscardSecondCallAsync(item, saved, link.OtherCallId.Value))
			{
				return false;
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

			return true;
		}

		private async Task RouteAfterLinkAsync(DispatcherQueueItem item, Call saved)
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

		/// <summary>
		/// A call another request saved for this voicemail (found by its source): linked to the item now. Linking the same call twice is
		/// harmless; an item that already is another of its voicemail calls keeps that one (the found call's own request deletes it).
		/// </summary>
		private async Task LinkFoundAsync(DispatcherQueueItem item, Call found)
		{
			if ((await LinkAsync(item, found)).Result == LinkResults.Linked)
				await RouteAfterLinkAsync(item, found);
		}

		/// <summary>
		/// <paramref name="saved"/> is a second call for the item, which already is <paramref name="linkedCallId"/>: deleted when that call
		/// is the item's own voicemail call (its "DQ:" source), so one voicemail never waits as two pending calls. A linked call that is not
		/// a voicemail call of the item, or one that cannot be read, keeps this call: the recording is not dropped. True when deleted.
		/// </summary>
		private async Task<bool> DiscardSecondCallAsync(DispatcherQueueItem item, Call saved, int linkedCallId)
		{
			try
			{
				var linked = await _callsService.GetCallByIdAsync(linkedCallId);
				if (linked == null || linked.IsDeleted || !IsVoicemailCallOf(linked, item))
				{
					Logging.LogError($"Dispatcher desk: voice item {item.DispatcherQueueItemId} is call {linkedCallId}; voicemail call {saved.CallId} waits as its own pending call.");
					return false;
				}

				saved.IsDeleted = true;
				saved.DeletedOn = UtcNow();
				saved.DeletedByUserId = SystemUserId;
				saved.DeletedReason = $"A second call for the same voicemail: desk item {item.DispatcherQueueItemId} already is call {linked.Number ?? linked.CallId.ToString()}.";
				await _callsService.SaveCallAsync(saved, CancellationToken.None);
				Logging.LogInfo($"[Dispatcher desk] Voicemail call {saved.CallId} deleted: item {item.DispatcherQueueItemId} already is voicemail call {linked.CallId}.");
				return true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk: second voicemail call {saved.CallId} of item {item.DispatcherQueueItemId} could not be deleted.");
				return false;
			}
		}

		private static string SourceOf(DispatcherQueueItem item) => "DQ:" + item.DispatcherQueueItemId;

		private static bool IsVoicemailCallOf(Call call, DispatcherQueueItem item) =>
			call != null && call.DepartmentId == item.DepartmentId && call.CallSource == (int)CallSources.AudioImport &&
			string.Equals(call.SourceIdentifier, SourceOf(item), StringComparison.OrdinalIgnoreCase);

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

			/// <summary>The claim replaced an expired one: that claim's request may have saved a call before it stopped.</summary>
			public bool TookOver { get; init; }
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

				// A live claim belongs to its request; an expired one is taken over, fenced on the row read (a claim written or renewed
				// meanwhile, or the call linked, fails the write and is judged again).
				var takeOver = IsClaimed(current);
				if (takeOver && !ClaimExpired(current, UtcNow()))
					return new ClaimResult { Outcome = ClaimOutcomes.ClaimedByAnother };

				var previous = current.OverflowNote;
				var expected = current.Version;
				current.OverflowNote = Truncate(note);
				if (await _itemsRepository.TryUpdateAsync(current, expected, CancellationToken.None))
				{
					if (takeOver)
						Logging.LogInfo($"[Dispatcher desk] Voice item {itemId}: an expired voicemail claim was taken over.");

					return new ClaimResult { Outcome = ClaimOutcomes.Claimed, PreviousNote = previous, TookOver = takeOver };
				}
			}

			// The item kept changing under us: saving anyway could make a second call, and nothing was saved yet.
			Logging.LogError($"Dispatcher desk: voice item {itemId} could not be claimed for its voicemail.");
			return new ClaimResult { Outcome = ClaimOutcomes.Failed };
		}

		/// <summary>
		/// A repeat that found another request's claim. The item is linked: done. A call that request saved (it could not link it, or
		/// is still linking): linked here. The claim was given back (that request failed before saving), or has expired (that request
		/// stopped): null, so the caller claims again (taking an expired claim over). Otherwise the claim's request may still be saving:
		/// looked at again a few times, then <see cref="DispatcherVoicemailOutcomes.InProgress"/>.
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

				// A lookup that failed proves nothing either way: the claim is looked at again.
				var saved = await FindVoicemailCallAsync(current);
				if (saved.Call != null)
				{
					// Saved by the claim's request but not linked: link it now.
					await LinkFoundAsync(item, saved.Call);
					return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.AlreadyTaken);
				}

				if (!IsClaimed(current) || ClaimExpired(current, UtcNow()))
					return null;
			}

			return DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.InProgress);
		}

		/// <summary>What looking for an item's voicemail call found: <see cref="Known"/> is false when the lookup failed (not "none").</summary>
		private readonly struct VoicemailLookup
		{
			public VoicemailLookup(bool known, Call call)
			{
				Known = known;
				Call = call;
			}

			public bool Known { get; }

			public Call Call { get; }
		}

		/// <summary>
		/// The call this item's voicemail was saved as (its source is "DQ:{itemId}"), in any state: a dispatcher may have dispatched or
		/// closed it before the item was linked. Logged after the item was created, so the search is bounded to that window; of two (a
		/// claim taken over), the first saved. A failed lookup is reported as unknown, never as no call.
		/// </summary>
		private async Task<VoicemailLookup> FindVoicemailCallAsync(DispatcherQueueItem item)
		{
			try
			{
				var now = UtcNow();
				var from = item.CreatedOn > now.AddDays(-30) ? item.CreatedOn.AddMinutes(-1) : now.AddDays(-1);
				var calls = await _callsService.GetAllCallsByDepartmentDateRangeAsync(item.DepartmentId, from, now.AddMinutes(1));
				var call = (calls ?? new List<Call>())
					.Where(x => x != null && !x.IsDeleted && IsVoicemailCallOf(x, item))
					.OrderBy(x => x.CallId)
					.FirstOrDefault();

				return new VoicemailLookup(true, call);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk: the voicemail call of item {item.DispatcherQueueItemId} could not be looked up.");
				return new VoicemailLookup(false, null);
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

		private enum LinkResults
		{
			/// <summary>The stored item is the call.</summary>
			Linked,

			/// <summary>The stored item already is another call (a link is never replaced: the first call linked stays the item's).</summary>
			LinkedToAnother,

			/// <summary>Not linked (the item is gone, or the writes kept failing).</summary>
			Failed
		}

		/// <summary>
		/// Links the saved call to the voice item, checked by reading the item back: MarkOverflowedAsync gives up quietly when another
		/// webhook changed the item meanwhile, so each attempt starts from the stored row. An item that already is another call keeps it
		/// (MarkOverflowedAsync never replaces a CallId): <see cref="LinkResults.LinkedToAnother"/>, with that call's id.
		/// </summary>
		private async Task<(LinkResults Result, int? OtherCallId)> LinkAsync(DispatcherQueueItem item, Call saved)
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
						return (LinkResults.Failed, null);
					}

					if (current.CallId == saved.CallId)
					{
						item.CallId = saved.CallId;
						return (LinkResults.Linked, null);
					}

					if (current.CallId.HasValue)
						return (LinkResults.LinkedToAnother, current.CallId);

					current.CallId = saved.CallId;
					await _dispatcherRoutingService.MarkOverflowedAsync(current, note, CancellationToken.None);

					var linked = await _itemsRepository.GetItemAsync(item.DispatcherQueueItemId);
					if (linked?.CallId == saved.CallId)
					{
						item.CallId = saved.CallId;
						return (LinkResults.Linked, null);
					}

					if (linked?.CallId != null)
						return (LinkResults.LinkedToAnother, linked.CallId);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Dispatcher desk: voicemail call {saved.CallId} could not be linked to item {item.DispatcherQueueItemId} (attempt {attempt + 1}).");
				}
			}

			// The call is kept (a pending call, with the recording) and the claim stays on the item, so no repeat makes another call; a
			// repeat of this voicemail finds the call by its "DQ:" source and links it then.
			Logging.LogError($"Dispatcher desk: voicemail call {saved.CallId} is not linked to item {item.DispatcherQueueItemId}; it waits as a pending call.");
			return (LinkResults.Failed, null);
		}
	}
}
