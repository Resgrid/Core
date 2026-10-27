using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0295 (both dialects).

	/// <summary>A rate schedule entry as applied (plan §10 R18) — never a second rate model. A person's rate sits in PersonalAmount (protected); equipment, vendor and contract rates in Amount.</summary>
	public class IncidentRateSnapshot : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentProtectable
	{
		[Required]
		public string IncidentRateSnapshotId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		/// <summary><see cref="IncidentRateSubjectKinds"/>.</summary>
		public int SubjectKind { get; set; }
		public string SubjectId { get; set; }
		public string RateScheduleId { get; set; }
		public string RateScheduleEntryId { get; set; }
		public string VendorAgreementId { get; set; }
		public string Description { get; set; }
		public decimal? Amount { get; set; }
		public string Unit { get; set; }
		public string Currency { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public string RulesJson { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string PersonalAmount { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentRateSnapshots";
		[NotMapped] public string IdName => "IncidentRateSnapshotId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentRateSnapshotId; set => IncidentRateSnapshotId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One dated cost fact in exactly one measure (plan §13.4): Estimated, Reserved, Committed, Accrued, Actual, Invoiced or Posted are never blended. Labor priced from a personal rate keeps its amount in ProtectedAmount.</summary>
	public class IncidentCostTransaction : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentProtectable
	{
		[Required]
		public string IncidentCostTransactionId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentOperationalPeriodId { get; set; }
		/// <summary><see cref="IncidentCostMeasures"/>.</summary>
		public int Measure { get; set; }
		/// <summary><see cref="IncidentCostCategories"/>.</summary>
		public int Category { get; set; }
		public string SourceType { get; set; }
		public string SourceId { get; set; }
		public string IncidentResourceId { get; set; }
		public string IncidentParticipantId { get; set; }
		public string VendorId { get; set; }
		public string IncidentBudgetLineId { get; set; }
		public string IncidentFundingCodeId { get; set; }
		public string IncidentCostGroupId { get; set; }
		/// <summary><see cref="IncidentSections"/>.</summary>
		public int Section { get; set; }
		public string IncidentFacilityId { get; set; }
		public string Agency { get; set; }
		public string IncidentRecoveryProjectId { get; set; }
		public string MemberActivationId { get; set; }
		public DateTime CostDate { get; set; }
		public decimal? Quantity { get; set; }
		public string Unit { get; set; }
		public decimal? Amount { get; set; }
		public string Currency { get; set; }
		public bool IsSystemGenerated { get; set; }
		public string AdjustsTransactionId { get; set; }
		public string RunId { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string ProtectedAmount { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentCostTransactions";
		[NotMapped] public string IdName => "IncidentCostTransactionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentCostTransactionId; set => IncidentCostTransactionId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A daily rollup by measure and category produced by the daily cost worker (plan §15.3a: aggregates are unprotected totals with a minimum-cell rule).</summary>
	public class IncidentAccrualSnapshot : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentAccrualSnapshotId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public DateTime AsOfDate { get; set; }
		/// <summary><see cref="IncidentCostMeasures"/>.</summary>
		public int Measure { get; set; }
		/// <summary><see cref="IncidentCostCategories"/>.</summary>
		public int Category { get; set; }
		public decimal Amount { get; set; }
		public string Currency { get; set; }
		public int SubjectCount { get; set; }
		public string RunId { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentAccrualSnapshots";
		[NotMapped] public string IdName => "IncidentAccrualSnapshotId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentAccrualSnapshotId; set => IncidentAccrualSnapshotId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A cost projection scenario: daily burn, cumulative to date and forecast at completion (plan §9.13).</summary>
	public class IncidentCostProjection : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentCostProjectionId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string ScenarioName { get; set; }
		public DateTime? ProjectedEndOn { get; set; }
		public decimal DailyBurn { get; set; }
		public decimal CumulativeToDate { get; set; }
		public decimal ForecastAtCompletion { get; set; }
		public string Currency { get; set; }
		public string Assumptions { get; set; }
		public string RunId { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentCostProjections";
		[NotMapped] public string IdName => "IncidentCostProjectionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentCostProjectionId; set => IncidentCostProjectionId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A versioned cost-share agreement (plan §9.23). Amendments create a new version; the signed document is an RMS record.</summary>
	public class IncidentCostShareAgreement : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentCostShareAgreementId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string Name { get; set; }
		public int Version { get; set; }
		/// <summary><see cref="IncidentCostShareBases"/>.</summary>
		public int Basis { get; set; }
		public DateTime? EffectiveFrom { get; set; }
		public DateTime? EffectiveTo { get; set; }
		public string RecordId { get; set; }
		/// <summary><see cref="IncidentCostShareStatuses"/>.</summary>
		public int Status { get; set; }
		public string PartiesJson { get; set; }
		public string SupersedesAgreementId { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentCostShareAgreements";
		[NotMapped] public string IdName => "IncidentCostShareAgreementId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentCostShareAgreementId; set => IncidentCostShareAgreementId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An ordered apportionment rule of an agreement version (plan §9.23).</summary>
	public class IncidentCostShareRule : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentCostShareRuleId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentCostShareAgreementId { get; set; }
		public int RuleOrder { get; set; }
		/// <summary><see cref="IncidentCostShareScopes"/>.</summary>
		public int ScopeKind { get; set; }
		public string ScopeValue { get; set; }
		/// <summary><see cref="IncidentCostShareMethods"/>.</summary>
		public int Method { get; set; }
		public string ParametersJson { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentCostShareRules";
		[NotMapped] public string IdName => "IncidentCostShareRuleId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentCostShareRuleId; set => IncidentCostShareRuleId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A reproducible apportionment run: agreement version, rule versions, the cost cut consumed and its checksum (plan §9.23).</summary>
	public class IncidentApportionmentRun : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentApportionmentRunId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentCostShareAgreementId { get; set; }
		public int AgreementVersion { get; set; }
		/// <summary><see cref="IncidentApportionmentRunKinds"/>.</summary>
		public int RunKind { get; set; }
		public DateTime ThroughDate { get; set; }
		public string InputChecksum { get; set; }
		public string OutputChecksum { get; set; }
		public decimal TotalApportioned { get; set; }
		public string Currency { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentApportionmentRuns";
		[NotMapped] public string IdName => "IncidentApportionmentRunId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentApportionmentRunId; set => IncidentApportionmentRunId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A party's apportioned share from a run; re-apportionment adds adjusting rows, never in-place edits (plan §9.23).</summary>
	public class IncidentCostAllocation : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentCostAllocationId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentApportionmentRunId { get; set; }
		public string IncidentCostShareRuleId { get; set; }
		public string PartyKey { get; set; }
		public string PartyName { get; set; }
		/// <summary><see cref="IncidentCostCategories"/>.</summary>
		public int Category { get; set; }
		public decimal Amount { get; set; }
		public string Currency { get; set; }
		/// <summary><see cref="IncidentCostShareMethods"/>.</summary>
		public int Method { get; set; }
		public string AdjustsAllocationId { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentCostAllocations";
		[NotMapped] public string IdName => "IncidentCostAllocationId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentCostAllocationId; set => IncidentCostAllocationId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
