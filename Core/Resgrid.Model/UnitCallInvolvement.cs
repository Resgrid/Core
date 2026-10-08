using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Reporting;

namespace Resgrid.Model
{
	/// <summary>
	/// What a unit's current status says about its part in a call, for the call lifecycle automations: which units a
	/// call's release statuses leave alone, when a unit has finished with a call (closing the call when its last unit
	/// clears), and which call a status set without a destination belongs to. Custom statuses are read through their
	/// <see cref="CustomStateDetail.BaseType"/>; a custom status with no base type is <see cref="AvailabilityClass.Unknown"/>
	/// and drives none of these rules.
	/// </summary>
	public static class UnitCallInvolvement
	{
		/// <summary>
		/// The availability class of a raw unit state (built-in <see cref="UnitStateTypes"/> value or custom status detail id).
		/// </summary>
		public static AvailabilityClass Classify(int rawState, IReadOnlyDictionary<int, int> customBaseTypes)
		{
			if (rawState <= CallStatusLinkage.MaxBuiltInStatusId)
				return AvailabilityMatrix.ForUnitStateType(rawState);

			if (customBaseTypes == null || !customBaseTypes.TryGetValue(rawState, out var baseType))
				return AvailabilityClass.Unknown;

			return AvailabilityMatrix.ForCustomBaseType(baseType);
		}

		/// <summary>
		/// True when a call's release statuses (call closed, or the unit taken off the call) must leave the unit's status as it
		/// is: the unit is already back in service, or someone took it out of service, made it unavailable or put it on a
		/// break. Releasing it would put an out-of-service unit back in the dispatchable pool.
		/// </summary>
		public static bool KeepsStatusOnRelease(int rawState, IReadOnlyDictionary<int, int> customBaseTypes)
		{
			var availability = Classify(rawState, customBaseTypes);

			return availability is AvailabilityClass.Available or AvailabilityClass.Unavailable or AvailabilityClass.Delayed;
		}

		/// <summary>
		/// True when the unit is done with its call: back in service (available, in quarters, cleared) or out of service
		/// (unavailable, maintenance). Returning, transporting and the other committed statuses are still working the call.
		/// </summary>
		public static bool HasFinishedCall(int rawState, IReadOnlyDictionary<int, int> customBaseTypes)
		{
			var availability = Classify(rawState, customBaseTypes);

			return availability is AvailabilityClass.Available or AvailabilityClass.Unavailable;
		}

		/// <summary>
		/// The call a unit is working, which a new status sent without a destination is linked to (the same rules as the
		/// server's write-time attribution, <see cref="CallStatusAttribution"/>): the open call its latest status points at
		/// unless that status already cleared it, otherwise the one open call it is dispatched to (ignoring the call it just
		/// cleared). Null when there is none, or more than one dispatched call to choose from.
		/// </summary>
		/// <param name="latest">The unit's latest status, or null.</param>
		/// <param name="latestIsClearing">Whether that status ended the unit's involvement (<see cref="CallStatusLinkage.IsClearingUnitState"/>).</param>
		/// <param name="openCallIds">The department's open (active) call ids.</param>
		/// <param name="openDispatchedCallIds">The open calls the unit is dispatched to.</param>
		public static int? ResolveWorkingCallId(UnitState latest, bool latestIsClearing, ICollection<int> openCallIds, IEnumerable<int> openDispatchedCallIds)
		{
			var latestCallId = latest != null ? CallStatusLinkage.LinkedCallId(latest.DestinationId, latest.DestinationType) : null;

			// Untyped legacy destinations may be station ids, so only a destination typed as a call counts here.
			if (latest != null && latest.DestinationType != (int)DestinationEntityTypes.Call)
				latestCallId = null;

			if (latestCallId.HasValue && !latestIsClearing && openCallIds != null && openCallIds.Contains(latestCallId.Value))
				return latestCallId.Value;

			var dispatched = (openDispatchedCallIds ?? Enumerable.Empty<int>())
				.Where(x => openCallIds == null || openCallIds.Contains(x));

			return CallStatusAttribution.PickDispatchCall(dispatched, latestIsClearing ? latestCallId : null);
		}
	}
}
