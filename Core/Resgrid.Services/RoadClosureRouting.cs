using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using Resgrid.Model;

namespace Resgrid.Services
{
	/// <summary>
	/// How a routed trip meets road closures (M0314). Before the provider is asked, <see cref="Plan"/> sorts the trip's
	/// closures: full closures near the route are handed to the provider to avoid as far as it can (Valhalla takes areas and
	/// road points, Mapbox road points on driving profiles, Google and OSRM nothing); a full closure the trip starts or ends
	/// in, and every partial closure, is driven through at its reduced speed. After the route comes back,
	/// <see cref="Apply"/> measures the route line against each closure and adds the slowdown, plus the closure's detour time
	/// for a full closure the route still crosses, so every provider's time reflects the closures.
	/// </summary>
	public static class RoadClosureRouting
	{
		/// <summary>Closures farther than this from the straight line's box are left out (scaled up for long trips).</summary>
		public const double MinimumCorridorMarginMeters = 2000;
		public const double MaximumCorridorMarginMeters = 20000;

		/// <summary>Valhalla takes many exclusion points; this keeps the request reasonable.</summary>
		public const int MaxValhallaExcludePoints = 200;

		/// <summary>Re-routes allowed to push a route out of a full closure by excluding where it went in (point-exclusion providers).</summary>
		public const int MaxAvoidanceRetries = 2;

		public sealed class ClosurePlan
		{
			public List<RoutingClosure> Avoid { get; } = new List<RoutingClosure>();

			public List<RoutingClosure> Slow { get; } = new List<RoutingClosure>();

			public bool HasClosures => Avoid.Count > 0 || Slow.Count > 0;

			/// <summary>The request the provider is sent: the original plus what it is told to avoid.</summary>
			public RouteRequest ProviderRequest { get; set; }

			/// <summary>How many more road points the provider can be told to avoid (0 when it can't).</summary>
			public int PointBudget { get; set; }

			/// <summary>Part of the route cache key: which closures, in which version, in which role.</summary>
			public string Key { get; set; } = string.Empty;
		}

		public static ClosurePlan Plan(RouteRequest request, RoutingCapabilities capabilities, bool isEstimate)
		{
			var plan = new ClosurePlan { ProviderRequest = request };

			if (isEstimate || request?.Closures == null || request.Closures.Count == 0 || request.Waypoints == null || request.Waypoints.Count < 2)
				return plan;

			var points = request.Waypoints.Select(w => new GeoMath.GeoPoint(w.Latitude, w.Longitude)).ToList();
			var low = new GeoMath.GeoPoint(points.Min(p => p.Latitude), points.Min(p => p.Longitude));
			var high = new GeoMath.GeoPoint(points.Max(p => p.Latitude), points.Max(p => p.Longitude));
			var span = GeoMath.HaversineMeters(low.Latitude, low.Longitude, high.Latitude, high.Longitude);
			var margin = Math.Max(MinimumCorridorMarginMeters, Math.Min(MaximumCorridorMarginMeters, span * 0.3));

			foreach (var closure in request.Closures.Where(c => c?.Shape != null && !c.Shape.IsEmpty))
			{
				if (!closure.Shape.IntersectsCorridor(low, high, margin))
					continue;

				// A trip that starts or ends inside a full closure has to go in (an incident on a marathon course): it is slowed
				// through, not refused.
				var touchesStop = points.Any(p => closure.Shape.Contains(p.Latitude, p.Longitude));

				if (closure.Severity == RoadClosureSeverities.Full && !touchesStop)
					plan.Avoid.Add(closure);
				else
					plan.Slow.Add(closure);
			}

			if (!plan.HasClosures)
				return plan;

			var keys = plan.Avoid.Select(c => "a:" + c.VersionKey).Concat(plan.Slow.Select(c => "s:" + c.VersionKey)).OrderBy(k => k, StringComparer.Ordinal);
			plan.Key = "|rc:" + string.Join(",", keys);

			if (plan.Avoid.Count == 0)
				return plan;

			var providerRequest = Clone(request);
			plan.ProviderRequest = providerRequest;

			if (capabilities?.ExcludeAreas == true)
			{
				foreach (var polygon in plan.Avoid.SelectMany(c => c.Shape.Polygons))
					providerRequest.ExcludeAreas.Add(polygon.Select(p => new RoutingWaypoint(p.Latitude, p.Longitude)).ToList());
			}

			var pointsAllowed = capabilities != null && capabilities.MaxExcludePoints > 0 &&
				(!capabilities.ExcludePointsDrivingOnly || IsDrivingProfile(request.Profile));

			if (pointsAllowed)
			{
				var budget = capabilities.MaxExcludePoints;
				var lineClosures = plan.Avoid.Where(c => c.Shape.Lines.Count > 0).ToList();

				foreach (var closure in lineClosures)
				{
					var share = Math.Max(1, budget / Math.Max(1, lineClosures.Count));
					foreach (var point in closure.Shape.LineExclusionPoints(share))
						providerRequest.ExcludePoints.Add(new RoutingWaypoint(point.Latitude, point.Longitude));
				}

				if (providerRequest.ExcludePoints.Count > budget)
					providerRequest.ExcludePoints = providerRequest.ExcludePoints.Take(budget).ToList();

				plan.PointBudget = Math.Max(0, budget - providerRequest.ExcludePoints.Count);
			}

			return plan;
		}

