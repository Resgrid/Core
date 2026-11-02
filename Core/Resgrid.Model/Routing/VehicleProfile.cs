using System.Globalization;

namespace Resgrid.Model
{
	/// <summary>
	/// A vehicle's physical limits, so routing avoids roads, bridges, tunnels and turns it cannot use. All values are
	/// metric and optional; a unit's effective profile is its own value, else its unit type's, else none (see
	/// <see cref="Resolve"/>). Valhalla's truck costing applies every field; Mapbox applies height, width and weight.
	/// </summary>
	public class VehicleProfile
	{
		public double? HeightMeters { get; set; }

		public double? WidthMeters { get; set; }

		/// <summary>Overall length, including any towed trailer.</summary>
		public double? LengthMeters { get; set; }

		public double? GrossWeightKg { get; set; }

		/// <summary>Load on the heaviest axle.</summary>
		public double? AxleLoadKg { get; set; }

		public int? AxleCount { get; set; }

		/// <summary>Carries hazardous materials (tunnel and route hazmat restrictions).</summary>
		public bool? Hazmat { get; set; }

		public bool HasAny =>
			HeightMeters.HasValue || WidthMeters.HasValue || LengthMeters.HasValue || GrossWeightKg.HasValue ||
			AxleLoadKg.HasValue || AxleCount.HasValue || Hazmat == true;

		/// <summary>A stable string for cache keys and matrix grouping; empty when there are no limits.</summary>
		public string Key()
		{
			if (!HasAny)
				return string.Empty;

			return string.Join("|",
				Format(HeightMeters), Format(WidthMeters), Format(LengthMeters), Format(GrossWeightKg), Format(AxleLoadKg),
				AxleCount?.ToString(CultureInfo.InvariantCulture) ?? "", Hazmat == true ? "h" : "");
		}

		/// <summary>The unit's own values win field by field; the unit type supplies the rest.</summary>
		public static VehicleProfile Resolve(Unit unit, UnitType unitType)
		{
			var profile = new VehicleProfile
			{
				HeightMeters = unit?.VehicleHeightMeters ?? unitType?.VehicleHeightMeters,
				WidthMeters = unit?.VehicleWidthMeters ?? unitType?.VehicleWidthMeters,
				LengthMeters = unit?.VehicleLengthMeters ?? unitType?.VehicleLengthMeters,
				GrossWeightKg = unit?.VehicleGrossWeightKg ?? unitType?.VehicleGrossWeightKg,
				AxleLoadKg = unit?.VehicleAxleLoadKg ?? unitType?.VehicleAxleLoadKg,
				AxleCount = unit?.VehicleAxleCount ?? unitType?.VehicleAxleCount,
				Hazmat = unit?.VehicleHazmat ?? unitType?.VehicleHazmat
			};

			return profile.HasAny ? profile : null;
		}

		private static string Format(double? value)
		{
			return value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "";
		}
	}
}
