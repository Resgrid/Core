using System;
using System.Collections.Generic;
using System.Globalization;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Units
{
	/// <summary>
	/// The "Vehicle size and weight" fields of the unit type and unit edit pages (maplibre-mapping-routing-navigation-plan.md
	/// §10.3, owner Q2). Entered in feet and pounds or metres and kilograms, stored metric on <see cref="UnitType"/> and
	/// <see cref="Unit"/>. Blank fields store null: on a unit that means "use the unit type's value".
	/// </summary>
	public class VehicleSizeInput
	{
		public const double MetersPerFoot = 0.3048;
		public const double KilogramsPerPound = 0.45359237;

		/// <summary>True when the values below are metres and kilograms; false for feet and pounds.</summary>
		public bool Metric { get; set; }

		public double? Height { get; set; }

		public double? Width { get; set; }

		public double? Length { get; set; }

		public double? GrossWeight { get; set; }

		public double? AxleLoad { get; set; }

		public int? AxleCount { get; set; }

		/// <summary>"" uses the unit type's value (units only), "true" or "false".</summary>
		public string Hazmat { get; set; }

		/// <summary>Unit pages only: each unit type's values (metric), keyed by type name, shown as the inherited values.</summary>
		public Dictionary<string, VehicleProfile> TypeDefaults { get; set; }

		/// <summary>The form's default units: feet and pounds for US English, metres and kilograms elsewhere.</summary>
		public static bool DefaultMetric()
		{
			var culture = CultureInfo.CurrentUICulture?.Name ?? string.Empty;
			return !(culture.Equals("en-US", StringComparison.OrdinalIgnoreCase) || culture.Equals("en", StringComparison.OrdinalIgnoreCase) || culture.Length == 0);
		}

		/// <summary>Which vehicle limits a routing provider applies (Valhalla all seven, Mapbox height/width/weight).</summary>
		public static string LimitsFor(string routingProvider)
		{
			switch (RoutingProviderNames.Normalize(routingProvider))
			{
				case RoutingProviderNames.Valhalla:
					return "all";
				case RoutingProviderNames.Mapbox:
					return "size";
				default:
					return "none";
			}
		}

		public static VehicleSizeInput From(double? height, double? width, double? length, double? grossWeight, double? axleLoad, int? axleCount, bool? hazmat, bool metric)
		{
			return new VehicleSizeInput
			{
				Metric = metric,
				Height = ToDisplayLength(height, metric),
				Width = ToDisplayLength(width, metric),
				Length = ToDisplayLength(length, metric),
				GrossWeight = ToDisplayWeight(grossWeight, metric),
				AxleLoad = ToDisplayWeight(axleLoad, metric),
				AxleCount = axleCount,
				Hazmat = hazmat.HasValue ? (hazmat.Value ? "true" : "false") : string.Empty
			};
		}

		public static VehicleSizeInput From(UnitType type, bool metric) =>
			From(type?.VehicleHeightMeters, type?.VehicleWidthMeters, type?.VehicleLengthMeters, type?.VehicleGrossWeightKg, type?.VehicleAxleLoadKg,
				type?.VehicleAxleCount, type?.VehicleHazmat, metric);

		public static VehicleSizeInput From(Unit unit, bool metric) =>
			From(unit?.VehicleHeightMeters, unit?.VehicleWidthMeters, unit?.VehicleLengthMeters, unit?.VehicleGrossWeightKg, unit?.VehicleAxleLoadKg,
				unit?.VehicleAxleCount, unit?.VehicleHazmat, metric);

		/// <summary>
		/// Range checks in metric (height 1–6 m, width 1–4 m, length 2–40 m, weight 500–80,000 kg, axle load 500–25,000 kg,
		/// axles 2–12). Each failure is (field, label key, low, high) in the entered units, for the page to localize.
		/// </summary>
		public List<(string Field, string LabelKey, string Low, string High)> Validate()
		{
			var errors = new List<(string, string, string, string)>();

			void CheckLength(string field, string label, double? value, double low, double high)
			{
				var meters = ToMeters(value);
				if (meters.HasValue && (meters < low || meters > high))
					errors.Add((field, label, FormatLength(low), FormatLength(high)));
			}

			void CheckWeight(string field, string label, double? value, double low, double high)
			{
				var kilograms = ToKilograms(value);
				if (kilograms.HasValue && (kilograms < low || kilograms > high))
					errors.Add((field, label, FormatWeight(low), FormatWeight(high)));
			}

			CheckLength(nameof(Height), "VehicleHeight", Height, 1, 6);
			CheckLength(nameof(Width), "VehicleWidth", Width, 1, 4);
			CheckLength(nameof(Length), "VehicleLength", Length, 2, 40);
			CheckWeight(nameof(GrossWeight), "VehicleGrossWeight", GrossWeight, 500, 80000);
			CheckWeight(nameof(AxleLoad), "VehicleAxleLoad", AxleLoad, 500, 25000);

			if (AxleCount.HasValue && (AxleCount < 2 || AxleCount > 12))
				errors.Add((nameof(AxleCount), "VehicleAxleCount", "2", "12"));

			return errors;
		}

		/// <summary>Adds each range failure to the model state under the posted field name ("VehicleSize.Height").</summary>
		public void AddErrorsTo(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelState, string prefix = "VehicleSize")
		{
			foreach (var (field, labelKey, low, high) in Validate())
				modelState.AddModelError(prefix + "." + field, labelKey + "|" + low + "|" + high);
		}

		/// <summary>Each unit type's vehicle values (metric), by type name, for the unit pages' inherited placeholders.</summary>
		public static Dictionary<string, VehicleProfile> DefaultsFor(IEnumerable<UnitType> types)
		{
			var defaults = new Dictionary<string, VehicleProfile>(StringComparer.Ordinal);

			foreach (var type in types ?? new List<UnitType>())
			{
				if (type == null || string.IsNullOrWhiteSpace(type.Type) || defaults.ContainsKey(type.Type))
					continue;

				defaults[type.Type] = VehicleProfile.Resolve(null, type) ?? new VehicleProfile();
			}

			return defaults;
		}

		public void ApplyTo(UnitType type)
		{
			type.VehicleHeightMeters = ToMeters(Height);
			type.VehicleWidthMeters = ToMeters(Width);
			type.VehicleLengthMeters = ToMeters(Length);
			type.VehicleGrossWeightKg = ToKilograms(GrossWeight);
			type.VehicleAxleLoadKg = ToKilograms(AxleLoad);
			type.VehicleAxleCount = AxleCount;
			// A unit type either carries hazardous materials or does not set it.
			type.VehicleHazmat = Hazmat == "true" ? true : (bool?)null;
		}

		public void ApplyTo(Unit unit)
		{
			unit.VehicleHeightMeters = ToMeters(Height);
			unit.VehicleWidthMeters = ToMeters(Width);
			unit.VehicleLengthMeters = ToMeters(Length);
			unit.VehicleGrossWeightKg = ToKilograms(GrossWeight);
			unit.VehicleAxleLoadKg = ToKilograms(AxleLoad);
			unit.VehicleAxleCount = AxleCount;
			unit.VehicleHazmat = Hazmat == "true" ? true : Hazmat == "false" ? false : (bool?)null;
		}

		private double? ToMeters(double? value) =>
			value.HasValue && value.Value > 0 ? Math.Round(Metric ? value.Value : value.Value * MetersPerFoot, 3) : (double?)null;

		private double? ToKilograms(double? value) =>
			value.HasValue && value.Value > 0 ? Math.Round(Metric ? value.Value : value.Value * KilogramsPerPound, 1) : (double?)null;

		private string FormatLength(double meters) =>
			Metric ? meters.ToString("0.#", CultureInfo.CurrentCulture) + " m" : (meters / MetersPerFoot).ToString("0.#", CultureInfo.CurrentCulture) + " ft";

		private string FormatWeight(double kilograms) =>
			Metric ? kilograms.ToString("#,0", CultureInfo.CurrentCulture) + " kg" : (kilograms / KilogramsPerPound).ToString("#,0", CultureInfo.CurrentCulture) + " lb";

		private static double? ToDisplayLength(double? meters, bool metric) =>
			meters.HasValue ? Math.Round(metric ? meters.Value : meters.Value / MetersPerFoot, 2) : (double?)null;

		private static double? ToDisplayWeight(double? kilograms, bool metric) =>
			kilograms.HasValue ? Math.Round(metric ? kilograms.Value : kilograms.Value / KilogramsPerPound, 0) : (double?)null;
	}
}