		/// <summary>
		/// Where a route still runs into full closures it should have avoided: the points it goes in at, for a provider that
		/// excludes by point to re-route around. Empty when the route is clear or the provider can't take more points.
		/// </summary>
		public static List<RoutingWaypoint> EntryPointsToExclude(RouteResult route, ClosurePlan plan)
		{
			var points = new List<RoutingWaypoint>();
			if (route == null || plan == null || plan.Avoid.Count == 0 || plan.PointBudget <= 0)
				return points;

			var line = ParseLine(route.GeometryGeoJson);
			if (line.Count < 2)
				return points;

			foreach (var closure in plan.Avoid)
			{
				foreach (var entry in closure.Shape.Measure(line).EntryPoints)
				{
					if (points.Count >= plan.PointBudget)
						return points;

					points.Add(new RoutingWaypoint(entry.Latitude, entry.Longitude));
				}
			}

			return points;
		}

		/// <summary>A copy of the plan's provider request that also avoids <paramref name="extraPoints"/>.</summary>
		public static RouteRequest WithExtraExcludePoints(ClosurePlan plan, List<RoutingWaypoint> extraPoints)
		{
			var next = Clone(plan.ProviderRequest);
			next.ExcludeAreas = plan.ProviderRequest.ExcludeAreas.ToList();
			next.ExcludePoints = plan.ProviderRequest.ExcludePoints.Concat(extraPoints).ToList();
			plan.ProviderRequest = next;
			plan.PointBudget = Math.Max(0, plan.PointBudget - extraPoints.Count);
			return next;
		}

		/// <summary>
		/// Adds the closures' time to the route and lists what each did. Time inside a closure is the route's average pace over
		/// the metres inside, slowed to the closure's speed; a full closure the route still crosses also adds its detour time.
		/// </summary>
		public static void Apply(RouteResult route, ClosurePlan plan)
		{
			if (route == null || plan == null || !plan.HasClosures)
				return;

			route.Closures = new List<RouteClosureEffect>();
			var line = ParseLine(route.GeometryGeoJson);
			var distance = route.DistanceMeters;
			var duration = route.DurationSeconds;
			double total = 0;

			double TimeInside(double meters) => distance > 0 && duration > 0 ? duration * Math.Min(1, meters / distance) : 0;

			double Slowdown(RoutingClosure closure, double meters)
			{
				var percent = Math.Max(RoadClosure.MinimumSpeedPercent, Math.Min(RoadClosure.MaximumSpeedPercent, closure.SpeedPercent));
				return TimeInside(meters) * (100d / percent - 1d);
			}

			foreach (var closure in plan.Avoid)
			{
				var measure = line.Count >= 2 ? closure.Shape.Measure(line) : new RoadClosureRouteMeasure();
				if (measure.Crosses)
				{
					// The router drove straight through; the unit will go round. The detour time stands in for that.
					var delay = closure.DetourSeconds;
					total += delay;
					route.Closures.Add(new RouteClosureEffect { ClosureId = closure.ClosureId, Name = closure.Name, Effect = RouteClosureEffects.Crossed, DelaySeconds = delay });
				}
				else
				{
					route.Closures.Add(new RouteClosureEffect { ClosureId = closure.ClosureId, Name = closure.Name, Effect = RouteClosureEffects.Avoided });
				}
			}

			foreach (var closure in plan.Slow)
			{
				var measure = line.Count >= 2 ? closure.Shape.Measure(line) : new RoadClosureRouteMeasure();
				if (!measure.Crosses)
					continue;

				var delay = Slowdown(closure, measure.MetersInside);
				total += delay;
				route.Closures.Add(new RouteClosureEffect { ClosureId = closure.ClosureId, Name = closure.Name, Effect = RouteClosureEffects.Slowed, DelaySeconds = (int)Math.Round(delay) });
			}

			route.ClosureDelaySeconds = (int)Math.Round(total);
			route.DurationSeconds += route.ClosureDelaySeconds;
		}

