using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>Who is travelling, for a dispatch travel-time lookup (<c>IGeoService.GetTravelEstimateAsync</c>).</summary>
	public class TravelLookup
	{
		/// <summary>The department whose road closures apply; 0 applies none.</summary>
		public int DepartmentId { get; set; }

		/// <summary>How the unit travels; null is driving at the default speed.</summary>
		public UnitTravelProfile Travel { get; set; }

		/// <summary>The vehicle's size and weight limits (driving only).</summary>
		public VehicleProfile Vehicle { get; set; }

		/// <summary>A stable string for sharing one lookup between co-located units that travel the same way.</summary>
		public string Key()
		{
			return (Travel ?? UnitTravelProfile.Default).Key() + "|" + (Vehicle?.Key() ?? string.Empty);
		}
	}

	/// <summary>A travel time for dispatch ranking.</summary>
	public class TravelEstimate
	{
		public static TravelEstimate None => new TravelEstimate { Seconds = -1, Source = EtaSources.None };

		/// <summary>Travel seconds; -1 when only a straight-line estimate could answer (callers keep their own and label it).</summary>
		public double Seconds { get; set; } = -1;

		/// <summary><see cref="EtaSources.Road"/> for a routed time, <see cref="EtaSources.Direct"/> for air travel.</summary>
		public EtaSources Source { get; set; }

		/// <summary>Seconds active road closures added (included in <see cref="Seconds"/>).</summary>
		public int ClosureDelaySeconds { get; set; }

		/// <summary>The road closures near the route and what each did to it.</summary>
		public List<RouteClosureEffect> Closures { get; set; } = new List<RouteClosureEffect>();

		public bool HasValue => Seconds >= 0;
	}
}
