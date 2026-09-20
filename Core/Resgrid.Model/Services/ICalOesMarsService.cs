using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.CostRecovery.CalOesMars;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Cal OES MARS / CFAA cost recovery (Workforce &amp; Business Operations plan, C4 and C11; decisions 36-38).
	/// Resgrid owns preparation, local approval, expected-reimbursement calculation, evidence, handoff and
	/// reconciliation; Cal OES MARS owns acceptance, correction queues, generated invoices and paid status. Nothing
	/// here writes to MARS: the P0 gateway is manual (<see cref="ICalOesMarsExternalGateway"/>), and only a recorded
	/// observation advances an external state. Callers authorize (permission 79 for management; rostered members for
	/// their own incident-bound drafts).
	/// </summary>
	public interface ICalOesMarsService
	{
		#region Readiness and agency

		Task<CalOesMarsReadiness> GetAgencyReadinessAsync(int departmentId, DateTime? dispatchOn = null);
		Task<CalOesMarsAgencyProfile> GetAgencyProfileAsync(int departmentId);
		Task<CalOesMarsAgencyProfile> SaveAgencyProfileAsync(CalOesMarsAgencyProfile profile, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<CalOesMarsAgencyProfile> MarkAgencyVerifiedAsync(int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		#endregion

		#region F-5 resource inventory crosswalk

		Task<List<CalOesMarsResourceProfile>> GetResourceProfilesAsync(int departmentId);
		Task<CalOesMarsResourceProfile> GetResourceProfileAsync(string resourceProfileId, int departmentId);
		/// <summary>Maps the selected units to draft crosswalk rows (designator, plate, VIN, ownership) without touching Unit state; existing rows are returned as-is.</summary>
		Task<List<CalOesMarsResourceProfile>> BuildResourceInventoryF5DraftAsync(int departmentId, IEnumerable<int> unitIds, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<CalOesMarsResourceProfile> SaveResourceProfileAsync(CalOesMarsResourceProfile profile, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<CalOesMarsResourceProfile> RecordResourceObservationAsync(string resourceProfileId, int departmentId, CalOesMarsExternalObservation observation, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteResourceProfileAsync(string resourceProfileId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		#endregion

		#region Annual rate profiles

		Task<List<CalOesMarsRateProfile>> GetRateProfilesAsync(int departmentId, int? submissionYear = null);
		Task<CalOesMarsRateProfile> GetRateProfileAsync(string rateProfileId, int departmentId);
		Task<CalOesMarsRateProfile> SaveRateProfileAsync(CalOesMarsRateProfile profile, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Upserts the profile's lines by id (missing ids are deleted). Refused once the profile is signed or submitted.</summary>
		Task<CalOesMarsRateProfile> SaveRateLinesAsync(string rateProfileId, int departmentId, List<CalOesMarsRateLine> lines, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<CalOesMarsRateProfile> SaveAdministrativeInputsAsync(string rateProfileId, int departmentId, List<CalOesMarsAdministrativeRateInput> inputs, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Allowable indirect ÷ allowable direct from the reviewed inputs, compared with the de-minimis option; records the method chosen. Blocks on unresolved double-count flags.</summary>
		Task<CalOesMarsAdministrativeRateDraft> BuildAdministrativeRateDraftAsync(string rateProfileId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Salary Survey / Attachment A draft from Phase E pay data (plan C-M3 §"Salary Survey/Attachment A draft"): the mean of the current approved individual hourly rates per reviewed MARS classification (plus components paid for each overtime hour) becomes the straight / overtime rate of the matching line. Aggregates only — never a pay-range midpoint, never an individual; a review aid the authorized representative still signs in MARS.</summary>
		Task<CalOesMarsSalarySurveyDraft> BuildSalarySurveyDraftAsync(string rateProfileId, int departmentId, DateTime asOf, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<CalOesMarsRateProfile> SetRateProfileStatusAsync(string rateProfileId, int departmentId, CalOesMarsRateProfileStatuses status, string signedByName, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<CalOesMarsRateProfile> RecordRateProfileObservationAsync(string rateProfileId, int departmentId, CalOesMarsExternalObservation observation, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteRateProfileAsync(string rateProfileId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		#endregion

		#region Agreements

		Task<List<CalOesMarsAgreementSnapshot>> GetAgreementsAsync(int departmentId);
		Task<CalOesMarsAgreementSnapshot> GetAgreementAsync(string agreementSnapshotId, int departmentId);
		Task<CalOesMarsAgreementSnapshot> SaveAgreementAsync(CalOesMarsAgreementSnapshot agreement, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<CalOesMarsAgreementSnapshot> RecordAgreementObservationAsync(string agreementSnapshotId, int departmentId, CalOesMarsExternalObservation observation, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteAgreementAsync(string agreementSnapshotId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>The agreement covering the classification as of initial dispatch (specific classification first, then the department-wide one).</summary>
		Task<CalOesMarsAgreementSnapshot> SelectAgreementAsync(int departmentId, string classificationCode, DateTime dispatchOn);

		#endregion

		#region Work items (F-42, expense claims, invoices)

		Task<List<CalOesMarsQueueItem>> GetActionQueueAsync(int departmentId, string userId, bool managerScope);
		Task<List<CalOesMarsWorkItem>> GetWorkItemsForDeploymentAsync(string deploymentId, int departmentId);
		Task<CalOesMarsWorkItem> GetWorkItemAsync(string workItemId, int departmentId);
		/// <summary>Whether the user may open the item without permission 79: rostered on its deployment.</summary>
		Task<bool> IsRosteredForWorkItemAsync(string workItemId, int departmentId, string userId);

		/// <summary>One F-42 per ordered resource / request (the fill); a redispatch supersedes the earlier item. Time facts pre-fill from the DTRs; a DTR is not an F-42.</summary>
		Task<CalOesMarsWorkItem> BuildF42DraftAsync(string deploymentId, int departmentId, string rmsExternalOrderFillId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Groups the deployment's dated expenses for the resource / F-42 (or the travel-only path when no F-42 exists).</summary>
		Task<CalOesMarsWorkItem> BuildExpenseClaimDraftAsync(string deploymentId, int departmentId, string f42WorkItemId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Saves the editable parts of a local snapshot (comments, signatures, rotations, documentation-only, attachments). Refused once external.</summary>
		Task<CalOesMarsWorkItem> SaveF42SnapshotAsync(string workItemId, int departmentId, CalOesMarsF42Snapshot snapshot, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<CalOesMarsWorkItem> SaveExpenseSnapshotAsync(string workItemId, int departmentId, CalOesMarsExpenseClaimSnapshot snapshot, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		/// <summary>Field-by-field errors / warnings against the pinned checklist; a clean run moves Draft/NeedsReview → ReadyForPortal, errors move it to NeedsReview.</summary>
		Task<CalOesMarsValidationResult> ValidateForPortalAsync(string workItemId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Immutable expected lines from the profile effective at initial dispatch (decision 37). Replaces the item's earlier lines while it is local.</summary>
		Task<CalOesMarsReimbursementResult> CalculateExpectedReimbursementAsync(string workItemId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>The no-store side-by-side copy view plus the portal link. Requires an explicit actor attestation; never marks the item submitted.</summary>
		Task<CalOesMarsHandoffManifest> OpenPortalHandoffAsync(string workItemId, int departmentId, bool attested, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Evidence packet zip (manifest JSON, printable snapshot, supporting attachments). Labelled "not an accepted MARS import file".</summary>
		Task<byte[]> BuildEvidencePacketAsync(string workItemId, int departmentId, string userId);
		Task<string> RenderWorkItemHtmlAsync(string workItemId, int departmentId);

		Task<CalOesMarsWorkItem> RecordExternalSubmissionAsync(string workItemId, int departmentId, CalOesMarsExternalObservation observation, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Maps the observed status through the authority profile (Cal OES Review / Agency Review / Approved / Documentation Only). A returned item creates a new local revision.</summary>
		Task<CalOesMarsWorkItem> RecordExternalStatusAsync(string workItemId, int departmentId, CalOesMarsExternalObservation observation, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<CalOesMarsWorkItem> RecordMarsInvoiceAsync(int departmentId, string deploymentId, CalOesMarsInvoiceObservation observation, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<CalOesMarsWorkItem> ApproveOrRejectObservedInvoiceAsync(string invoiceWorkItemId, int departmentId, bool approve, string decisionTitle, string comment, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<CalOesMarsWorkItem> RecordPaymentAsync(string invoiceWorkItemId, int departmentId, CalOesMarsPaymentObservation observation, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<CalOesMarsWorkItem> CloseWorkItemAsync(string workItemId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteWorkItemAsync(string workItemId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Expected vs observed comparison for an invoice item: covered F-42/expense items, their expected totals, the invoiced / paid amounts and the variance.</summary>
		Task<CalOesMarsInvoiceReconciliation> GetInvoiceReconciliationAsync(string invoiceWorkItemId, int departmentId);

		#endregion

		#region Worker 32

		/// <summary>Value-minimized digest per department per day to permission-79 holders: annual deadlines, released resources without an F-42, returned items, invoices awaiting local approval. Never changes a state.</summary>
		Task<int> RunReminderSweepAsync(DateTime asOfUtc, Func<int, Task<bool>> departmentEnabled = null, CancellationToken cancellationToken = default);

		#endregion
	}

	/// <summary>The administrative-rate worksheet result.</summary>
	public sealed class CalOesMarsAdministrativeRateDraft
	{
		public string RateProfileId { get; set; }
		public decimal AllowableDirect { get; set; }
		public decimal AllowableIndirect { get; set; }
		public decimal ExcludedUnallowable { get; set; }
		public decimal ExcludedIncidentDirect { get; set; }
		public decimal? CalculatedPercent { get; set; }
		public decimal DeMinimisPercent { get; set; }
		public int MethodChosen { get; set; }
		public decimal? ChosenPercent { get; set; }
		public List<string> Blockers { get; set; } = new List<string>();
		public bool IsReady => Blockers.Count == 0;
	}

	/// <summary>The Salary Survey draft result (counts and classification means only).</summary>
	public sealed class CalOesMarsSalarySurveyDraft
	{
		public string RateProfileId { get; set; }
		public DateTime AsOf { get; set; }
		public List<CalOesMarsSalarySurveyDraftLine> Classifications { get; set; } = new List<CalOesMarsSalarySurveyDraftLine>();
		public int EmployeesIncluded => Classifications.Sum(c => c.Count);
		public int LinesWritten { get; set; }
		public List<string> UnknownClassifications { get; set; } = new List<string>();
		public List<string> Blockers { get; set; } = new List<string>();
		public bool IsReady => Blockers.Count == 0;
	}

	public sealed class CalOesMarsSalarySurveyDraftLine
	{
		public string ClassificationCode { get; set; }
		public int Count { get; set; }
		public decimal MeanRate { get; set; }
		public decimal MeanOvertimeAdder { get; set; }
		public decimal StraightRate { get; set; }
		public decimal OvertimeRate { get; set; }
		/// <summary>A single-employee classification exposes that person's rate as the mean; the reviewer decides whether to keep it.</summary>
		public bool SingleEmployee => Count == 1;
	}

	public sealed class CalOesMarsInvoiceReconciliation
	{
		public CalOesMarsWorkItem Invoice { get; set; }
		public CalOesMarsInvoiceSnapshot Snapshot { get; set; }
		public List<CalOesMarsWorkItem> CoveredItems { get; set; } = new List<CalOesMarsWorkItem>();
		public decimal ExpectedTotal { get; set; }
		public decimal? InvoicedTotal { get; set; }
		public decimal? PaidTotal { get; set; }
		public decimal? Variance => InvoicedTotal.HasValue ? InvoicedTotal.Value - ExpectedTotal : null;
	}
}
