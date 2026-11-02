using System;

namespace Resgrid.Model
{
	/// <summary>
	/// How a route is travelled. Persisted on route plans (<c>RoutePlans.RoutingProfile</c>), so values are never
	/// renumbered. Each routing provider maps these onto its own profile/costing names.
	/// </summary>
	public enum RoutingProfile
	{
		Driving = 0,
		/// <summary>Driving with live traffic where the provider has it (Mapbox <c>driving-traffic</c>, Google TRAFFIC_AWARE).</summary>
		DrivingTraffic = 1,
		/// <summary>Driving restricted by the vehicle's size and weight (<see cref="VehicleProfile"/>).</summary>
		Truck = 2,
		Walking = 3,
		Cycling = 4
	}

	/// <summary>Road types a route should avoid. Persisted on route plans as an int, so values are never changed.</summary>
	[Flags]
	public enum RouteAvoidOptions
	{
		None = 0,
		Tolls = 1,
		Highways = 2,
		Ferries = 4,
		Unpaved = 8
	}

	/// <summary>Distance units for spoken and written instructions.</summary>
	public enum RoutingUnits
	{
		Metric = 0,
		Imperial = 1
	}

	/// <summary>The routing provider names <c>MappingConfig.RoutingProvider</c> accepts.</summary>
	public static class RoutingProviderNames
	{
		public const string Valhalla = "valhalla";
		public const string Mapbox = "mapbox";
		public const string Google = "google";
		public const string Osrm = "osrm";
		/// <summary>Straight-line estimate (no external service); always available as the last fallback.</summary>
		public const string Estimate = "estimate";

		public static string Normalize(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
				return Estimate;

			var trimmed = name.Trim().ToLowerInvariant();

			switch (trimmed)
			{
				case Valhalla:
				case Mapbox:
				case Google:
				case Osrm:
				case Estimate:
					return trimmed;
				default:
					return Estimate;
			}
		}
	}

	/// <summary>Maps the legacy <c>RoutePlan.MapboxRouteProfile</c> strings to and from <see cref="RoutingProfile"/>.</summary>
	public static class RoutingProfileNames
	{
		public static RoutingProfile FromLegacyProfile(string profile)
		{
			if (string.IsNullOrWhiteSpace(profile))
				return RoutingProfile.Driving;

			switch (profile.Trim().ToLowerInvariant())
			{
				case "driving-traffic":
					return RoutingProfile.DrivingTraffic;
				case "walking":
					return RoutingProfile.Walking;
				case "cycling":
					return RoutingProfile.Cycling;
				default:
					return RoutingProfile.Driving;
			}
		}

		/// <summary>The value written to <c>MapboxRouteProfile</c> so builds that only read that column keep working.</summary>
		public static string ToLegacyProfile(RoutingProfile profile)
		{
			switch (profile)
			{
				case RoutingProfile.DrivingTraffic:
					return "driving-traffic";
				case RoutingProfile.Walking:
					return "walking";
				case RoutingProfile.Cycling:
					return "cycling";
				default:
					return "driving";
			}
		}
	}
}
