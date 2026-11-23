using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The pending call a voicemail left on the desk becomes (plan D8 voice overflow). Text and chat overflow is the routing
	/// service's own (it makes the pending call from the conversation), so only the voicemail, which arrives on the voice webhook,
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
		/// linked now, and a claim given back by a request that failed before saving is taken again. A claim older than the claim
		/// expiry (its request stopped) is taken over: the call that request may have saved is looked for first; the claim's holder
		/// looks at the item once more just before it saves and saves nothing when its claim was taken over or the item already is its
		/// voicemail call; of two calls saved for one item the one linked first stays and the other is deleted.
		/// </summary>
		Task<DispatcherVoicemailResult> FromVoicemailAsync(DispatcherQueueItem item, string from, string recordingUrl, int? durationSeconds, string recordingSid,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// The worker's recovery of voicemails whose request stopped or could not confirm its save (Twilio never asks again): every
		/// overflowed voice item created since <paramref name="createdSinceUtc"/> that still holds an expired voicemail claim and no call
		/// is finished as a repeat of its voicemail would finish it (<see cref="FromVoicemailAsync"/> takes the expired claim over, links a
		/// call already saved under "DQ:{itemId}", or saves the pending call from the recording on the claim). Returns how many items
		/// became their voicemail call. Each item is isolated: one failing never stops the others.
		/// </summary>
		Task<int> RecoverExpiredVoicemailClaimsAsync(DateTime utcNow, DateTime createdSinceUtc, CancellationToken cancellationToken = default);
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
		/// Whether a call holds the voicemail could not be checked (this request's save failed and the lookup after it too, or the lookup
		/// for the call of a stopped request whose claim this one took over failed): a claim stays on the item (a repeat or the worker's
		/// recovery finds the call, or once the claim expires looks for it before saving), and the caller must not be told it was passed on.
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
}
