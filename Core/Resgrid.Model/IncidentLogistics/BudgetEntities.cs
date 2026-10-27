using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0295 (both dialects).

	/// <summary>An incident budget (plan §9.13); the signed delegation is an RMS record.</summary>
	public class IncidentBudget : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentBudgetId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string Name { get; set; }
		public decimal OriginalAmount { get; set; }
		public decimal RevisedAmount { get; set; }
		public string Currency { get; set; }
		/// <summary><see cref="IncidentBudgetStatuses"/>.</summary>
		public int Status { get; set; }
		public string RecordId { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentBudgets";
		[NotMapped] public string IdName => "IncidentBudgetId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentBudgetId; set => IncidentBudgetId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A budget line by section, function, facility, vendor, cost group or period, with thresholds (plan §9.13).</summary>
	public class IncidentBudgetLine : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentBudgetLineId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentBudgetId { get; set; }
		public string Name { get; set; }
		/// <summary><see cref="IncidentBudgetLineKinds"/>.</summary>
		public int Kind { get; set; }
		public string ScopeKey { get; set; }
		public string IncidentFundingCodeId { get; set; }
		public decimal OriginalAmount { get; set; }
		public decimal RevisedAmount { get; set; }
		public string Currency { get; set; }
		public decimal? ThresholdPercent { get; set; }
		public decimal? FloorAmount { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentBudgetLines";
		[NotMapped] public string IdName => "IncidentBudgetLineId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentBudgetLineId; set => IncidentBudgetLineId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An initial authorization, revision or transfer of budget authority, append-only (plan §9.13).</summary>
	public class IncidentFundingAuthorization : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentFundingAuthorizationId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentBudgetLineId { get; set; }
		public string FromBudgetLineId { get; set; }
		/// <summary><see cref="IncidentFundingAuthorizationKinds"/>.</summary>
		public int Kind { get; set; }
		public decimal Amount { get; set; }
		public string Currency { get; set; }
		public DateTime AuthorizedOn { get; set; }
		public string RecordId { get; set; }
		public string Reason { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentFundingAuthorizations";
		[NotMapped] public string IdName => "IncidentFundingAuthorizationId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentFundingAuthorizationId; set => IncidentFundingAuthorizationId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Who may request, approve, commit, receive, accept or close, up to what amount (plan §9.13, §15.1).</summary>
	public class IncidentSpendingDelegation : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentSpendingDelegationId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string PositionCode { get; set; }
		public string UserId { get; set; }
		/// <summary><see cref="IncidentSpendingActions"/>.</summary>
		public int Action { get; set; }
		public decimal ThresholdAmount { get; set; }
		public string Currency { get; set; }
		public string IncidentFundingCodeId { get; set; }
		public DateTime EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public string RecordId { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentSpendingDelegations";
		[NotMapped] public string IdName => "IncidentSpendingDelegationId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentSpendingDelegationId; set => IncidentSpendingDelegationId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A FireCode, FEMA DR/P-code, project or agency accounting code with effective dates (plan §9.13).</summary>
	public class IncidentFundingCode : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentFundingCodeId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string Code { get; set; }
		/// <summary><see cref="IncidentFundingCodeKinds"/>.</summary>
		public int Kind { get; set; }
		public string Description { get; set; }
		public string PayingAgency { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentFundingCodes";
		[NotMapped] public string IdName => "IncidentFundingCodeId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentFundingCodeId; set => IncidentFundingCodeId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A cost group for slicing incident cost.</summary>
	public class IncidentCostGroup : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentCostGroupId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string Name { get; set; }
		public string Code { get; set; }
		public string Description { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentCostGroups";
		[NotMapped] public string IdName => "IncidentCostGroupId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentCostGroupId; set => IncidentCostGroupId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
