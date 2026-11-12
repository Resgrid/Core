using System;
using System.Globalization;

namespace Resgrid.Model
{
	/// <summary>One road closure as a routed trip sees it (built from a <see cref="RoadClosure"/> for the trip's travel mode).</summary>
	public class RoutingClosure
	{
		public string ClosureId { get; set; }

		public string Name { get; set; }

		public RoadClosureSeverities Severity { get; set; }

		/// <summary>Speed inside, as a percentage of normal (5–95).</summary>
		public int SpeedPercent { get; set; } = RoadClosure.DefaultSpeedPercent;

		/// <summary>Seconds charged for crossing a full closure the provider couldn't route around.</summary>
		public int DetourSeconds { get; set; } = RoadClosure.DefaultDetourMinutes * 60;

		public RoadClosureShape Shape { get; set; }

		/// <summary>Changes whenever the closure is edited, so cached routes measured against the old version are not reused.</summary>
		public string VersionKey { get; set; }

		public static RoutingClosure From(RoadClosure closure, RoadClosureShape shape)
		{
			if (closure == null || shape == null)
				return null;

			return new RoutingClosure
			{
				ClosureId = closure.RoadClosureId,
				Name = closure.Name,
				Severity = closure.SeverityValue,
				SpeedPercent = Math.Max(RoadClosure.MinimumSpeedPercent, Math.Min(RoadClosure.MaximumSpeedPercent, closure.SpeedPercent)),
				DetourSeconds = Math.Max(0, Math.Min(RoadClosure.MaximumDetourMinutes, closure.DetourMinutes)) * 60,
				Shape = shape,
				VersionKey = closure.RoadClosureId + "@" + closure.UpdatedOn.Ticks.ToString(CultureInfo.InvariantCulture)
			};
		}
	}

	/// <summary>What a road closure did to a route.</summary>
	public enum RouteClosureEffects
	{
		/// <summary>A full closure near the route that the provider was told to go around and the route stays out of.</summary>
		Avoided = 0,

		/// <summary>The route runs through it at reduced speed (a partial closure, or a full one the trip starts or ends in).</summary>
		Slowed = 1,

		/// <summary>The route still crosses a full closure (the provider can't avoid areas): a detour time was added.</summary>
		Crossed = 2
	}

	public class RouteClosureEffect
	{
		public string ClosureId { get; set; }

		public string Name { get; set; }

		public RouteClosureEffects Effect { get; set; }

		public int DelaySeconds { get; set; }
	}
}
