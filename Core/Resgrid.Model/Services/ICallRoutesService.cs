using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>A route plan a call can follow, with where it starts and ends (M0322).</summary>
	public class CallRoutePlan
	{
		public RoutePlan Plan { get; set; }

		public RoutePlanEndpoints Endpoints { get; set; }
	}

	/// <summary>
	/// Call start/destination locations and pre-defined routes (M0322): which route plans a call can follow, the defaults a call takes from
	/// its location POI and its route, and starting the route for the unit working the call.
	/// </summary>
	public interface ICallRoutesService
	{
		/// <summary>
		/// The department's plans a call can follow: active, not deleted, with a start and an end (station, explicit point or first/last
		/// stop; see <see cref="RoutePlanEndpoints.IsComplete"/>), by name.
		/// </summary>
		Task<List<CallRoutePlan>> GetCallRoutePlansAsync(int departmentId);

		/// <summary>The plan when a call in the department can follow it now, else null.</summary>
		Task<CallRoutePlan> GetEligibleRoutePlanAsync(int departmentId, string routePlanId, int? unitId = null);

		/// <summary>True when the plan is one a call can follow (active, not deleted, in the department).</summary>
		bool IsSelectable(RoutePlan plan, int departmentId);

		/// <summary>The department's POI by id (any type), else null.</summary>
		Task<Poi> GetLocationPoiAsync(int departmentId, int poiId);

		/// <summary>
		/// Keeps one destination (a POI clears the free one), fills a blank address/point from the location POI and, for a call with a
		/// route, a blank location from the route start and a missing destination from the route end. Never overwrites what is set.
		/// </summary>
		Task ApplyDefaultsAsync(Call call);

		/// <summary>
		/// Starts the call's route for the unit, or returns the unit's in-progress or paused run of this call. A run the unit still has
		/// for another call that is closed or deleted is cancelled first. Throws <see cref="RouteInProgressException"/> when the unit runs
		/// another route, and <see cref="System.InvalidOperationException"/> when the call has no route or its plan can no longer be followed.
		/// </summary>
		Task<RouteInstance> StartCallRouteAsync(Call call, int unitId, string userId, CancellationToken cancellationToken = default);
	}
}
