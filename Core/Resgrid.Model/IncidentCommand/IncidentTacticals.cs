using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>A tactical objective / benchmark for an incident (e.g. "Primary search complete").</summary>
	public class TacticalObjective : IEntity
	{
		public string TacticalObjectiveId { get; set; }

		public string IncidentCommandId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		public string Name { get; set; }

		/// <summary>Maps to <see cref="TacticalObjectiveType"/>.</summary>
		public int ObjectiveType { get; set; }

		/// <summary>Maps to <see cref="TacticalObjectiveStatus"/>.</summary>
		public int Status { get; set; }

		public bool AutoPopulated { get; set; }

		public string CompletedByUserId { get; set; }

		public DateTime? CompletedOn { get; set; }

		public int SortOrder { get; set; }

		[NotMapped]
		public string TableName => "TacticalObjectives";

		[NotMapped]
		public string IdName => "TacticalObjectiveId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return TacticalObjectiveId; }
			set { TacticalObjectiveId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A scene / benchmark / role timer for an incident. Personnel accountability (PAR) is handled by the
	/// Checkin feature, not by these timers.
	/// </summary>
	public class IncidentTimer : IEntity
	{
		public string IncidentTimerId { get; set; }

		public string IncidentCommandId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		/// <summary>Maps to <see cref="IncidentTimerType"/>.</summary>
		public int TimerType { get; set; }

		/// <summary>Maps to <see cref="IncidentTimerScopeType"/>.</summary>
		public int ScopeType { get; set; }

		/// <summary>Identifier of the scoped object (node id, unit id, ...), null for incident scope.</summary>
		public string ScopeId { get; set; }

		public string Name { get; set; }

		public int IntervalSeconds { get; set; }

		public DateTime StartedOn { get; set; }

		public DateTime? NextDueOn { get; set; }

		/// <summary>Maps to <see cref="IncidentTimerStatus"/>.</summary>
		public int Status { get; set; }

		public DateTime? AcknowledgedOn { get; set; }

		[NotMapped]
		public string TableName => "IncidentTimers";

		[NotMapped]
		public string IdName => "IncidentTimerId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IncidentTimerId; }
			set { IncidentTimerId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A real-time map annotation (markup) on the tactical map, synced across devices.</summary>
	public class IncidentMapAnnotation : IEntity
	{
		public string IncidentMapAnnotationId { get; set; }

		public string IncidentCommandId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		/// <summary>Maps to <see cref="IncidentMapAnnotationType"/>.</summary>
		public int AnnotationType { get; set; }

		/// <summary>The annotation geometry as a GeoJSON feature.</summary>
		public string GeoJson { get; set; }

		/// <summary>Optional ICS standard symbology code.</summary>
		public string IcsSymbolCode { get; set; }

		public string Label { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? DeletedOn { get; set; }

		[NotMapped]
		public string TableName => "IncidentMapAnnotations";

		[NotMapped]
		public string IdName => "IncidentMapAnnotationId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IncidentMapAnnotationId; }
			set { IncidentMapAnnotationId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
