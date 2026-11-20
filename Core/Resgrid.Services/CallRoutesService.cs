using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc />
	public class CallRoutesService : ICallRoutesService
	{
		private readonly IRouteService _routeService;
		private readonly IRoutePlanRoutingService _routePlanRoutingService;
		private readonly IMappingService _mappingService;
		// Lazy: the calls service reaches back here (it applies these defaults on save).
		private readonly Lazy<ICallsService> _callsService;

		public CallRoutesService(IRouteService routeService, IRoutePlanRoutingService routePlanRoutingService, IMappingService mappingService,
			Lazy<ICallsService> callsService)
		{
			_callsService = callsService;
			_routeService = routeService;
			_routePlanRoutingService = routePlanRoutingService;
			_mappingService = mappingService;
		}

		public bool IsSelectable(RoutePlan plan, int departmentId)
		{
			return plan != null && plan.DepartmentId == departmentId && !plan.IsDeleted && plan.RouteStatus == (int)RouteStatus.Active;
		}

		public async Task<List<CallRoutePlan>> GetCallRoutePlansAsync(int departmentId)
		{
			var plans = await _routeService.GetRoutePlansForDepartmentAsync(departmentId) ?? new List<RoutePlan>();
			var result = new List<CallRoutePlan>();

			foreach (var plan in plans.Where(p => IsSelectable(p, departmentId)).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
			{
				var endpoints = await ResolveAsync(plan, null);
				if (endpoints != null && endpoints.IsComplete)
					result.Add(new CallRoutePlan { Plan = plan, Endpoints = endpoints });
			}

			return result;
		}

		public async Task<CallRoutePlan> GetEligibleRoutePlanAsync(int departmentId, string routePlanId, int? unitId = null)
		{
			if (String.IsNullOrWhiteSpace(routePlanId))
				return null;

			var plan = await _routeService.GetRoutePlanByIdAsync(routePlanId.Trim());
			if (!IsSelectable(plan, departmentId))
				return null;

			var endpoints = await ResolveAsync(plan, unitId);
			return endpoints != null && endpoints.IsComplete ? new CallRoutePlan { Plan = plan, Endpoints = endpoints } : null;
		}

		public async Task<Poi> GetLocationPoiAsync(int departmentId, int poiId)
		{
			if (poiId <= 0)
				return null;

			var poi = await _mappingService.GetPOIByIdAsync(poiId);
			if (poi == null)
				return null;

			// A POI carries its type, not its department; the type says whose it is.
			var type = poi.Type ?? await _mappingService.GetTypeByIdAsync(poi.PoiTypeId);
			return type != null && type.DepartmentId == departmentId ? poi : null;
		}

		public async Task ApplyDefaultsAsync(Call call)
		{
			if (call == null)
				return;

			CallLocations.NormalizeDestination(call);

			try
			{
				if (call.LocationPoiId.HasValue && (String.IsNullOrWhiteSpace(call.Address) || !CallLocations.TryParsePoint(call.GeoLocationData, out _, out _)))
				{
					var poi = await GetLocationPoiAsync(call.DepartmentId, call.LocationPoiId.Value);
					CallLocations.ApplyLocationPoi(call, poi);
				}

				if (!String.IsNullOrWhiteSpace(call.RoutePlanId) && (!CallLocations.HasLocation(call) || !CallLocations.HasDestination(call)))
				{
					var plan = await _routeService.GetRoutePlanByIdAsync(call.RoutePlanId);
					if (plan != null && plan.DepartmentId == call.DepartmentId && !plan.IsDeleted)
						CallLocations.ApplyRouteEndpoints(call, await ResolveAsync(plan, null));
				}
			}
			catch (Exception ex)
			{
				// Defaults are a convenience; a lookup failure must never stop the call being saved.
				Logging.LogException(ex, $"Applying location and route defaults to call {call.CallId}");
			}
		}

		public async Task<RouteInstance> StartCallRouteAsync(Call call, int unitId, string userId, CancellationToken cancellationToken = default)
		{
			if (call == null || String.IsNullOrWhiteSpace(call.RoutePlanId))
				throw new InvalidOperationException("The call has no route.");

			// Only an open call (active or pending, the rule IsCallOverAsync applies to a leftover run) gets a run: one started for a
			// closed call would stay in progress, holding the unit, until its next call's start cancelled it.
			if (!IsOpen(call))
				throw new InvalidOperationException("The call is closed.");

			// The unit's run of this call is resumed by the client, never started twice.
			var running = await _routeService.GetActiveOrPausedInstanceForUnitAsync(call.DepartmentId, unitId);
			if (running != null && running.CallId == call.CallId)
				return running;

			var eligible = await GetEligibleRoutePlanAsync(call.DepartmentId, call.RoutePlanId, unitId);
			if (eligible == null)
				throw new InvalidOperationException("The call's route can no longer be followed.");

			// A run left over from another call that is no longer open is cancelled rather than blocking the unit's next call.
			if (running != null && running.CallId.HasValue && await IsCallOverAsync(running.CallId.Value, call.DepartmentId))
			{
				await _routeService.CancelRouteAsync(running.RouteInstanceId, userId, "Call closed", cancellationToken);
				running = null;
			}

			if (running != null)
				throw new RouteInProgressException(running.RouteInstanceId, running.Status == (int)RouteInstanceStatus.Paused);

			// Planned ETAs come from the stored route, so bring it up to date first (best effort).
			try
			{
				await _routePlanRoutingService.GetCurrentRouteAsync(eligible.Plan.RoutePlanId, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Refreshing route plan {eligible.Plan.RoutePlanId} before starting it for call {call.CallId}");
			}

			return await _routeService.StartRouteForCallAsync(eligible.Plan.RoutePlanId, unitId, call.CallId, userId, cancellationToken);
		}

		/// <summary>True when the call is closed, deleted or gone (a pending call is still open).</summary>
		private async Task<bool> IsCallOverAsync(int callId, int departmentId)
		{
			var other = await _callsService.Value.GetCallByIdAsync(callId);
			return other == null || other.DepartmentId != departmentId || !IsOpen(other);
		}

		/// <summary>Not deleted, and active or pending.</summary>
		private static bool IsOpen(Call call)
		{
			return call != null && !call.IsDeleted && (call.State == (int)CallStates.Active || call.State == (int)CallStates.Pending);
		}

		private async Task<RoutePlanEndpoints> ResolveAsync(RoutePlan plan, int? unitId)
		{
			try
			{
				return await _routePlanRoutingService.ResolveEndpointsAsync(plan, unitId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Resolving the start and end of route plan {plan?.RoutePlanId}");
				return null;
			}
		}
	}
}
