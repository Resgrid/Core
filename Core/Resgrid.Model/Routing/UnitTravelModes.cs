using System;
using System.Globalization;

namespace Resgrid.Model
{
	/// <summary>
	/// How a unit gets to an incident, set on its unit type and optionally overridden per unit (M0314). Persisted as an
	/// int on <c>UnitTypes.TravelMode</c> / <c>Units.TravelMode</c>, so values are never renumbered.
	/// </summary>
	public enum UnitTravelModes
	{
		/// <summary>Roads at driving speeds (ambulances, engines, cars). The default.</summary>
		Driving = 0,

		/// <summary>Cycle routes at cycling speed (bike teams).</summary>
		Cycling = 1,

		/// <summary>Footpaths at walking speed (foot patrols, BLS teams at events).</summary>
		Walking = 2,

		/// <summary>Straight line at the unit's air speed (helicopters, drones); no routing.</summary>
		Air = 3
	}

	/// <summary>
	/// A unit's effective travel mode and speed: its own values win, else its unit type's. Dispatch ranking (run cards and
	/// the nearest-units board) measures every unit with this, so a foot team two minutes' walk away ranks ahead of an
	/// ambulance that has to drive around.
	/// </summary>
	public sealed class UnitTravelProfile
	{
		/// <summary>The lowest and highest speed a department may set, in km/h.</summary>
		public const double MinimumSpeedKmh = 1;
		public const double MaximumSpeedKmh = 1000;

		/// <summary>Driving keeps the blended urban/suburban estimate used before travel modes (40 km/h).</summary>
		public const double DefaultDrivingSpeedKmh = 40;
		public const double DefaultCyclingSpeedKmh = 15;
		public const double DefaultWalkingSpeedKmh = 5;
		public const double DefaultAirSpeedKmh = 180;

		public static readonly UnitTravelProfile Default = new UnitTravelProfile(UnitTravelModes.Driving, null);

		public UnitTravelProfile(UnitTravelModes mode, double? configuredSpeedKmh)
		{
			Mode = Enum.IsDefined(typeof(UnitTravelModes), mode) ? mode : UnitTravelModes.Driving;
			ConfiguredSpeedKmh = Normalize(configuredSpeedKmh);
		}

		public UnitTravelModes Mode { get; }

		/// <summary>The speed the department set for the unit or its type; null uses the mode's default.</summary>
		public double? ConfiguredSpeedKmh { get; }

		/// <summary>The speed estimates (and air travel) are worked out with.</summary>
		public double SpeedKmh => ConfiguredSpeedKmh ?? DefaultSpeedKmh(Mode);

		public double SpeedMetersPerSecond => SpeedKmh * 1000d / 3600d;

		/// <summary>Air travel is a straight line at <see cref="SpeedKmh"/>; it never asks the routing provider.</summary>
		public bool IsAir => Mode == UnitTravelModes.Air;

		/// <summary>
		/// The routing profile a routed lookup uses. Driving returns null: the caller picks driving, traffic or truck from the
		/// vehicle size and install config, as before travel modes. Air returns null too, but is never routed.
		/// </summary>
		public Resgrid.Model.RoutingProfile? RoutingProfile
		{
			get
			{
				switch (Mode)
				{
					case UnitTravelModes.Cycling:
						return Resgrid.Model.RoutingProfile.Cycling;
					case UnitTravelModes.Walking:
						return Resgrid.Model.RoutingProfile.Walking;
					default:
						return null;
				}
			}
		}

		/// <summary>
		/// True when a routed walking or cycling time should be taken as routed distance over the configured speed rather than
		/// the provider's own duration: providers walk and cycle at fixed speeds, and on paths the time is distance over speed.
		/// </summary>
		public bool UsesConfiguredSpeedOnRoute => ConfiguredSpeedKmh.HasValue && (Mode == UnitTravelModes.Walking || Mode == UnitTravelModes.Cycling);

		/// <summary>
		/// Travel time from a straight-line distance. Ground modes add the road factor (paths and roads run about a third longer
		/// than the straight line); air travel is the straight line itself.
		/// </summary>
		public double EstimateSeconds(double straightLineMeters)
		{
			if (straightLineMeters <= 0)
				return 0;

			var meters = IsAir ? straightLineMeters : straightLineMeters * UnitResponseOrigin.EstimatedRoadDistanceFactor;
			return Math.Round(meters / SpeedMetersPerSecond);
		}

		/// <summary>A stable string for grouping lookups: two units share a routed time only when their travel matches.</summary>
		public string Key()
		{
			return ((int)Mode).ToString(CultureInfo.InvariantCulture) + ":" +
				(ConfiguredSpeedKmh.HasValue ? ConfiguredSpeedKmh.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty);
		}

		/// <summary>
		/// The unit's own mode wins, else its type's; the speed follows the same rule, except that a unit that changes its mode
		/// away from the type's does not inherit the type's speed (a walking speed on a driving type would make no sense).
		/// </summary>
		public static UnitTravelProfile Resolve(Unit unit, UnitType unitType)
		{
			var typeMode = ToMode(unitType?.TravelMode);
			var mode = unit?.TravelMode.HasValue == true ? ToMode(unit.TravelMode) : typeMode;

			double? speed = unit?.TravelSpeedKmh;
			if (!speed.HasValue && mode == typeMode)
				speed = unitType?.TravelSpeedKmh;

			if (mode == UnitTravelModes.Driving && !Normalize(speed).HasValue)
				return Default;

			return new UnitTravelProfile(mode, speed);
		}

		public static double DefaultSpeedKmh(UnitTravelModes mode)
		{
			switch (mode)
			{
				case UnitTravelModes.Cycling:
					return DefaultCyclingSpeedKmh;
				case UnitTravelModes.Walking:
					return DefaultWalkingSpeedKmh;
				case UnitTravelModes.Air:
					return DefaultAirSpeedKmh;
				default:
					return DefaultDrivingSpeedKmh;
			}
		}

		public static UnitTravelModes ToMode(int? value)
		{
			return value.HasValue && Enum.IsDefined(typeof(UnitTravelModes), value.Value) ? (UnitTravelModes)value.Value : UnitTravelModes.Driving;
		}

		private static double? Normalize(double? speed)
		{
			if (!speed.HasValue || double.IsNaN(speed.Value) || double.IsInfinity(speed.Value) || speed.Value <= 0)
				return null;

			return Math.Min(MaximumSpeedKmh, Math.Max(MinimumSpeedKmh, speed.Value));
		}
	}
}