		/// <summary>True when the route still runs through a full closure the provider could not avoid: its directions lead into it.</summary>
		public static bool CrossesFullClosure(RouteResult route)
		{
			return route?.Closures != null && route.Closures.Any(c => c != null && c.Effect == RouteClosureEffects.Crossed);
		}

		/// <summary>
		/// The route's closure delay for a trip over the same path at another pace, <paramref name="baseSeconds"/> without
		/// closures (a team at its own walking or cycling speed). A slowdown is time spent inside a closure, so it scales with
		/// the pace; a crossed full closure's detour time is a fixed allowance and does not.
		/// </summary>
		public static int DelayAtPace(RouteResult route, double baseSeconds)
		{
			if (route == null || route.ClosureDelaySeconds <= 0)
				return 0;

			var effects = route.Closures ?? new List<RouteClosureEffect>();
			if (!effects.Any(c => c != null && c.Effect == RouteClosureEffects.Slowed))
				return route.ClosureDelaySeconds;

			var fixedSeconds = Math.Min(route.ClosureDelaySeconds, effects.Where(c => c != null && c.Effect == RouteClosureEffects.Crossed).Sum(c => Math.Max(0, c.DelaySeconds)));
			var slowedSeconds = route.ClosureDelaySeconds - fixedSeconds;
			var providerBaseSeconds = route.DurationSeconds - route.ClosureDelaySeconds;
			if (slowedSeconds <= 0 || providerBaseSeconds <= 0 || baseSeconds <= 0)
				return route.ClosureDelaySeconds;

			return (int)Math.Round(fixedSeconds + slowedSeconds * baseSeconds / providerBaseSeconds);
		}

		/// <summary>The [lon, lat] positions of a GeoJSON LineString string; empty when missing or unreadable.</summary>
		public static List<GeoMath.GeoPoint> ParseLine(string geoJson)
		{
			var points = new List<GeoMath.GeoPoint>();
			if (string.IsNullOrWhiteSpace(geoJson))
				return points;

			try
			{
				using var document = JsonDocument.Parse(geoJson);
				var root = document.RootElement;
				if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("geometry", out var geometry))
					root = geometry;

				if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("coordinates", out var coordinates) || coordinates.ValueKind != JsonValueKind.Array)
					return points;

				foreach (var position in coordinates.EnumerateArray())
				{
					if (position.ValueKind != JsonValueKind.Array || position.GetArrayLength() < 2)
						continue;

					points.Add(new GeoMath.GeoPoint(position[1].GetDouble(), position[0].GetDouble()));
				}
			}
			catch (Exception)
			{
				points.Clear();
			}

			return points;
		}

		/// <summary>A stable part of the cache key for any extra points a re-route avoided.</summary>
		public static string ExcludePointsKey(RouteRequest request)
		{
			if (request?.ExcludePoints == null || request.ExcludePoints.Count == 0)
				return string.Empty;

			var builder = new StringBuilder("|xp:");
			foreach (var point in request.ExcludePoints)
				builder.Append(Math.Round(point.Latitude, 5).ToString(CultureInfo.InvariantCulture)).Append(',')
					.Append(Math.Round(point.Longitude, 5).ToString(CultureInfo.InvariantCulture)).Append(';');

			return builder.ToString();
		}

		private static bool IsDrivingProfile(RoutingProfile profile)
		{
			return profile == RoutingProfile.Driving || profile == RoutingProfile.DrivingTraffic || profile == RoutingProfile.Truck;
		}

		private static RouteRequest Clone(RouteRequest request)
		{
			return new RouteRequest
			{
				Waypoints = request.Waypoints,
				Profile = request.Profile,
				Vehicle = request.Vehicle,
				Avoid = request.Avoid,
				Steps = request.Steps,
				NavigationInstructions = request.NavigationInstructions,
				Language = request.Language,
				Units = request.Units,
				BypassCache = request.BypassCache,
				Closures = request.Closures,
				ExcludeAreas = (request.ExcludeAreas ?? new List<List<RoutingWaypoint>>()).ToList(),
				ExcludePoints = (request.ExcludePoints ?? new List<RoutingWaypoint>()).ToList()
			};
		}
	}
}
