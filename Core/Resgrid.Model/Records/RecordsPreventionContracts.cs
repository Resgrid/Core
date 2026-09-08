using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Resgrid.Model
{
	/// <summary>The separately enabled RMS-5 modules and the feature flag that gates each (RMS plan section 6, RMS-5).</summary>
	public enum RecordsPreventionModule
	{
		Occupancy = 1,
		Inspections = 2,
		Hydrants = 3,
		Permits = 4,
		Crr = 5,
		Investigations = 6,
		QualityReview = 7
	}

	public static class RecordsPreventionModules
	{
		public static string FlagKey(RecordsPreventionModule module)
		{
			switch (module)
			{
				case RecordsPreventionModule.Occupancy: return FeatureFlagKeys.RecordsPreventionOccupancy;
				case RecordsPreventionModule.Inspections: return FeatureFlagKeys.RecordsPreventionInspections;
				case RecordsPreventionModule.Hydrants: return FeatureFlagKeys.RecordsPreventionHydrants;
				case RecordsPreventionModule.Permits: return FeatureFlagKeys.RecordsPreventionPermits;
				case RecordsPreventionModule.Crr: return FeatureFlagKeys.RecordsPreventionCrr;
				case RecordsPreventionModule.Investigations: return FeatureFlagKeys.RecordsInvestigations;
				case RecordsPreventionModule.QualityReview: return FeatureFlagKeys.RecordsQualityReview;
				default: throw new ArgumentOutOfRangeException(nameof(module));
			}
		}

		public static readonly IReadOnlyList<RecordsPreventionModule> All = new[]
		{
			RecordsPreventionModule.Occupancy, RecordsPreventionModule.Inspections, RecordsPreventionModule.Hydrants,
			RecordsPreventionModule.Permits, RecordsPreventionModule.Crr, RecordsPreventionModule.Investigations, RecordsPreventionModule.QualityReview
		};
	}

	/// <summary>Thrown when a prevention module is not enabled for the department; surfaces as 404 on the API and Web.</summary>
	public class RecordsModuleDisabledException : InvalidOperationException
	{
		public RecordsModuleDisabledException(RecordsPreventionModule module)
			: base($"The Records {module} module is not enabled for this department.")
		{
			Module = module;
		}

		public RecordsPreventionModule Module { get; }
	}

	/// <summary>Address folding shared by the crosswalk inventory and the occupancy master so both sides match the same way.</summary>
	public static class AddressNormalizer
	{
		private static readonly Regex NonAlphanumeric = new Regex("[^A-Z0-9 ]", RegexOptions.Compiled);
		private static readonly Regex Whitespace = new Regex(" {2,}", RegexOptions.Compiled);
		private static readonly Dictionary<string, string> Abbreviations = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["STREET"] = "ST", ["AVENUE"] = "AVE", ["ROAD"] = "RD", ["DRIVE"] = "DR", ["BOULEVARD"] = "BLVD", ["LANE"] = "LN",
			["COURT"] = "CT", ["PLACE"] = "PL", ["HIGHWAY"] = "HWY", ["PARKWAY"] = "PKWY", ["NORTH"] = "N", ["SOUTH"] = "S",
			["EAST"] = "E", ["WEST"] = "W", ["SUITE"] = "STE", ["APARTMENT"] = "APT", ["UNIT"] = "UNIT", ["FIRST"] = "1ST",
			["SECOND"] = "2ND", ["THIRD"] = "3RD"
		};

		public static string Normalize(string address)
		{
			if (string.IsNullOrWhiteSpace(address))
				return null;
			var upper = address.Normalize(NormalizationForm.FormD).ToUpperInvariant();
			var sb = new StringBuilder(upper.Length);
			foreach (var c in upper)
				if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
					sb.Append(c == ',' || c == '.' || c == '-' || c == '#' ? ' ' : c);
			var folded = NonAlphanumeric.Replace(sb.ToString(), " ");
			var parts = Whitespace.Replace(folded, " ").Trim().Split(' ');
			for (var i = 0; i < parts.Length; i++)
				if (Abbreviations.TryGetValue(parts[i], out var abbreviation))
					parts[i] = abbreviation;
			var result = string.Join(" ", parts).Trim();
			return result.Length == 0 ? null : result;
		}
	}

	/// <summary>Result of an inventory pass over the department's structure sources.</summary>
	public class OccupancyCrosswalkInventoryResult
	{
		public int SourcesScanned { get; set; }
		public int CandidatesCreated { get; set; }
		public int CandidatesUpdated { get; set; }
		public int Groups { get; set; }
		public int AlreadyDecided { get; set; }
	}

	public class OccupancyReconciliationStatus
	{
		public RmsOccupancyOwnershipState State { get; set; }
		public DateTime? InventoriedOn { get; set; }
		public DateTime? SwitchedOn { get; set; }
		public int Candidates { get; set; }
		public int Linked { get; set; }
		public int Rejected { get; set; }
		public int Occupancies { get; set; }
		/// <summary>Contact pre-plans that exist but have no Linked or Rejected crosswalk row; each blocks the switch.</summary>
		public int UnreconciledPreplans { get; set; }
		public bool CanSwitchToRecords => State != RmsOccupancyOwnershipState.RecordsOwned && Candidates == 0 && UnreconciledPreplans == 0 && InventoriedOn.HasValue;
	}

	/// <summary>Lightweight map-layer row for hydrants (v4 and the web map); never carries notes.</summary>
	public class HydrantMapPoint
	{
		public string HydrantId { get; set; }
		public string HydrantNumber { get; set; }
		public int Type { get; set; }
		public decimal Latitude { get; set; }
		public decimal Longitude { get; set; }
		public int FlowClass { get; set; }
		public int? FlowGpm { get; set; }
		public bool InService { get; set; }
		public decimal? MainSizeInches { get; set; }
	}

	/// <summary>One line of a hydrant CSV import; parsed leniently, validated strictly.</summary>
	public class HydrantImportRow
	{
		public int Line { get; set; }
		public string HydrantNumber { get; set; }
		public decimal Latitude { get; set; }
		public decimal Longitude { get; set; }
		public string Type { get; set; }
		public string Address { get; set; }
		public string MainSize { get; set; }
		public string FlowGpm { get; set; }
		public string Owner { get; set; }
		public string Error { get; set; }
	}

	public class HydrantImportResult
	{
		public int RowsRead { get; set; }
		public int Created { get; set; }
		public int Updated { get; set; }
		public List<HydrantImportRow> Rejected { get; set; } = new List<HydrantImportRow>();
	}

	public class PreventionSummary
	{
		public int Occupancies { get; set; }
		public int OccupanciesReviewOverdue { get; set; }
		public int InspectionsScheduled { get; set; }
		public int InspectionsDue { get; set; }
		public int ViolationsOpen { get; set; }
		public int ViolationsOverdue { get; set; }
		public int Hydrants { get; set; }
		public int HydrantsOutOfService { get; set; }
		public int HydrantsTestDue { get; set; }
		public int PermitsActive { get; set; }
		public int PermitsExpiringSoon { get; set; }
		public int PermitsAwaitingReview { get; set; }
		public int CrrActivitiesLast90Days { get; set; }
		public int CrrSmokeAlarmsLast90Days { get; set; }
		public int InvestigationCasesOpen { get; set; }
	}
}
