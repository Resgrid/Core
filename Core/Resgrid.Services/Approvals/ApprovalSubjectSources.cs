using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Approvals;
using Resgrid.Model.CostRecovery.CalOesMars;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Approvals
{
	/// <summary>Shared pieces of the approval subject sources: the canonical hash and the encoded approval document.</summary>
	public static class ApprovalDocuments
	{
		public const string Head = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>body{font-family:Arial,Helvetica,sans-serif;font-size:12px}th,td{border:1px solid #bbb;padding:4px 6px;text-align:left;vertical-align:top}table{border-collapse:collapse}</style></head><body>";

		/// <summary>SHA-256 of the canonical JSON of the approvable fields (status, approval stamps and edit times are never part of it).</summary>
		public static string Hash(object stable) => SignaturePolicy.HashArtifact(JsonConvert.SerializeObject(stable));

		public static string Money(decimal? value) => value?.ToString("0.00", CultureInfo.InvariantCulture);

		public static string Date(DateTime? value) => value?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

		/// <summary>An HTML document of label/value rows; every value is encoded.</summary>
		public static string Build(string title, IEnumerable<(string Label, string Value)> rows, string body = null)
		{
			string E(string v) => WebUtility.HtmlEncode(v ?? string.Empty);
			var html = new StringBuilder(Head).Append("<h1>").Append(E(title)).Append("</h1><table>");
			foreach (var (label, value) in rows)
				html.Append("<tr><th>").Append(E(label)).Append("</th><td>").Append(E(value)).Append("</td></tr>");
			html.Append("</table>");
			if (!string.IsNullOrWhiteSpace(body))
				html.Append("<div style=\"white-space:pre-wrap;margin-top:12px\">").Append(E(body)).Append("</div>");
			return html.Append("</body></html>").ToString();
		}

		public static async Task<bool> SafeAsync(Func<Task<bool>> check, string what)
		{
			try
			{
				return await check();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The " + what + " state could not be read for approvals; approvals stay off.");
				return false;
			}
		}
	}

	/// <summary>
	/// Incident Back Office purchase requisitions (add-on 6). Amount = the estimated amount; submittable while Submitted. The completed
	/// chain moves the requisition to Approved through the ledger (its own separation-of-duties and delegation rules still apply); a
	/// rejection hands it back to Draft so it can be changed and submitted again.
	/// </summary>
	public class PurchaseRequisitionApprovalSource : IApprovalSubjectSource
	{
		private readonly IIncidentLedgerRepository<PurchaseRequisition> _requisitions;
		private readonly IIncidentBackOfficeAccessService _access;
		private readonly Lazy<IIncidentLedgerService<PurchaseRequisition>> _ledger;

		public PurchaseRequisitionApprovalSource(IIncidentLedgerRepository<PurchaseRequisition> requisitions, IIncidentBackOfficeAccessService access,
			Lazy<IIncidentLedgerService<PurchaseRequisition>> ledger)
		{
			_requisitions = requisitions;
			_access = access;
			_ledger = ledger;
		}

		public SignatureSubjectKinds Kind => SignatureSubjectKinds.PurchaseRequisitionApproval;

		public Task<bool> IsAvailableAsync(int departmentId) => ApprovalDocuments.SafeAsync(() => _access.HasActiveAddonAsync(departmentId), "Incident Back Office");

		public async Task<ApprovalSubjectInfo> DescribeAsync(int departmentId, string subjectId)
		{
			var requisition = await _requisitions.GetForDepartmentAsync(departmentId, subjectId);
			return requisition == null || requisition.DepartmentId != departmentId || requisition.IsDeleted ? null : Describe(requisition);
		}

		public static ApprovalSubjectInfo Describe(PurchaseRequisition r)
		{
			var stable = new
			{
				kind = "purchase-requisition/1", id = r.PurchaseRequisitionId.ToLowerInvariant(), r.IncidentActivationId, r.RequisitionNumber, r.VendorId, r.IncidentVendorEngagementId,
				r.IncidentBudgetLineId, r.IncidentResourceRequestId, r.Method, r.Description, amount = ApprovalDocuments.Money(r.EstimatedAmount), currency = r.Currency?.ToUpperInvariant(),
				r.Justification, r.ConflictOfInterestAttested
			};
			var title = string.IsNullOrWhiteSpace(r.RequisitionNumber) ? r.Description : r.RequisitionNumber + " " + r.Description;
			return new ApprovalSubjectInfo
			{
				Kind = SignatureSubjectKinds.PurchaseRequisitionApproval,
				SubjectId = r.PurchaseRequisitionId,
				Title = (title ?? string.Empty).Trim(),
				Amount = r.EstimatedAmount,
				Currency = r.Currency,
				ContentSha256 = ApprovalDocuments.Hash(stable),
				RevisionId = r.RequisitionNumber,
				OwnerUserId = r.RequestedByUserId ?? r.AddedByUserId,
				Submittable = r.Status == (int)IncidentRequisitionStatuses.Submitted,
				DocumentHtml = ApprovalDocuments.Build("Purchase requisition " + (r.RequisitionNumber ?? string.Empty), new[]
				{
					("Description", r.Description), ("Estimated amount", ApprovalRouting.AmountText(r.EstimatedAmount, r.Currency)),
					("Method", ((IncidentPurchaseMethods)r.Method).ToString()), ("Justification", r.Justification),
					("Conflict of interest attested", r.ConflictOfInterestAttested ? "Yes" : "No"), ("Split-purchase warning", r.IsSplitPurchaseWarning ? "Yes" : "No")
				})
			};
		}

		public async Task<List<ApprovalSubjectInfo>> GetCandidatesAsync(int departmentId, int take) =>
			(await _requisitions.ListAsync(departmentId, new IncidentLedgerQuery().Where("Status", (int)IncidentRequisitionStatuses.Submitted).Order("AddedOn", true).Page(0, take))
				?? Enumerable.Empty<PurchaseRequisition>()).Where(r => r.DepartmentId == departmentId && !r.IsDeleted).Select(Describe).ToList();

		public async Task OnApprovedAsync(ApprovalRequest approval, string approverUserId, CancellationToken cancellationToken = default)
		{
			var actor = new IncidentActor { DepartmentId = approval.DepartmentId, UserId = approverUserId, IpAddress = "approval-chain", UserAgent = "Resgrid.Approvals" };
			await _ledger.Value.TransitionAsync(actor, approval.SubjectId, (int)IncidentRequisitionStatuses.Approved, null, null, cancellationToken);
		}

		public async Task OnRejectedAsync(ApprovalRequest approval, string userId, string reason, CancellationToken cancellationToken = default)
		{
			var actor = new IncidentActor { DepartmentId = approval.DepartmentId, UserId = userId, IpAddress = "approval-chain", UserAgent = "Resgrid.Approvals" };
			await _ledger.Value.TransitionAsync(actor, approval.SubjectId, (int)IncidentRequisitionStatuses.Draft, reason, null, cancellationToken);
		}
	}

	/// <summary>
	/// Incident Back Office Incident Action Plan versions (add-on 6): the IAP assembly manifest of a period. Submittable while Assembled;
	/// the completed chain approves the plan through the planning service (supersedes earlier versions, snapshots the weather block); a
	/// rejection hands the version back to Draft. No amount.
	/// </summary>
	public class IncidentPlanApprovalSource : IApprovalSubjectSource
	{
		private readonly IIncidentLedgerRepository<IncidentPlanVersion> _plans;
		private readonly IIncidentBackOfficeAccessService _access;
		private readonly Lazy<IIncidentPlanningService> _planning;
		private readonly Lazy<IIncidentLedgerService<IncidentPlanVersion>> _ledger;

		public IncidentPlanApprovalSource(IIncidentLedgerRepository<IncidentPlanVersion> plans, IIncidentBackOfficeAccessService access, Lazy<IIncidentPlanningService> planning,
			Lazy<IIncidentLedgerService<IncidentPlanVersion>> ledger)
		{
			_plans = plans;
			_access = access;
			_planning = planning;
			_ledger = ledger;
		}

		public SignatureSubjectKinds Kind => SignatureSubjectKinds.IncidentPlanApproval;

		public Task<bool> IsAvailableAsync(int departmentId) => ApprovalDocuments.SafeAsync(() => _access.HasActiveAddonAsync(departmentId), "Incident Back Office");

		public async Task<ApprovalSubjectInfo> DescribeAsync(int departmentId, string subjectId)
		{
			var plan = await _plans.GetForDepartmentAsync(departmentId, subjectId);
			return plan == null || plan.DepartmentId != departmentId || plan.IsDeleted ? null : Describe(plan);
		}

		public static ApprovalSubjectInfo Describe(IncidentPlanVersion p)
		{
			var stable = new { kind = "incident-plan/1", id = p.IncidentPlanVersionId.ToLowerInvariant(), p.IncidentActivationId, p.IncidentOperationalPeriodId, p.VersionNumber, p.RecordId, p.ManifestJson, p.DistributionJson, p.PrintCopies };
			var title = "IAP version " + p.VersionNumber.ToString(CultureInfo.InvariantCulture);
			return new ApprovalSubjectInfo
			{
				Kind = SignatureSubjectKinds.IncidentPlanApproval,
				SubjectId = p.IncidentPlanVersionId,
				Title = title,
				ContentSha256 = ApprovalDocuments.Hash(stable),
				RevisionId = p.VersionNumber.ToString(CultureInfo.InvariantCulture),
				OwnerUserId = p.AddedByUserId,
				Submittable = p.Status == (int)IncidentPlanVersionStatuses.Assembled,
				DocumentHtml = ApprovalDocuments.Build(title, new[]
				{
					("Operational period", p.IncidentOperationalPeriodId), ("Version", p.VersionNumber.ToString(CultureInfo.InvariantCulture)), ("Plan record", p.RecordId),
					("Print copies", p.PrintCopies.ToString(CultureInfo.InvariantCulture))
				}, p.ManifestJson)
			};
		}

		public async Task<List<ApprovalSubjectInfo>> GetCandidatesAsync(int departmentId, int take) =>
			(await _plans.ListAsync(departmentId, new IncidentLedgerQuery().Where("Status", (int)IncidentPlanVersionStatuses.Assembled).Order("AddedOn", true).Page(0, take))
				?? Enumerable.Empty<IncidentPlanVersion>()).Where(p => p.DepartmentId == departmentId && !p.IsDeleted).Select(Describe).ToList();

		public async Task OnApprovedAsync(ApprovalRequest approval, string approverUserId, CancellationToken cancellationToken = default)
		{
			var actor = new IncidentActor { DepartmentId = approval.DepartmentId, UserId = approverUserId, IpAddress = "approval-chain", UserAgent = "Resgrid.Approvals" };
			await _planning.Value.ApprovePlanAsync(actor, approval.SubjectId, cancellationToken);
		}

		public async Task OnRejectedAsync(ApprovalRequest approval, string userId, string reason, CancellationToken cancellationToken = default)
		{
			var actor = new IncidentActor { DepartmentId = approval.DepartmentId, UserId = userId, IpAddress = "approval-chain", UserAgent = "Resgrid.Approvals" };
			await _ledger.Value.TransitionAsync(actor, approval.SubjectId, (int)IncidentPlanVersionStatuses.Draft, reason, null, cancellationToken);
		}
	}

	/// <summary>
	/// Business Ops customer invoices (add-on 4, invoicing): approved before they are sent. Amount = the total; submittable while a
	/// draft. The hash covers the customer, dates, currency, totals and every line (status, payments and send stamps excluded), so an
	/// edit after approval voids it. The decision itself needs no write-back: the send path asks the gate.
	/// </summary>
	public class InvoiceApprovalSource : IApprovalSubjectSource
	{
		private readonly IInvoiceRepository _invoices;
		private readonly IInvoiceLineItemRepository _lines;
		private readonly IBusinessOperationsAccessService _access;

		public InvoiceApprovalSource(IInvoiceRepository invoices, IInvoiceLineItemRepository lines, IBusinessOperationsAccessService access)
		{
			_invoices = invoices;
			_lines = lines;
			_access = access;
		}

		public SignatureSubjectKinds Kind => SignatureSubjectKinds.InvoiceApproval;

		public Task<bool> IsAvailableAsync(int departmentId) => ApprovalDocuments.SafeAsync(() => _access.CanUseInvoicingAsync(departmentId), "Business Ops invoicing");

		public async Task<ApprovalSubjectInfo> DescribeAsync(int departmentId, string subjectId)
		{
			var invoice = await _invoices.GetByIdForDepartmentAsync(subjectId, departmentId);
			if (invoice == null || invoice.DepartmentId != departmentId || invoice.IsDeleted)
				return null;
			var lines = (await _lines.GetByInvoiceIdAsync(invoice.InvoiceId, departmentId) ?? Enumerable.Empty<InvoiceLineItem>()).Where(l => l.DepartmentId == departmentId).ToList();
			return Describe(invoice, lines);
		}

		public static ApprovalSubjectInfo Describe(Invoice i, List<InvoiceLineItem> lines)
		{
			var ordered = (lines ?? new List<InvoiceLineItem>()).OrderBy(l => l.SortOrder).ThenBy(l => l.InvoiceLineItemId, StringComparer.Ordinal).ToList();
			var number = string.IsNullOrWhiteSpace(i.DisplayNumber) ? "#" + i.InvoiceNumber.ToString(CultureInfo.InvariantCulture) : i.DisplayNumber;
			var stable = new
			{
				kind = "invoice/1", id = i.InvoiceId.ToLowerInvariant(), number, i.CustomerBillingProfileId, i.ContactId, issued = ApprovalDocuments.Date(i.IssuedOn), due = ApprovalDocuments.Date(i.DueOn),
				currency = i.Currency?.ToUpperInvariant(), sub = ApprovalDocuments.Money(i.SubTotal), discount = ApprovalDocuments.Money(i.DiscountAmount), tax = ApprovalDocuments.Money(i.TaxAmount),
				total = ApprovalDocuments.Money(i.Total), i.TaxComponentsJson, i.TermsText, i.Notes, i.ServiceContractId, i.DeploymentId,
				lines = ordered.Select(l => new { id = l.InvoiceLineItemId, l.Description, q = ApprovalDocuments.Money(l.Quantity), rate = ApprovalDocuments.Money(l.UnitRate), amount = ApprovalDocuments.Money(l.Amount), l.Taxable, l.CallId }).ToList()
			};
			var rows = new List<(string, string)> { ("Total", ApprovalRouting.AmountText(i.Total, i.Currency)), ("Issued (UTC)", i.IssuedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("Due (UTC)", i.DueOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("Lines", ordered.Count.ToString(CultureInfo.InvariantCulture)) };
			rows.AddRange(ordered.Select(l => (l.Description ?? string.Empty, ApprovalRouting.AmountText(l.Amount, i.Currency))));
			return new ApprovalSubjectInfo
			{
				Kind = SignatureSubjectKinds.InvoiceApproval,
				SubjectId = i.InvoiceId,
				Title = "Invoice " + number,
				Amount = i.Total,
				Currency = i.Currency,
				ContentSha256 = ApprovalDocuments.Hash(stable),
				RevisionId = number,
				OwnerUserId = i.AddedByUserId,
				Submittable = i.Status == (int)InvoiceStatus.Draft,
				DocumentHtml = ApprovalDocuments.Build("Invoice " + number, rows)
			};
		}

		public async Task<List<ApprovalSubjectInfo>> GetCandidatesAsync(int departmentId, int take)
		{
			var list = new List<ApprovalSubjectInfo>();
			foreach (var invoice in (await _invoices.GetInvoicesByStatusAsync(departmentId, (int)InvoiceStatus.Draft) ?? Enumerable.Empty<Invoice>())
				.Where(i => i.DepartmentId == departmentId && !i.IsDeleted).OrderByDescending(i => i.AddedOn).Take(take))
				list.Add(Describe(invoice, (await _lines.GetByInvoiceIdAsync(invoice.InvoiceId, departmentId) ?? Enumerable.Empty<InvoiceLineItem>()).ToList()));
			return list;
		}

		public Task OnApprovedAsync(ApprovalRequest approval, string approverUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task OnRejectedAsync(ApprovalRequest approval, string userId, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	/// <summary>
	/// Cal OES MARS packages (Business Ops cost recovery): an F-42 or expense-claim work item approved before the portal hand-off. The
	/// hash covers the snapshot and the profiles it was built under; the completed chain seals the package's evidence document (S1,
	/// "sealing pending" until it seals). Submittable while Draft, Needs review or Ready for portal. Amount = the expected total.
	/// </summary>
	public class CalOesMarsPackageApprovalSource : IApprovalSubjectSource
	{
		private readonly ICalOesMarsWorkItemRepository _items;
		private readonly IBusinessOperationsAccessService _access;

		public CalOesMarsPackageApprovalSource(ICalOesMarsWorkItemRepository items, IBusinessOperationsAccessService access)
		{
			_items = items;
			_access = access;
		}

		public SignatureSubjectKinds Kind => SignatureSubjectKinds.CalOesMarsPackageApproval;

		public Task<bool> IsAvailableAsync(int departmentId) => ApprovalDocuments.SafeAsync(() => _access.CanUseCostRecoveryAsync(departmentId), "Cal OES MARS");

		private static bool IsPackage(CalOesMarsWorkItem item) => item.RecordType is (int)CalOesMarsRecordTypes.F42 or (int)CalOesMarsRecordTypes.ExpenseClaim;

		public async Task<ApprovalSubjectInfo> DescribeAsync(int departmentId, string subjectId)
		{
			var item = await _items.GetByIdForDepartmentAsync(subjectId, departmentId);
			return item == null || item.DepartmentId != departmentId || item.IsDeleted || !IsPackage(item) ? null : Describe(item);
		}

		public static ApprovalSubjectInfo Describe(CalOesMarsWorkItem w)
		{
			var stable = new
			{
				kind = "caloes-mars-package/1", id = w.CalOesMarsWorkItemId.ToLowerInvariant(), w.RecordType, w.DeploymentId, w.RmsExternalOrderId, w.RmsExternalOrderFillId,
				w.AuthorityProfileCode, w.RateProfileVersion, w.AgreementSnapshotId, w.SnapshotJson, expected = ApprovalDocuments.Money(w.ExpectedTotal), w.SupersedesWorkItemId
			};
			var title = ((CalOesMarsRecordTypes)w.RecordType == CalOesMarsRecordTypes.F42 ? "F-42 " : "Expense claim ") + (w.MarsRecordId ?? w.CalOesMarsWorkItemId);
			return new ApprovalSubjectInfo
			{
				Kind = SignatureSubjectKinds.CalOesMarsPackageApproval,
				SubjectId = w.CalOesMarsWorkItemId,
				Title = title,
				Amount = w.ExpectedTotal,
				Currency = "USD",
				ContentSha256 = ApprovalDocuments.Hash(stable),
				RevisionId = w.RowVersion.ToString(CultureInfo.InvariantCulture),
				OwnerUserId = w.AddedByUserId,
				RetentionYears = 7,
				Submittable = w.LocalState is (int)CalOesMarsLocalStates.Draft or (int)CalOesMarsLocalStates.NeedsReview or (int)CalOesMarsLocalStates.ReadyForPortal,
				DocumentHtml = ApprovalDocuments.Build(title, new[]
				{
					("Record type", ((CalOesMarsRecordTypes)w.RecordType).ToString()), ("Expected total", ApprovalRouting.AmountText(w.ExpectedTotal, "USD")),
					("Authority profile", w.AuthorityProfileCode), ("Rate profile version", w.RateProfileVersion), ("Agreement snapshot", w.AgreementSnapshotId)
				}, w.SnapshotJson)
			};
		}

		public async Task<List<ApprovalSubjectInfo>> GetCandidatesAsync(int departmentId, int take) =>
			(await _items.GetActionQueueAsync(departmentId) ?? Enumerable.Empty<CalOesMarsWorkItem>())
				.Where(w => w.DepartmentId == departmentId && !w.IsDeleted && IsPackage(w)).OrderByDescending(w => w.AddedOn).Take(take).Select(Describe).Where(i => i.Submittable).ToList();

		public Task OnApprovedAsync(ApprovalRequest approval, string approverUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task OnRejectedAsync(ApprovalRequest approval, string userId, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	/// <summary>
	/// Mutual-aid agreements (S4): the department's chain approves the agreement document and the completed chain seals it. Free with the
	/// department (no add-on). The partner department's countersignature is owed (Department Links sharing is not on this branch).
	/// </summary>
	public class MutualAidAgreementApprovalSource : IApprovalSubjectSource
	{
		private readonly IMutualAidAgreementsRepository _agreements;

		public MutualAidAgreementApprovalSource(IMutualAidAgreementsRepository agreements)
		{
			_agreements = agreements;
		}

		public SignatureSubjectKinds Kind => SignatureSubjectKinds.MutualAidAgreementApproval;

		public Task<bool> IsAvailableAsync(int departmentId) => Task.FromResult(departmentId > 0);

		public async Task<ApprovalSubjectInfo> DescribeAsync(int departmentId, string subjectId)
		{
			if (!int.TryParse(subjectId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
				return null;
			var agreement = await _agreements.GetForDepartmentAsync(departmentId, id);
			return agreement == null || agreement.DepartmentId != departmentId || agreement.IsDeleted ? null : Describe(agreement);
		}

		public static ApprovalSubjectInfo Describe(MutualAidAgreement a)
		{
			var stable = new { kind = "mutual-aid-agreement/1", id = a.MutualAidAgreementId, a.Title, a.DepartmentLinkId, a.PartnerDepartmentId, a.PartnerName, a.Terms, effective = ApprovalDocuments.Date(a.EffectiveOn), expires = ApprovalDocuments.Date(a.ExpiresOn) };
			return new ApprovalSubjectInfo
			{
				Kind = SignatureSubjectKinds.MutualAidAgreementApproval,
				SubjectId = a.MutualAidAgreementId.ToString(CultureInfo.InvariantCulture),
				Title = a.Title,
				ContentSha256 = ApprovalDocuments.Hash(stable),
				OwnerUserId = a.CreatedByUserId,
				RetentionYears = 10,
				Submittable = a.Status is (int)MutualAidAgreementStatuses.Draft or (int)MutualAidAgreementStatuses.InApproval,
				DocumentHtml = ApprovalDocuments.Build(a.Title, new[]
				{
					("Partner", a.PartnerName), ("Effective (UTC)", a.EffectiveOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
					("Expires (UTC)", a.ExpiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
				}, a.Terms)
			};
		}

		public async Task<List<ApprovalSubjectInfo>> GetCandidatesAsync(int departmentId, int take) =>
			(await _agreements.GetAllForDepartmentAsync(departmentId) ?? new List<MutualAidAgreement>()).Where(a => a.DepartmentId == departmentId && a.Status == (int)MutualAidAgreementStatuses.Draft)
				.Take(take).Select(Describe).ToList();

		public async Task OnApprovedAsync(ApprovalRequest approval, string approverUserId, CancellationToken cancellationToken = default) =>
			await SetStatusAsync(approval, MutualAidAgreementStatuses.Approved, approverUserId, cancellationToken);

		public async Task OnRejectedAsync(ApprovalRequest approval, string userId, string reason, CancellationToken cancellationToken = default) =>
			await SetStatusAsync(approval, MutualAidAgreementStatuses.Draft, userId, cancellationToken);

		private async Task SetStatusAsync(ApprovalRequest approval, MutualAidAgreementStatuses status, string userId, CancellationToken cancellationToken)
		{
			if (!int.TryParse(approval.SubjectId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
				return;
			var agreement = await _agreements.GetForDepartmentAsync(approval.DepartmentId, id);
			if (agreement == null)
				return;
			agreement.Status = (int)status;
			agreement.UpdatedByUserId = userId;
			agreement.UpdatedOn = DateTime.UtcNow;
			await _agreements.UpdateAsync(agreement, cancellationToken, true);
		}
	}
}
