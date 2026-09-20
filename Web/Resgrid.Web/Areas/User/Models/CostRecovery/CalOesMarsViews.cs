using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.CostRecovery.CalOesMars;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;

namespace Resgrid.Web.Areas.User.Models.CostRecovery
{
	// Workforce & Business Operations plan, Phase C-M3 (C6): the five Cal OES MARS screens — readiness dashboard, annual
	// rate workspace, incident action queue, F-42 / expense editor with handoff, and invoice / payment reconciliation —
	// plus the agency, F-5 resource and agreement sub-pages the dashboard links to.

	public class CalOesMarsPageView
	{
		public bool IsManager { get; set; }
		public bool CanSubmit { get; set; }
		public bool CanReconcile { get; set; }
		public string AuthorityProfileCode { get; set; }
		public string PortalUrl { get; set; }
		public string Message { get; set; }
		public bool SaveSuccess { get; set; }
	}

	public class CalOesMarsDashboardView : CalOesMarsPageView
	{
		public CalOesMarsReadiness Readiness { get; set; }
		public CalOesMarsAuthorityProfile Authority { get; set; }
		public DateTime AsOf { get; set; }
	}

	public class CalOesMarsAgencyView : CalOesMarsPageView
	{
		public CalOesMarsAgencyProfile Agency { get; set; } = new CalOesMarsAgencyProfile();
	}

	public class CalOesMarsResourcesView : CalOesMarsPageView
	{
		public List<CalOesMarsResourceProfile> Resources { get; set; } = new List<CalOesMarsResourceProfile>();
		public List<SelectListItem> Units { get; set; } = new List<SelectListItem>();
		public CalOesMarsResourceProfile Editing { get; set; }
		public IReadOnlyList<string> ResourceTypes { get; set; } = Array.Empty<string>();
	}

	public class CalOesMarsRatesView : CalOesMarsPageView
	{
		public List<CalOesMarsRateProfile> Profiles { get; set; } = new List<CalOesMarsRateProfile>();
		public int? Year { get; set; }
	}

	public class CalOesMarsRateEditView : CalOesMarsPageView
	{
		public CalOesMarsRateProfile Profile { get; set; } = new CalOesMarsRateProfile();
		public bool IsNew => string.IsNullOrWhiteSpace(Profile?.CalOesMarsRateProfileId);
		public string LinesJson { get; set; }
		public string InputsJson { get; set; }
		public CalOesMarsAdministrativeRateDraft AdministrativeDraft { get; set; }
		public IReadOnlyList<string> Classifications { get; set; } = Array.Empty<string>();
		public IReadOnlyList<string> ResourceTypes { get; set; } = Array.Empty<string>();
		public List<CalOesMarsRateProfileStatuses> NextStatuses { get; set; } = new List<CalOesMarsRateProfileStatuses>();
	}

	public class CalOesMarsAgreementsView : CalOesMarsPageView
	{
		public List<CalOesMarsAgreementSnapshot> Agreements { get; set; } = new List<CalOesMarsAgreementSnapshot>();
		public CalOesMarsAgreementSnapshot Editing { get; set; }
		public IReadOnlyList<string> Classifications { get; set; } = Array.Empty<string>();
	}

	public class CalOesMarsQueueView : CalOesMarsPageView
	{
		public List<CalOesMarsQueueItem> Items { get; set; } = new List<CalOesMarsQueueItem>();
		public int? TypeFilter { get; set; }
		public List<Deployment> CostRecoveryDeployments { get; set; } = new List<Deployment>();
	}

	public class CalOesMarsWorkItemView : CalOesMarsPageView
	{
		public CalOesMarsWorkItem Item { get; set; }
		public CalOesMarsF42Snapshot F42 { get; set; }
		public CalOesMarsExpenseClaimSnapshot Expense { get; set; }
		public CalOesMarsInvoiceSnapshot Invoice { get; set; }
		public CalOesMarsValidationResult Validation { get; set; }
		public CalOesMarsAuthorityProfile Authority { get; set; }
		public List<DeploymentAttachment> Attachments { get; set; } = new List<DeploymentAttachment>();
		public List<CalOesMarsWorkItem> Related { get; set; } = new List<CalOesMarsWorkItem>();
		public bool IsRostered { get; set; }
		public bool CanEdit { get; set; }
		public string SnapshotJson { get; set; }
		public IReadOnlyList<string> Classifications { get; set; } = Array.Empty<string>();
	}

	public class CalOesMarsHandoffView : CalOesMarsPageView
	{
		public CalOesMarsWorkItem Item { get; set; }
		public CalOesMarsHandoffManifest Manifest { get; set; }
	}

	public class CalOesMarsReconciliationView : CalOesMarsPageView
	{
		public List<CalOesMarsWorkItem> Invoices { get; set; } = new List<CalOesMarsWorkItem>();
		public List<CalOesMarsWorkItem> Coverable { get; set; } = new List<CalOesMarsWorkItem>();
		public Dictionary<string, string> DeploymentNames { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	public class CalOesMarsInvoiceView : CalOesMarsPageView
	{
		public CalOesMarsInvoiceReconciliation Reconciliation { get; set; }
	}

	#region Form inputs

	public sealed class CalOesMarsObservationInput
	{
		public string ExternalId { get; set; }
		public string ExternalStatus { get; set; }
		public DateTime? ObservedOn { get; set; }
		public string Comment { get; set; }
		public string ArtifactChecksum { get; set; }
		public int? ArtifactAttachmentId { get; set; }
	}

	public sealed class CalOesMarsInvoiceInput
	{
		public string DeploymentId { get; set; }
		public string MarsInvoiceId { get; set; }
		public DateTime? InvoiceDate { get; set; }
		public decimal InvoicedTotal { get; set; }
		public string PayingEntity { get; set; }
		public string ExternalStatus { get; set; }
		public DateTime? ObservedOn { get; set; }
		public List<string> CoveredWorkItemIds { get; set; } = new List<string>();
		public string Comment { get; set; }
	}

	public sealed class CalOesMarsPaymentInput
	{
		public decimal PaidTotal { get; set; }
		public DateTime PaidOn { get; set; }
		public string PaymentReference { get; set; }
		public string PayingEntityStatus { get; set; }
		public string Comment { get; set; }
	}

	#endregion
}
