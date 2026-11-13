using System;
using System.Collections.Generic;
using System.Globalization;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Units
{
	/// <summary>
	/// The "Travel mode" fields of the unit type and unit edit pages (M0314): how units get to an incident (driving,
	/// cycling, walking or air) and their average speed. Speeds are entered in km/h or mph and stored in km/h. On a unit a
	/// blank mode or speed uses the unit type's.
	/// </summary>
	public class TravelModeInput
	{
		public const double KilometersPerMile = 1.609344;

		/// <summary>True when <see cref="Speed"/> is km/h; false for mph.</summary>
		public bool Metric { get; set; }

		/// <summary>A <see cref="UnitTravelModes"/> number; "" on a unit uses the unit type's mode.</summary>
		public string Mode { get; set; }

		/// <summary>Average speed in the units chosen; blank uses the default for the mode.</summary>
		public double? Speed { get; set; }

		/// <summary>Unit pages only: each unit type's mode and speed, keyed by type name, shown as the inherited values.</summary>
		public Dictionary<string, TravelModeDefault> TypeDefaults { get; set; }

		public class TravelModeDefault
		{
			public int Mode { get; set; }

			public double? SpeedKmh { get; set; }
		}

		public static TravelModeInput From(UnitType type, bool metric)
		{
			return new TravelModeInput
			{
				Metric = metric,
				Mode = ((int)UnitTravelProfile.ToMode(type?.TravelMode)).ToString(CultureInfo.InvariantCulture),
				Speed = ToDisplaySpeed(type?.TravelSpeedKmh, metric)
			};
		}

		public static TravelModeInput From(Unit unit, bool metric)
		{
			return new TravelModeInput
			{
				Metric = metric,
				Mode = unit?.TravelMode.HasValue == true ? ((int)UnitTravelProfile.ToMode(unit.TravelMode)).ToString(CultureInfo.InvariantCulture) : string.Empty,
				Speed = ToDisplaySpeed(unit?.TravelSpeedKmh, metric)
			};
		}

		/// <summary>Each unit type's mode and speed, by type name, for the unit pages' inherited hints.</summary>
		public static Dictionary<string, TravelModeDefault> DefaultsFor(IEnumerable<UnitType> types)
		{
			var defaults = new Dictionary<string, TravelModeDefault>(StringComparer.Ordinal);

			foreach (var type in types ?? new List<UnitType>())
			{
				if (type == null || string.IsNullOrWhiteSpace(type.Type) || defaults.ContainsKey(type.Type))
					continue;

				defaults[type.Type] = new TravelModeDefault { Mode = (int)UnitTravelProfile.ToMode(type.TravelMode), SpeedKmh = type.TravelSpeedKmh };
			}

			return defaults;
		}

		/// <summary>The form's default speed units: mph for US English, km/h elsewhere.</summary>
		public static bool DefaultMetric() => VehicleSizeInput.DefaultMetric();

		/// <summary>Range failures as (field, low, high) in the entered units, for the page to localize.</summary>
		public List<(string Field, string Low, string High)> Validate()
		{
			var errors = new List<(string, string, string)>();

			if (!string.IsNullOrWhiteSpace(Mode) && !TryMode(out _))
				errors.Add((nameof(Mode), string.Empty, string.Empty));

			// Only a blank speed means "use the default"; zero, a negative or a non-number is refused rather than read as blank,
			// which would clear a speed already saved.
			var kmh = ToKmh(Speed);
			if (Speed.HasValue && (!kmh.HasValue || kmh < UnitTravelProfile.MinimumSpeedKmh || kmh > UnitTravelProfile.MaximumSpeedKmh))
				errors.Add((nameof(Speed), FormatSpeed(UnitTravelProfile.MinimumSpeedKmh), FormatSpeed(UnitTravelProfile.MaximumSpeedKmh)));

			return errors;
		}

		/// <summary>Adds each failure to the model state as "TravelSpeedRange|low|high" (or "TravelModeInvalid") for the page to localize.</summary>
		public void AddErrorsTo(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelState, string prefix = "TravelMode")
		{
			foreach (var (field, low, high) in Validate())
				modelState.AddModelError(prefix + "." + field, field == nameof(Mode) ? "TravelModeInvalid" : "TravelSpeedRange|" + low + "|" + high);
		}

		public void ApplyTo(UnitType type)
		{
			type.TravelMode = TryMode(out var mode) ? (int)mode : (int)UnitTravelModes.Driving;
			type.TravelSpeedKmh = ToKmh(Speed);
		}

		public void ApplyTo(Unit unit)
		{
			unit.TravelMode = TryMode(out var mode) ? (int)mode : (int?)null;
			unit.TravelSpeedKmh = ToKmh(Speed);
		}

		private bool TryMode(out UnitTravelModes mode)
		{
			mode = UnitTravelModes.Driving;
			if (string.IsNullOrWhiteSpace(Mode) || !int.TryParse(Mode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
				!Enum.IsDefined(typeof(UnitTravelModes), value))
				return false;

			mode = (UnitTravelModes)value;
			return true;
		}

		private double? ToKmh(double? value) =>
			value.HasValue && value.Value > 0 && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
				? Math.Round(Metric ? value.Value : value.Value * KilometersPerMile, 2)
				: (double?)null;

		private string FormatSpeed(double kmh) =>
			Metric ? kmh.ToString("0.#", CultureInfo.CurrentCulture) + " km/h" : (kmh / KilometersPerMile).ToString("0.#", CultureInfo.CurrentCulture) + " mph";

		private static double? ToDisplaySpeed(double? kmh, bool metric) =>
			kmh.HasValue ? Math.Round(metric ? kmh.Value : kmh.Value / KilometersPerMile, 1) : (double?)null;
	}
}
