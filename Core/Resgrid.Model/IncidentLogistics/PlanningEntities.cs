using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0290 (both dialects).

	/// <summary>A numbered operational period (plan §9.1): the planning unit for forecasts, the IAP and cost. A planning window, not a staffing roster.</summary>
	public class IncidentOperationalPeriod : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentOperationalPeriodId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public int Number { get; set; }
		public DateTime StartOn { get; set; }
		public DateTime EndOn { get; set; }
		public string TimeZoneId { get; set; }
		public DateTime? BriefingOn { get; set; }
		/// <summary><see cref="IncidentPeriodStatuses"/>.</summary>
		public int Status { get; set; }
		public string PlannerUserId { get; set; }
		public bool IsFrozen { get; set; }
		public DateTime? FrozenOn { get; set; }
		public string FrozenByUserId { get; set; }
		public DateTime? ApprovedOn { get; set; }
		public string ApprovedByUserId { get; set; }
		public string CopiedFromPeriodId { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentOperationalPeriods";
		[NotMapped] public string IdName => "IncidentOperationalPeriodId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentOperationalPeriodId; set => IncidentOperationalPeriodId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An IAP assembly manifest for a period (plan §9.16): which RMS records and revisions are included, page order, maps, distribution and print job. Approval and immutability are RMS's.</summary>
	public class IncidentPlanVersion : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentPlanVersionId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentOperationalPeriodId { get; set; }
		public int VersionNumber { get; set; }
		/// <summary><see cref="IncidentPlanVersionStatuses"/>.</summary>
		public int Status { get; set; }
		/// <summary>The RMS record finalized over this manifest.</summary>
		public string RecordId { get; set; }
		public string ManifestJson { get; set; }
		public string DistributionJson { get; set; }
		public int PrintCopies { get; set; }
		public DateTime? ApprovedOn { get; set; }
		public string ApprovedByUserId { get; set; }
		public DateTime? DistributedOn { get; set; }
		public string SupersededByPlanVersionId { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentPlanVersions";
		[NotMapped] public string IdName => "IncidentPlanVersionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentPlanVersionId; set => IncidentPlanVersionId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>The structured weather block of a plan version, snapshotted at approval (plan §9.21). No new weather ingestion; references existing WeatherAlert rows.</summary>
	public class IncidentPlanWeatherBlock : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentPlanWeatherBlockId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentPlanVersionId { get; set; }
		public string IncidentOperationalPeriodId { get; set; }
		public string ForecastSource { get; set; }
		public DateTime? IssuedOn { get; set; }
		public string ForecastPeriod { get; set; }
		public string TemperatureSummary { get; set; }
		public string HumiditySummary { get; set; }
		public string WindSummary { get; set; }
		public string PrecipitationSummary { get; set; }
		public string SpotForecastReference { get; set; }
		public string ActiveAlertIdsJson { get; set; }
		public string Narrative { get; set; }
		public DateTime? SnapshottedOn { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentPlanWeatherBlocks";
		[NotMapped] public string IdName => "IncidentPlanWeatherBlockId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentPlanWeatherBlockId; set => IncidentPlanWeatherBlockId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An incident objective carried across periods (plans §9.1 and §9.25): becomes an ICS 202 line when the plan is published; shared with LI-D boards.</summary>
	public class IncidentObjective : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentObjectiveId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentOperationalPeriodId { get; set; }
		public int Number { get; set; }
		public string Text { get; set; }
		/// <summary><see cref="IncidentObjectiveStatuses"/>.</summary>
		public int Status { get; set; }
		public string CarriedFromObjectiveId { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentObjectives";
		[NotMapped] public string IdName => "IncidentObjectiveId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentObjectiveId; set => IncidentObjectiveId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
