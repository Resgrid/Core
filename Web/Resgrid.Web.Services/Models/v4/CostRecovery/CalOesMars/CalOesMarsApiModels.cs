using System;
using System.Collections.Generic;
using Resgrid.Model.CostRecovery.CalOesMars;

namespace Resgrid.Web.Services.Models.v4.CostRecovery.CalOesMars
{
	// Workforce & Business Operations plan, Phase C5 (Cal OES MARS): rostered personnel get / save / validate their own
	// incident-bound F-42 and expense drafts; permission-79 holders see the department queue, readiness metadata,
	// expected-versus-observed reimbursement and record manual external observations. Agency identifiers, rate inputs,
	// the protected handoff and invoice decisions stay MVC-only in P0. Nothing here is under ADP (decision 44); the DTOs
	// still omit the agency's FEIN / UEI / FI$Cal values (readiness reports presence, not the value).

	#region Access and readiness

	public class CalOesMarsAccessResult : StandardApiResponseV4Base
	{
		public CalOesMarsAccessData Data { get; set; }
	}

	public class CalOesMarsAccessData
	{
		/// <summary>CostRecovery.CalOesMars entitlement (paid add-on + flag).</summary>
		public bool Enabled { get; set; }
		public bool CanView { get; set; }
		public bool CanManage { get; set; }
		public bool CanSubmit { get; set; }
		public bool CanReconcile { get; set; }
		public string AuthorityProfileCode { get; set; }
		public string PortalUrl { get; set; }
	}

	public class CalOesMarsReadinessResult : StandardApiResponseV4Base
	{
		public CalOesMarsReadinessData Data { get; set; }
	}

	public class CalOesMarsReadinessData
	{
		public DateTime AsOf { get; set; }
		public string AuthorityProfileCode { get; set; }
		public bool AuthorityProfileCurrent { get; set; }
		public bool IsReady { get; set; }
		public bool HasAgencyProfile { get; set; }
		public string MacsDesignator { get; set; }
		public bool HasFein { get; set; }
		public bool HasUei { get; set; }
		public bool HasFiscalSupplier { get; set; }
		public DateTime? AgencyVerifiedOn { get; set; }
		public int ResourceProfiles { get; set; }
		public int ResourceMismatches { get; set; }
		public int CurrentRateProfiles { get; set; }
		public int CurrentAgreements { get; set; }
		public int OpenWorkItems { get; set; }
		public int ReturnedWorkItems { get; set; }
		public int InvoicesAwaitingLocalApproval { get; set; }
		public List<CalOesMarsReadinessItemData> Items { get; set; } = new List<CalOesMarsReadinessItemData>();
	}

	public class CalOesMarsReadinessItemData
	{
		public string Key { get; set; }
		public int Severity { get; set; }
		public string MessageKey { get; set; }
		public string Detail { get; set; }
		public string Area { get; set; }
	}

	#endregion

	#region Work items

	public class CalOesMarsQueueResult : StandardApiResponseV4Base
	{
		public List<CalOesMarsQueueItemData> Data { get; set; } = new List<CalOesMarsQueueItemData>();
	}

	public class CalOesMarsQueueItemData
	{
		public string Id { get; set; }
		public string DeploymentId { get; set; }
		public string DeploymentName { get; set; }
		public int RecordType { get; set; }
		public string RecordTypeName { get; set; }
		public int LocalState { get; set; }
		public string LocalStateName { get; set; }
		public string IncidentNumber { get; set; }
		public string RequestNumber { get; set; }
		public string MarsRecordId { get; set; }
		public string ObservedExternalStatus { get; set; }
		public DateTime? ObservedOn { get; set; }
		public int ErrorCount { get; set; }
		public int WarningCount { get; set; }
		public int AgeDays { get; set; }
		public bool IsMine { get; set; }
		/// <summary>Managers only; null for rostered members.</summary>
		public decimal? ExpectedTotal { get; set; }
		public DateTime AddedOn { get; set; }
		public DateTime? UpdatedOn { get; set; }
	}

