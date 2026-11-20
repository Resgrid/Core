using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Enhanced AI destination advice (call-intake-and-ai-destinations-plan.md D4, D9; M0323): the model reads the call's nature, note
	/// and call notes beside the department's rule-ranked destinations (capabilities, availability, drive time) and recommends one of
	/// them, or keeping the current one. It never changes the destination: the advice is shown on the call, a confident change is
	/// written as a System call note and pushed to the call's dispatcher and units, and a person applies or dismisses it. Call content
	/// goes only to the operator's self-hosted model, and departments enrolled in Advanced Data Protection are skipped.
	/// </summary>
	public interface IAiDestinationAdvisorService
	{
		/// <summary>
		/// The host switch, the operator's model and the department's entitlement (Ai.DestinationAdvice with the Enhanced AI add-on, or an
		/// operator-listed self-hosted department) allow advice, and the department is not enrolled in Advanced Data Protection. The
		/// answer is reused for <c>AiDestinationConfig.AvailabilityCacheSeconds</c>, so pages and triggers do not call the Billing API each time.
		/// </summary>
		Task<bool> IsAvailableAsync(int departmentId);

		/// <summary>
		/// Queues a review of the call's destination: one waiting review per call, so later triggers fold into it (debounced by
		/// <c>AiDestinationConfig.DebounceSeconds</c>). A cheap no-op when the feature is off for the department, the call is not open,
		/// or the call has neither a destination POI nor a matching destination rule. Never throws.
		/// </summary>
		Task RequestReviewAsync(int departmentId, int callId, AiDestinationReviewTriggers trigger, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>A destination's availability changed: queues a review for each open call going there. Never throws.</summary>
		Task RequestReviewsForDestinationAsync(int departmentId, int poiId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// "Ask Enhanced AI": advises now, bounded by <c>AiDestinationConfig.OnDemandTimeoutSeconds</c>. Works for any open call with a
		/// location. Returns the open advice without asking the model again when nothing it reads has changed.
		/// </summary>
		Task<AiDestinationAdviceOutcome> AdviseNowAsync(int departmentId, int callId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Worker 81: claims and processes the reviews that are due, then deletes old processed ones.</summary>
		Task<AiDestinationReviewSweepResult> ProcessDueReviewsAsync(DateTime nowUtc, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>The call's open advice, or null.</summary>
		Task<AiDestinationAdvice> GetOpenAdviceAsync(int departmentId, int callId);

		/// <summary>One of the department's advice rows, or null.</summary>
		Task<AiDestinationAdvice> GetAdviceByIdAsync(int departmentId, string adviceId);

		/// <summary>The names to show with the advice: current and recommended destinations, required capabilities.</summary>
		Task<AiDestinationAdviceDetails> DescribeAsync(int departmentId, AiDestinationAdvice advice);

		/// <summary>
		/// Sets the call's destination to the recommended POI through the normal call save (the free destination is cleared), writes a
		/// System call note naming who applied it, and marks the advice Applied. The caller checks the user may edit the call.
		/// </summary>
		Task<AiDestinationAdviceOutcome> ApplyAsync(int departmentId, string adviceId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Marks the advice Dismissed; a dismissed change is noted on the call with the optional reason.</summary>
		Task<AiDestinationAdviceOutcome> DismissAsync(int departmentId, string adviceId, string userId, string reason, CancellationToken cancellationToken = default(CancellationToken));
	}
}
