using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Read-only rendering of the NFIRS Basic Module field set for one Call (RMS plan sections 4.3 and 6,
	/// RMS-3: "historical NFIRS read-only rendering and crosswalk reporting ... no import tooling, no NFIRS
	/// authoring"). Every value is read from where the department already holds it — the Call, unit state
	/// history, the Incident Command timeline and the NERIS profile — and each field names the NERIS fact or
	/// section that replaces it, so the page doubles as the per-incident crosswalk report. Nothing here is
	/// written back; NFIRS retired on 2026-01-31.
	/// </summary>
	public class NfirsLegacyRendering
	{
		public const string ProfileName = "NFIRS 5.0 Basic Module (representative field set)";

		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string CallNumber { get; set; }
		public string CallName { get; set; }
		public string Profile { get; set; } = ProfileName;
		public bool ReadOnly => true;
		public DateTime GeneratedOn { get; set; }

		/// <summary>The department's NERIS incident report for this Call, when one exists.</summary>
		public string IncidentReportId { get; set; }
		public string IncidentReportNumber { get; set; }
		public string IncidentReportState { get; set; }

		public List<NfirsLegacyField> Fields { get; set; } = new List<NfirsLegacyField>();
		public NfirsLegacySummary Summary { get; set; } = new NfirsLegacySummary();
		public List<string> Notes { get; set; } = new List<string>();
	}

	public enum NfirsLegacyFieldStatus
	{
		/// <summary>A value was read from an existing source.</summary>
		Populated = 1,

		/// <summary>The source exists but holds no value for this Call.</summary>
		Missing = 2,

		/// <summary>Resgrid never captured this NFIRS field; it is a gap, not an omission.</summary>
		NotCaptured = 3
	}

	public class NfirsLegacyField
	{
		/// <summary>NFIRS Basic Module section letter (A–M).</summary>
		public string Section { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		public bool Required { get; set; }
		public string Value { get; set; }
		public NfirsLegacyFieldStatus Status { get; set; }
		/// <summary>Where the value was read from: Calls, UnitStates, IncidentCommand, NerisProfile, Crosswalk.</summary>
		public string SourceSystem { get; set; }
		/// <summary>The NERIS fact key (<see cref="NerisFactKeys"/>) or section that carries this field going forward; null when NERIS has no equivalent.</summary>
		public string NerisFactKey { get; set; }
		public string NerisSection { get; set; }
		/// <summary>Whether the department's NERIS report for the Call already carries the crosswalked value; null when no report exists.</summary>
		public bool? NerisPopulated { get; set; }
	}

	public class NfirsLegacySummary
	{
		public int TotalFields { get; set; }
		public int Populated { get; set; }
		public int Missing { get; set; }
		public int NotCaptured { get; set; }
		public int RequiredMissing { get; set; }
		/// <summary>Fields that have a NERIS equivalent at all.</summary>
		public int CrosswalkedToNeris { get; set; }
		/// <summary>Crosswalked fields the department's NERIS report already carries.</summary>
		public int CrosswalkedAndPopulated { get; set; }
	}
}
