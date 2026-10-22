using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Required resources a call still awaits (conditional run cards). A row marked "keep outstanding" that could not be met
	/// as configured becomes a CallOutstandingRequirement when the call is saved. It clears only when a matching resource
	/// is dispatched to the call, a person dismisses it, or a person closes the call; never on a timer. Reads reconcile
	/// against the call's current dispatches first, so every dispatch path resolves requirements without being hooked.
	/// </summary>
	public interface IOutstandingRequirementsService
	{
		/// <summary>
		/// Records the outstanding requirements a saved call's recommendation produced (substituted or unfilled rows marked
		/// "keep outstanding"), skipping ones the call already covers. No-op for a fixed card or a call that is not active.
		/// </summary>
		Task<List<CallOutstandingRequirement>> CreateFromRecommendationAsync(Call call, DispatchRecommendationResult result, string userId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>The call's requirements after reconciling with its dispatches; open ones only unless asked.</summary>
		Task<List<CallOutstandingRequirement>> GetForCallAsync(int departmentId, int callId, bool includeResolved = false,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>True when the call still awaits a resource (used by automatic call close, which must leave such a call open).</summary>
		Task<bool> HasOpenRequirementsAsync(int departmentId, int callId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Every active call of the department still awaiting a resource, oldest and highest priority first. With
		/// <paramref name="visibleCallIds"/> (group-scoped dispatch), only those calls; null means every call.
		/// </summary>
		Task<List<AwaitingResourcesCall>> GetAwaitingCallsAsync(int departmentId, ICollection<int> visibleCallIds = null,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// How many calls still await a resource (reconciled, without the available-unit lookups), for badges that poll. With
		/// <paramref name="visibleCallIds"/>, only those calls are counted; null means every call.
		/// </summary>
		Task<int> GetAwaitingCallCountAsync(int departmentId, ICollection<int> visibleCallIds = null,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>A person dismisses an open requirement (with an optional note).</summary>
		Task<bool> DismissAsync(int departmentId, int callOutstandingRequirementId, string userId, string note,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Called after a unit's status is saved: when the unit is now dispatchable and matches an open requirement, alerts the
		/// department's dispatchers, and dispatches it automatically where the department or the card turned that on.
		/// Never throws; a failure here must not affect the status save.
		/// </summary>
		Task OnUnitStateSavedAsync(int departmentId, UnitState saved, UnitState previous, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// One-step dispatch from an alert or the awaiting list: adds the unit to the call (additive, notifying only the unit),
		/// which resolves the requirement it matches.
		/// </summary>
		Task<AwaitingDispatchResult> DispatchUnitToCallAsync(int departmentId, int callId, int unitId, string userId,
			CancellationToken cancellationToken = default(CancellationToken));
	}

	/// <summary>A call awaiting resources, for the awaiting-resources views and alerts.</summary>
	public class AwaitingResourcesCall
	{
		public int CallId { get; set; }

		public string CallNumber { get; set; }

		public string CallName { get; set; }

		public int Priority { get; set; }

		public string PriorityName { get; set; }

		public string PriorityColor { get; set; }

		public string Address { get; set; }

		public System.DateTime LoggedOn { get; set; }

		public List<AwaitingResourceRequirement> Requirements { get; set; } = new List<AwaitingResourceRequirement>();
	}

	public class AwaitingResourceRequirement
	{
		public int CallOutstandingRequirementId { get; set; }

		public string Label { get; set; }

		public int RequiredCount { get; set; }

		public int ResolvedCount { get; set; }

		public string SentInstead { get; set; }

		public string ResponseCode { get; set; }

		/// <summary>The code's catalog id and colour, when the department still has the code.</summary>
		public int? ResponseCodeId { get; set; }

		public string ResponseCodeColor { get; set; }

		public System.DateTime CreatedOn { get; set; }

		/// <summary>Units currently available that would satisfy the requirement (ids), nearest first when known.</summary>
		public List<AwaitingCandidateUnit> AvailableUnits { get; set; } = new List<AwaitingCandidateUnit>();
	}

	public class AwaitingCandidateUnit
	{
		public int UnitId { get; set; }

		public string UnitName { get; set; }

		public string UnitTypeName { get; set; }

		public double? ResponseSeconds { get; set; }
	}

	public class AwaitingDispatchResult
	{
		public bool Success { get; set; }

		public string Message { get; set; }

		public int ResolvedRequirements { get; set; }
	}
}