	public class CalOesMarsWorkItemResult : StandardApiResponseV4Base
	{
		public CalOesMarsWorkItemData Data { get; set; }
	}

	public class CalOesMarsWorkItemData
	{
		public string Id { get; set; }
		public string DeploymentId { get; set; }
		public string DeploymentName { get; set; }
		public string RmsExternalOrderId { get; set; }
		public string RmsExternalOrderFillId { get; set; }
		public int RecordType { get; set; }
		public string RecordTypeName { get; set; }
		public int LocalState { get; set; }
		public string LocalStateName { get; set; }
		public string MarsRecordId { get; set; }
		public string ObservedExternalStatus { get; set; }
		public DateTime? ObservedOn { get; set; }
		public string CorrectionComment { get; set; }
		public string AuthorityProfileCode { get; set; }
		public bool IsLocallyEditable { get; set; }
		public bool IsExternal { get; set; }
		public string SupersedesWorkItemId { get; set; }
		public CalOesMarsF42Snapshot F42 { get; set; }
		public CalOesMarsExpenseClaimSnapshot ExpenseClaim { get; set; }
		public CalOesMarsValidationResult Validation { get; set; }
		/// <summary>Managers only.</summary>
		public decimal? ExpectedTotal { get; set; }
		public List<CalOesMarsLineData> Lines { get; set; } = new List<CalOesMarsLineData>();
		public int RowVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public DateTime? UpdatedOn { get; set; }
	}

	public class CalOesMarsLineData
	{
		public string Id { get; set; }
		public int LineKind { get; set; }
		public string LineKindName { get; set; }
		public string SubjectId { get; set; }
		public string SubjectName { get; set; }
		public DateTime? LineDate { get; set; }
		public decimal Quantity { get; set; }
		public string Unit { get; set; }
		public decimal Rate { get; set; }
		public decimal ExpectedAmount { get; set; }
		public decimal? ApprovedAmount { get; set; }
		public decimal? PaidAmount { get; set; }
		public int EligibilityState { get; set; }
		public string EligibilityReason { get; set; }
	}

	public class CalOesMarsValidationApiResult : StandardApiResponseV4Base
	{
		public CalOesMarsValidationResult Data { get; set; }
	}

	public class CalOesMarsReimbursementApiResult : StandardApiResponseV4Base
	{
		public CalOesMarsReimbursementData Data { get; set; }
	}

	public class CalOesMarsReimbursementData
	{
		public decimal ExpectedTotal { get; set; }
		public decimal UncertainTotal { get; set; }
		public List<CalOesMarsLineData> Lines { get; set; } = new List<CalOesMarsLineData>();
		public List<CalOesMarsValidationIssue> Exceptions { get; set; } = new List<CalOesMarsValidationIssue>();
	}

	#endregion

	#region Inputs

	public class BuildF42Input
	{
		public string DeploymentId { get; set; }
		public string RmsExternalOrderFillId { get; set; }
	}

	public class BuildExpenseClaimInput
	{
		public string DeploymentId { get; set; }
		public string F42WorkItemId { get; set; }
	}

	public class SaveF42Input
	{
		public string WorkItemId { get; set; }
		public CalOesMarsF42Snapshot Snapshot { get; set; }
	}

	public class SaveExpenseClaimInput
	{
		public string WorkItemId { get; set; }
		public CalOesMarsExpenseClaimSnapshot Snapshot { get; set; }
	}

	public class CalOesMarsObservationInput
	{
		public string WorkItemId { get; set; }
		public string ExternalId { get; set; }
		public string ExternalStatus { get; set; }
		public DateTime? ObservedOn { get; set; }
		public string Comment { get; set; }
		public string ArtifactChecksum { get; set; }
	}

	#endregion
}
