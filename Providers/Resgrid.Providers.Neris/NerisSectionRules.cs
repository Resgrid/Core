using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Providers.Neris
{
	/// <summary>
	/// Progressive section rules (RMS plan section 4.2, RMS-3): which conditional sections an incident must carry,
	/// decided from the incident types the author selected. "Progressive" means a section is only demanded once the
	/// type that needs it is chosen — an author who has not yet said what kind of incident this was is not shown a
	/// wall of required sections, and a medical run is never asked for a fire cause.
	/// <para>
	/// Requirements come from the incident-type taxonomy of the pinned contract (FIRE, HAZSIT, MEDICAL, RESCUE,
	/// PUBSERV, NOEMERG, LAWENFORCE at the first level). Anything the contract merely encourages is a warning, so
	/// a report is never blocked on a section the destination would accept without.
	/// </para>
	/// </summary>
	public static class NerisSectionRules
	{
		public sealed class SectionRequirement
		{
			public SectionRequirement(RmsIncidentModuleKind kind, bool required, string reason)
			{
				Kind = kind;
				Required = required;
				Reason = reason;
			}

			public RmsIncidentModuleKind Kind { get; }

			/// <summary>True blocks finalization; false surfaces as a warning the author may answer or ignore.</summary>
			public bool Required { get; }

			/// <summary>Why the section applies, shown beside it in the authoring surface.</summary>
			public string Reason { get; }
		}

		public const string StructureFirePrefix = "FIRE||STRUCTURE_FIRE";
		public const string OutsideFirePrefix = "FIRE||OUTSIDE_FIRE";
		public const string TransportationFirePrefix = "FIRE||TRANSPORTATION_FIRE";
		public const string SpecialFirePrefix = "FIRE||SPECIAL_FIRE";
		public const string HazmatPrefix = "HAZSIT||HAZARDOUS_MATERIALS";
		public const string MedicalPrefix = "MEDICAL";
		public const string FirePrefix = "FIRE";

		/// <summary>
		/// The sections that apply to a set of incident type codes, most specific first. The result is what the
		/// authoring surface renders and what <see cref="NerisValidationService"/> checks; there is no second list.
		/// </summary>
		public static IReadOnlyList<SectionRequirement> For(IEnumerable<string> incidentTypeCodes)
		{
			var codes = (incidentTypeCodes ?? Enumerable.Empty<string>())
				.Where(c => !string.IsNullOrWhiteSpace(c))
				.Select(c => c.Trim())
				.ToList();

			var requirements = new List<SectionRequirement>();
			void Add(RmsIncidentModuleKind kind, bool required, string reason)
			{
				if (requirements.All(r => r.Kind != kind))
					requirements.Add(new SectionRequirement(kind, required, reason));
			}

			bool Any(string prefix) => codes.Any(c => c.StartsWith(prefix, StringComparison.Ordinal));

			if (Any(FirePrefix))
				Add(RmsIncidentModuleKind.Fire, true, "A fire incident type is selected.");

			if (Any(StructureFirePrefix))
			{
				Add(RmsIncidentModuleKind.StructureFireLocation, true, "The fire was in a structure.");
				Add(RmsIncidentModuleKind.SmokeAlarm, false, "Structure fires report whether a smoke alarm was present.");
				Add(RmsIncidentModuleKind.FireAlarm, false, "Structure fires report whether a fire alarm system was present.");
				Add(RmsIncidentModuleKind.FireSuppression, false, "Structure fires report whether automatic suppression was present.");
			}

			if (Any(OutsideFirePrefix) || Any(SpecialFirePrefix) || Any(TransportationFirePrefix))
				Add(RmsIncidentModuleKind.OutsideFireLocation, true, "The fire was outside a structure.");

			if (Any(HazmatPrefix))
			{
				Add(RmsIncidentModuleKind.Hazsit, true, "A hazardous-materials incident type is selected.");
				Add(RmsIncidentModuleKind.Chemical, false, "Record each chemical released.");
			}

			if (Any(MedicalPrefix))
				Add(RmsIncidentModuleKind.Medical, true, "A medical incident type is selected.");

			return requirements;
		}

		/// <summary>
		/// The structure and outside fire location sections are mutually exclusive: the contract's location_detail
		/// is one discriminated value, so carrying both would make the payload ambiguous.
		/// </summary>
		public static bool Conflicts(RmsIncidentModuleKind a, RmsIncidentModuleKind b)
		{
			return (a == RmsIncidentModuleKind.StructureFireLocation && b == RmsIncidentModuleKind.OutsideFireLocation)
				|| (a == RmsIncidentModuleKind.OutsideFireLocation && b == RmsIncidentModuleKind.StructureFireLocation);
		}

		/// <summary>The value set a section's headline code must belong to, or null when the section has no coded headline.</summary>
		public static string PrimaryCodeSetFor(RmsIncidentModuleKind kind)
		{
			switch (kind)
			{
				case RmsIncidentModuleKind.Fire: return "water_supply";
				case RmsIncidentModuleKind.StructureFireLocation: return "fire_cause_indoor";
				case RmsIncidentModuleKind.OutsideFireLocation: return "fire_cause_outdoor";
				case RmsIncidentModuleKind.Hazsit: return "hazard_disposition";
				case RmsIncidentModuleKind.Chemical: return "hazard_physical_state";
				case RmsIncidentModuleKind.Medical: return "medical_patient_care";
				case RmsIncidentModuleKind.SmokeAlarm: return "alarm_smoke";
				case RmsIncidentModuleKind.FireAlarm: return "alarm_fire";
				case RmsIncidentModuleKind.OtherAlarm: return "alarm_other";
				case RmsIncidentModuleKind.FireSuppression: return "suppress_fire";
				case RmsIncidentModuleKind.CookingFireSuppression: return "suppress_cooking";
				case RmsIncidentModuleKind.ElectricHazard: return "emerghaz_elec";
				case RmsIncidentModuleKind.PowergenHazard: return "emerghaz_pv";
				case RmsIncidentModuleKind.MedicalOxygenHazard: return "yes_no_unknown";
				case RmsIncidentModuleKind.CsstHazard: return "yes_no_unknown";
				case RmsIncidentModuleKind.StructureFireOrigin: return "room";
				case RmsIncidentModuleKind.OutsideFire: return "fire_cause_outdoor";
				case RmsIncidentModuleKind.HazsitAnalysis: return "hazsit_release_factors";
				case RmsIncidentModuleKind.Product: return "consumer_product";
				case RmsIncidentModuleKind.Battery: return "battery_chemistry";
				default: return null;
			}
		}

		/// <summary>The value set a section's secondary code must belong to, or null when it has none.</summary>
		public static string SecondaryCodeSetFor(RmsIncidentModuleKind kind)
		{
			switch (kind)
			{
				case RmsIncidentModuleKind.Fire: return "fire_invest_need";
				case RmsIncidentModuleKind.StructureFireLocation: return "fire_bldg_damage";
				case RmsIncidentModuleKind.SmokeAlarm:
				case RmsIncidentModuleKind.FireAlarm:
				case RmsIncidentModuleKind.OtherAlarm: return "alarm_operation";
				case RmsIncidentModuleKind.FireSuppression:
				case RmsIncidentModuleKind.CookingFireSuppression: return "suppress_operation";
				case RmsIncidentModuleKind.Medical: return "medical_transport";
				case RmsIncidentModuleKind.Chemical: return "hazard_released_into";
				case RmsIncidentModuleKind.StructureFireOrigin: return "item_first_ignited";
				case RmsIncidentModuleKind.Battery: return "battery_cell";
				default: return null;
			}
		}
	}
}
