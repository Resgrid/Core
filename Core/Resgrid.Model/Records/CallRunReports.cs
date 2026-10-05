using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// A run (or callback) report another station, unit or member wrote for the same call, as the incident report author
	/// sees it: who wrote it, its state, the units and times it records, who was there, and its narrative. Read-only; the
	/// author pulls what they need into the incident report.
	/// </summary>
	public class CallRunReport
	{
		public string RecordId { get; set; }
		public string DefinitionKey { get; set; }
		/// <summary>"Run", "Callback" or the department definition's name.</summary>
		public string TypeName { get; set; }
		/// <summary>The record number once finalized, else the draft reference.</summary>
		public string Reference { get; set; }
		/// <summary><see cref="RmsRecordState"/>.</summary>
		public int State { get; set; }
		public bool IsFinal { get; set; }
		public string AuthorUserId { get; set; }
		public string AuthorName { get; set; }
		public int? StationGroupId { get; set; }
		public string StationName { get; set; }
		public DateTime? StartedOn { get; set; }
		public DateTime? EndedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public string Location { get; set; }
		public string InitialReport { get; set; }
		public string Cause { get; set; }
		public string OtherAgencies { get; set; }
		public string OtherUnits { get; set; }
		/// <summary>Plain text; null when the narrative is withheld (Protected Data without a grant).</summary>
		public string Narrative { get; set; }
		public bool ContentWithheld { get; set; }
		public List<CallRunReportUnit> Units { get; set; } = new List<CallRunReportUnit>();
		public List<CallRunReportPerson> Personnel { get; set; } = new List<CallRunReportPerson>();
	}

	public class CallRunReportUnit
	{
		public int UnitId { get; set; }
		public string Name { get; set; }
		public DateTime? DispatchedOn { get; set; }
		public DateTime? EnrouteOn { get; set; }
		public DateTime? OnSceneOn { get; set; }
		/// <summary>Released from the scene (the incident report's unit clear time).</summary>
		public DateTime? ReleasedOn { get; set; }
		public DateTime? InQuartersOn { get; set; }
		/// <summary>People the report puts on this unit.</summary>
		public int Crew { get; set; }
	}

	public class CallRunReportPerson
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public int? UnitId { get; set; }
		public string Role { get; set; }
		public DateTime? StartOn { get; set; }
		public DateTime? EndOn { get; set; }
	}
}
