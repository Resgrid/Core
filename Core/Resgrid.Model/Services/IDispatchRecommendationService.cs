using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The run card recommendation engine: matches a run card to a call, builds the
	/// dispatchable candidate pool (availability selections, staffing gate, rest
	/// period) and fills the card's requirements via the department's dispatch mode
	/// (station geofence cascade or closest unit). The single seam used by the web
	/// controllers, the v4 API and every automated call source — dispatch selection
	/// logic must not be duplicated outside it.
	/// </summary>
	public interface IDispatchRecommendationService
	{
		/// <summary>
		/// Computes recommendations without touching a call. MatchedRunCardId == null
		/// means no card applies and the caller should change nothing.
		/// </summary>
		Task<DispatchRecommendationResult> GetRecommendationAsync(DispatchRecommendationRequest request, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Computes recommendations for the call's priority/type/location and adds the
		/// recommended units/personnel to the call's dispatch collections (additive —
		/// existing dispatches are never removed or duplicated). Sets ActiveRunCardId
		/// and advances AlarmLevel when a card matched. The caller is responsible for
		/// saving the call and broadcasting. When <paramref name="onlyWhenAutoDispatch"/>
		/// is true the dispatch collections are only mutated if the resolved
		/// auto-dispatch decision (department default + card override) is on — use this
		/// from call-creation sites; explicit escalation passes false to always apply.
		/// </summary>
		Task<DispatchRecommendationResult> EnrichCallForDispatchAsync(Call call, int targetAlarmLevel, bool onlyWhenAutoDispatch = false, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// As above, with the outcome node a dispatcher saw on the New Call preview (conditional run cards). The posted key is
		/// used only when it is an outcome of the matched card's level; anything else walks the graph fresh.
		/// </summary>
		Task<DispatchRecommendationResult> EnrichCallForDispatchAsync(Call call, int targetAlarmLevel, bool onlyWhenAutoDispatch, string forcedLeafNodeKey,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Persists the RunCardActivation audit row and raises the workflow events
		/// (RunCardActivated, DispatchShortfallDetected, StationCoverageGapDetected)
		/// for an applied recommendation. Call AFTER the call is saved so CallId is
		/// assigned. No-op when the result matched no card.
		/// </summary>
		Task RecordActivationAsync(Call call, DispatchRecommendationResult result, string createdByUserId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Conditional run cards: pins the level's decision on the saved call (RunCardDecisions) and records the outstanding
		/// requirements it left. RecordActivationAsync does this itself; call it directly where a recommendation was applied
		/// without an activation (the dispatcher accepted the New Call preview). No-op for a fixed level, an already pinned
		/// result, or a call not yet saved; never throws.
		/// </summary>
		Task RecordDecisionAsync(Call call, DispatchRecommendationResult result, string createdByUserId, RunCardDecisionSources source,
			CancellationToken cancellationToken = default(CancellationToken));
	}
}
