using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Releases (stands down) one resource, a unit or a member, from a call that stays open: a MUG stood down by the ambulance
	/// crew before it arrives goes back to radio availability and can take the next call. The record keeps the release: a unit's
	/// dispatch is marked released (<see cref="CallDispatchUnit.ReleasedOn"/>), a member gets a <see cref="CallPersonnelRelease"/>.
	/// Either way the resource stops being live on the call. Authorization is the caller's job.
	/// </summary>
	public interface ICallResourceReleaseService
	{
		/// <summary>
		/// Releases the unit from the call: marks its dispatch released, applies the department's release status unless the
		/// unit is already back in service, out of service, or working another open call, and (with <paramref name="notify"/>)
		/// sends the unit a cancellation notice for the call.
		/// </summary>
		Task<CallResourceReleaseResult> ReleaseUnitAsync(int departmentId, int callId, int unitId, string releasedByUserId, bool notify,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Releases the member from the call, however they were reached (by name, through a dispatched group or role, or by a
		/// status set against the call): records the release, applies the department's personnel release status unless the
		/// member is already available, unavailable, or working another open call, and (with <paramref name="notify"/>) sends
		/// them a cancellation notice for the call.
		/// </summary>
		Task<CallResourceReleaseResult> ReleasePersonnelAsync(int departmentId, int callId, string userId, string releasedByUserId, bool notify,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Puts released members back on the call (they were dispatched to it again by name): removes their releases. Returns the
		/// releases removed.
		/// </summary>
		Task<int> ClearPersonnelReleasesAsync(int callId, IEnumerable<string> userIds, CancellationToken cancellationToken = default(CancellationToken));
	}

	public enum CallResourceReleaseOutcome
	{
		Released = 0,

		/// <summary>No such call in the department (or it was deleted).</summary>
		CallNotFound = 1,

		/// <summary>The call is closed, pending, or scheduled and not yet sent: edit its dispatch list instead.</summary>
		CallNotLive = 2,

		/// <summary>The unit or member is not on the call.</summary>
		NotOnCall = 3,

		/// <summary>The unit or member was already released from the call and has not been dispatched to it again since.</summary>
		AlreadyReleased = 4
	}

	public class CallResourceReleaseResult
	{
		public CallResourceReleaseOutcome Outcome { get; set; }

		/// <summary>Units: the unit's dispatch to the call (released, for Released and AlreadyReleased).</summary>
		public CallDispatchUnit Dispatch { get; set; }

		/// <summary>Members: the release (for Released and AlreadyReleased).</summary>
		public CallPersonnelRelease PersonnelRelease { get; set; }

		/// <summary>True when the release wrote a new status; false when the unit or member kept the status they had.</summary>
		public bool StatusApplied { get; set; }

		/// <summary>When the resource was released, UTC (null unless Released or AlreadyReleased).</summary>
		public System.DateTime? ReleasedOn => Dispatch?.ReleasedOn ?? PersonnelRelease?.ReleasedOn;

		public static CallResourceReleaseResult Failed(CallResourceReleaseOutcome outcome, CallDispatchUnit dispatch = null, CallPersonnelRelease personnelRelease = null)
		{
			return new CallResourceReleaseResult { Outcome = outcome, Dispatch = dispatch, PersonnelRelease = personnelRelease };
		}
	}
}
