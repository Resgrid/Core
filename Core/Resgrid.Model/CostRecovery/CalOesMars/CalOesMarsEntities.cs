using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.CostRecovery.CalOesMars
{
	// Workforce & Business Operations plan, Phase C-M3 (C1/C11; registry M0219): the Cal OES MARS shadow tables. They
	// are local mirrors of an external system of record — Resgrid prepares, validates, estimates and reconciles; Cal OES
	// MARS accepts, invoices and pays. No portal credential, MFA token or browser session material is stored anywhere here.
	// Nothing in these tables is under Advanced Data Protection (decision 44): every value is either printed on a claim
	// the paying entity reads or is the department's own public agency information (the marker columns were dropped
	// before release on 2026-09-20).

	/// <summary>The department's MARS agency record (one per department).</summary>
	public class CalOesMarsAgencyProfile : IEntity
	{
		[Required]
		public string CalOesMarsAgencyProfileId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		/// <summary>The reviewed <see cref="CalOesMarsAuthorityProfile"/> this row was prepared against.</summary>
		public string AuthorityProfileCode { get; set; }
		/// <summary>The Cal OES MACS agency designator (e.g. "XLA" style three-letter identifier).</summary>
		public string MacsDesignator { get; set; }
		public string AgencyName { get; set; }
		/// <summary>Cal OES agency category (city, county, fire district, …).</summary>
		public string AgencyCategory { get; set; }
		public string ContactName { get; set; }
		public string ContactPhone { get; set; }
		public string ContactEmail { get; set; }
		public string Address { get; set; }
		/// <summary>Federal employer identification number as entered for MARS; printed on the claim, so stored as data.</summary>
		public string FeinReference { get; set; }
		/// <summary>SAM.gov unique entity identifier.</summary>
		public string UeiReference { get; set; }
		/// <summary>SAM registration reference / expiry note.</summary>
		public string SamReference { get; set; }
		/// <summary>FI$Cal supplier id.</summary>
		public string FiscalSupplierReference { get; set; }
		/// <summary>The department's MARS portal role (Primary / Secondary) — a reference, never a credential.</summary>
		public string PortalAccountRole { get; set; }
		public string PortalAccountReference { get; set; }
		public DateTime? VerifiedOn { get; set; }
		public string VerifiedByUserId { get; set; }
		public bool IsActive { get; set; } = true;
		public int RowVersion { get; set; } = 1;
		public string SourceArtifact { get; set; }
		public string SourceChecksum { get; set; }
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "CalOesMarsAgencyProfiles";
		[NotMapped] public string IdName => "CalOesMarsAgencyProfileId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => CalOesMarsAgencyProfileId; set => CalOesMarsAgencyProfileId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>F-5 resource-inventory crosswalk: one Resgrid Unit / asset / external resource to its MARS identity. Never a live status master.</summary>
	public class CalOesMarsResourceProfile : IEntity
	{
		[Required]
		public string CalOesMarsResourceProfileId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		/// <summary><see cref="CalOesMarsSubjectTypes"/>.</summary>
		public int SubjectType { get; set; }
		public int? UnitId { get; set; }
		public string InventoryAssetId { get; set; }
		public string ExternalResourceName { get; set; }
		/// <summary>The identifier MARS / F-5 shows for this resource once accepted.</summary>
		public string MarsResourceId { get; set; }
		/// <summary>Pinned resource type code (e.g. "Type 1 Engine").</summary>
		public string ResourceType { get; set; }
		public string ResourceKind { get; set; }
		public string CodeScheme { get; set; }
		public string UnitDesignator { get; set; }
		public string LicensePlate { get; set; }
		public string Vin { get; set; }
		public string SerialNumber { get; set; }
		/// <summary><see cref="CalOesMarsOwnerships"/>.</summary>
		public int Ownership { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public string ObservedExternalStatus { get; set; }
		public DateTime? ObservedOn { get; set; }
		/// <summary><see cref="CalOesMarsReviewStates"/>.</summary>
		public int ReviewState { get; set; }
		public int RowVersion { get; set; } = 1;
		public string SourceArtifact { get; set; }
		public string SourceChecksum { get; set; }
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string SubjectName { get; set; }
		public bool IsCurrent(DateTime asOf) => (!EffectiveOn.HasValue || EffectiveOn.Value.Date <= asOf.Date) && (!ExpiresOn.HasValue || ExpiresOn.Value.Date >= asOf.Date);

		[NotMapped] public string TableName => "CalOesMarsResourceProfiles";
		[NotMapped] public string IdName => "CalOesMarsResourceProfileId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => CalOesMarsResourceProfileId; set => CalOesMarsResourceProfileId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "SubjectName" };
	}

	/// <summary>An annual rate submission snapshot (Salary Survey, Attachment A, Administrative Rate, Rate Letter, Special Equipment) with its lines.</summary>
	public class CalOesMarsRateProfile : IEntity
	{
		[Required]
		public string CalOesMarsRateProfileId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public int SubmissionYear { get; set; }
		/// <summary><see cref="CalOesMarsSubmissionTypes"/>.</summary>
		public int SubmissionType { get; set; }
		/// <summary><see cref="CalOesMarsRateProfileStatuses"/>.</summary>
		public int Status { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		/// <summary>The agency accepted the Cal OES base rate instead of submitting its own survey.</summary>
		public bool BaseRateAccepted { get; set; }
		/// <summary><see cref="CalOesMarsAdministrativeRateMethods"/>.</summary>
		public int AdministrativeRateMethod { get; set; }
		/// <summary>Administrative rate as a percentage (e.g. 10.0000).</summary>
		public decimal? AdministrativeRateValue { get; set; }
		public string AuthorityProfileCode { get; set; }
		public string SourceUrl { get; set; }
		public DateTime? SourceDate { get; set; }
		public DateTime? SignedOn { get; set; }
		public string SignedByName { get; set; }
		public string ObservedExternalStatus { get; set; }
		public DateTime? ObservedOn { get; set; }
		public int RowVersion { get; set; } = 1;
		public string SourceArtifact { get; set; }
		public string SourceChecksum { get; set; }
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public List<CalOesMarsRateLine> Lines { get; set; } = new List<CalOesMarsRateLine>();
		[NotMapped] public List<CalOesMarsAdministrativeRateInput> AdministrativeInputs { get; set; } = new List<CalOesMarsAdministrativeRateInput>();
		[NotMapped] public bool IsEditable => Status is (int)CalOesMarsRateProfileStatuses.Draft or (int)CalOesMarsRateProfileStatuses.Reviewed;
		public bool IsCurrent(DateTime asOf) => (!EffectiveOn.HasValue || EffectiveOn.Value.Date <= asOf.Date) && (!ExpiresOn.HasValue || ExpiresOn.Value.Date >= asOf.Date) && Status != (int)CalOesMarsRateProfileStatuses.Superseded;

		[NotMapped] public string TableName => "CalOesMarsRateProfiles";
		[NotMapped] public string IdName => "CalOesMarsRateProfileId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => CalOesMarsRateProfileId; set => CalOesMarsRateProfileId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "Lines", "AdministrativeInputs", "IsEditable" };
	}

	public class CalOesMarsRateLine : IEntity
	{
		[Required]
		public string CalOesMarsRateLineId { get; set; }
		[Required]
		public string CalOesMarsRateProfileId { get; set; }
		public int DepartmentId { get; set; }
		/// <summary><see cref="CalOesMarsRateLineKinds"/>.</summary>
		public int LineKind { get; set; }
		/// <summary>Personnel classification (rank) code for salary lines.</summary>
		public string ClassificationCode { get; set; }
		/// <summary>Apparatus / support / equipment resource code for equipment lines.</summary>
		public string ResourceCode { get; set; }
		public string FemaCode { get; set; }
		public string Description { get; set; }
		/// <summary><see cref="CalOesMarsRateBases"/>.</summary>
		public int Basis { get; set; }
		public decimal? StraightRate { get; set; }
		public decimal? OvertimeRate { get; set; }
		public bool IncludesWorkersComp { get; set; }
		public bool IncludesUnemploymentInsurance { get; set; }
		public bool PortalToPortalEligible { get; set; }
		public bool OvertimeEligible { get; set; }
		/// <summary><see cref="CalOesMarsRateAuthorities"/>.</summary>
		public int Authority { get; set; }
		/// <summary>Versions of the inputs (pay data, source documents) this line was derived from.</summary>
		public string SourceInputVersions { get; set; }
		public string ObservedExternalStatus { get; set; }
		public int SortOrder { get; set; }
		public int RowVersion { get; set; } = 1;
		public string SourceArtifact { get; set; }
		public string SourceChecksum { get; set; }
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "CalOesMarsRateLines";
		[NotMapped] public string IdName => "CalOesMarsRateLineId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => CalOesMarsRateLineId; set => CalOesMarsRateLineId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A prior-year actual-cost input to the administrative (indirect) rate worksheet. Actuals, never budgets.</summary>
	public class CalOesMarsAdministrativeRateInput : IEntity
	{
		[Required]
		public string CalOesMarsAdministrativeRateInputId { get; set; }
		[Required]
		public string CalOesMarsRateProfileId { get; set; }
		public int DepartmentId { get; set; }
		public int FiscalYear { get; set; }
		public string FunctionCode { get; set; }
		public string CategoryCode { get; set; }
		public string CategoryProfileVersion { get; set; }
		/// <summary><see cref="CalOesMarsCostClassifications"/>.</summary>
		public int Classification { get; set; }
		/// <summary>Stored as text (the column is nvarchar(max)); parsed with <see cref="Amount"/>.</summary>
		public string ActualAmount { get; set; }
		public string SourceSystem { get; set; }
		public string SourceLine { get; set; }
		public int InputVersion { get; set; } = 1;
		/// <summary>Cost already billed directly to an incident — excluded from the indirect pool.</summary>
		public bool IncidentDirectExclusion { get; set; }
		/// <summary>Flagged as possibly counted twice (a resource billed directly and included in a pool).</summary>
		public bool DoubleCountMarker { get; set; }
		/// <summary><see cref="CalOesMarsInputReviewStatuses"/>.</summary>
		public int ReviewStatus { get; set; }
		public string ReviewReason { get; set; }
		public int RowVersion { get; set; } = 1;
		public string SourceArtifact { get; set; }
		public string SourceChecksum { get; set; }
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public decimal Amount => decimal.TryParse(ActualAmount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0m;

		[NotMapped] public string TableName => "CalOesMarsAdministrativeRateInputs";
		[NotMapped] public string IdName => "CalOesMarsAdministrativeRateInputId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => CalOesMarsAdministrativeRateInputId; set => CalOesMarsAdministrativeRateInputId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "Amount" };
	}

	/// <summary>An approved MOU / MOA / GBR compensation method for a classification, selected as of initial dispatch.</summary>
	public class CalOesMarsAgreementSnapshot : IEntity
	{
		[Required]
		public string CalOesMarsAgreementSnapshotId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		/// <summary>Null = applies to every classification without a more specific agreement.</summary>
		public string ClassificationCode { get; set; }
		public string ClassificationTitle { get; set; }
		/// <summary><see cref="CalOesMarsDocumentKinds"/>.</summary>
		public int DocumentKind { get; set; }
		/// <summary><see cref="CalOesMarsCompensationMethods"/>.</summary>
		public int CompensationMethod { get; set; }
		/// <summary><see cref="CalOesMarsOvertimeMethods"/>.</summary>
		public int OvertimeMethod { get; set; }
		public DateTime? StartOn { get; set; }
		public DateTime? EndOn { get; set; }
		public string ExternalApprovalStatus { get; set; }
		public DateTime? ObservedOn { get; set; }
		public int? AttachmentId { get; set; }
		public string AttachmentChecksum { get; set; }
		public int RowVersion { get; set; } = 1;
		public string SourceArtifact { get; set; }
		public string SourceChecksum { get; set; }
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		public bool CoversDate(DateTime asOf) => (!StartOn.HasValue || StartOn.Value.Date <= asOf.Date) && (!EndOn.HasValue || EndOn.Value.Date >= asOf.Date);

		[NotMapped] public string TableName => "CalOesMarsAgreementSnapshots";
		[NotMapped] public string IdName => "CalOesMarsAgreementSnapshotId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => CalOesMarsAgreementSnapshotId; set => CalOesMarsAgreementSnapshotId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A local mirror of one MARS record (F-42, expense claim, generated invoice, annual submission…). The snapshot JSON
	/// carries the prepared document; the external ids / statuses are what a MARS manager observed in the portal.
	/// </summary>
	public class CalOesMarsWorkItem : IEntity
	{
		[Required]
		public string CalOesMarsWorkItemId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string DeploymentId { get; set; }
		public string RmsExternalOrderId { get; set; }
		public string RmsExternalOrderFillId { get; set; }
		/// <summary><see cref="CalOesMarsRecordTypes"/>.</summary>
		public int RecordType { get; set; }
		/// <summary><see cref="CalOesMarsLocalStates"/>.</summary>
		public int LocalState { get; set; }
		public string MarsRecordId { get; set; }
		public string MarsInvoiceId { get; set; }
		public string ObservedExternalStatus { get; set; }
		public DateTime? ObservedOn { get; set; }
		/// <summary><see cref="CalOesMarsObservationSources"/>.</summary>
		public string ObservedSource { get; set; }
		/// <summary>The reviewer's comment when MARS returned the record for agency review.</summary>
		public string CorrectionComment { get; set; }
		public string AuthorityProfileCode { get; set; }
		public string RateProfileVersion { get; set; }
		public string AgreementSnapshotId { get; set; }
		/// <summary>Typed snapshot (<see cref="CalOesMarsF42Snapshot"/> / <see cref="CalOesMarsExpenseClaimSnapshot"/> / <see cref="CalOesMarsInvoiceSnapshot"/>).</summary>
		public string SnapshotJson { get; set; }
		public string ValidationSummaryJson { get; set; }
		public string SubmittedByUserId { get; set; }
		public DateTime? SubmittedOn { get; set; }
		public string ApprovedByUserId { get; set; }
		public DateTime? ApprovedOn { get; set; }
		public string RejectedByUserId { get; set; }
		public DateTime? RejectedOn { get; set; }
		public DateTime? PaidOn { get; set; }
		public decimal? ExpectedTotal { get; set; }
		public decimal? ApprovedTotal { get; set; }
		public decimal? PaidTotal { get; set; }
		public string PaymentReference { get; set; }
		/// <summary>The earlier revision this row replaced (returned-for-review corrections, redispatch).</summary>
		public string SupersedesWorkItemId { get; set; }
		public int RowVersion { get; set; } = 1;
		public string SourceArtifact { get; set; }
		public string SourceChecksum { get; set; }
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public List<CalOesMarsReimbursementLine> Lines { get; set; } = new List<CalOesMarsReimbursementLine>();
		[NotMapped] public string DeploymentName { get; set; }
		[NotMapped] public bool IsLocallyEditable => LocalState is (int)CalOesMarsLocalStates.Draft or (int)CalOesMarsLocalStates.NeedsReview or (int)CalOesMarsLocalStates.ReadyForPortal or (int)CalOesMarsLocalStates.ReturnedForAgencyReview;
		[NotMapped] public bool IsExternal => LocalState >= (int)CalOesMarsLocalStates.SubmittedExternal;

		[NotMapped] public string TableName => "CalOesMarsWorkItems";
		[NotMapped] public string IdName => "CalOesMarsWorkItemId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => CalOesMarsWorkItemId; set => CalOesMarsWorkItemId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "Lines", "DeploymentName", "IsLocallyEditable", "IsExternal" };
	}

	/// <summary>An immutable expected-reimbursement line (decision 37: reimbursement, not revenue, not cost).</summary>
	public class CalOesMarsReimbursementLine : IEntity
	{
		[Required]
		public string CalOesMarsReimbursementLineId { get; set; }
		[Required]
		public string CalOesMarsWorkItemId { get; set; }
		public int DepartmentId { get; set; }
		public string DeploymentId { get; set; }
		public DateTime? LineDate { get; set; }
		/// <summary><see cref="CalOesMarsLineKinds"/>.</summary>
		public int LineKind { get; set; }
		/// <summary><see cref="Resgrid.Model.Invoicing.DeploymentTimeSubjectTypes"/> for roster subjects.</summary>
		public int? SubjectType { get; set; }
		public string SubjectId { get; set; }
		public string SourceWorkId { get; set; }
		public string SourceExpenseId { get; set; }
		public decimal Quantity { get; set; }
		public string Unit { get; set; }
		public decimal Rate { get; set; }
		public string RateLineId { get; set; }
		public int? RateLineVersion { get; set; }
		public decimal ExpectedAmount { get; set; }
		public decimal? ApprovedAmount { get; set; }
		public decimal? PaidAmount { get; set; }
		/// <summary><see cref="CalOesMarsEligibilityStates"/>.</summary>
		public int EligibilityState { get; set; }
		public string EligibilityReason { get; set; }
		public string SourceVersions { get; set; }
		public int SortOrder { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string SubjectName { get; set; }

		[NotMapped] public string TableName => "CalOesMarsReimbursementLines";
		[NotMapped] public string IdName => "CalOesMarsReimbursementLineId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => CalOesMarsReimbursementLineId; set => CalOesMarsReimbursementLineId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "SubjectName" };
	}
}
