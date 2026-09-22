using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Invoicing;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Workforce &amp; Business Operations plan Phase C (C4): daily time reports (DTRs), their time entries, signatures,
	/// expenses and receipts. Report numbers come from the atomic per-department sequence; status transitions happen
	/// only here (Draft→Submitted→Approved→Billed, Void from any unbilled state); Submitted and Approved publish
	/// their Workflow trigger through the domain outbox. Callers authorize (approval needs TimeReports_Approve).
	/// </summary>
	public interface ITimeTrackingService
	{
		Task<List<DeploymentTimeReport>> GetTimeReportsAsync(string deploymentId, int departmentId);
		/// <summary>Personnel hours across the deployment's non-void reports, read in one pass for the deployment page.</summary>
		Task<decimal> GetPersonnelHoursAsync(string deploymentId, int departmentId);
		Task<DeploymentTimeReport> GetTimeReportByIdAsync(string deploymentTimeReportId, int departmentId);
		/// <summary>Every live report of the deployment with its entries, in two reads (report + entries per report would be N+1).</summary>
		Task<List<DeploymentTimeReport>> GetTimeReportsWithEntriesAsync(string deploymentId, int departmentId);
		Task<List<DeploymentTimeReport>> GetUnbilledApprovedReportsAsync(int departmentId, string deploymentId = null);

		/// <summary>Allocates the next report number, copies the deployment's agency identifiers and prefills one Deployment entry per active roster subject (prior report's times when one exists).</summary>
		Task<DeploymentTimeReport> CreateTimeReportAsync(string deploymentId, int departmentId, DateTime reportDate, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Header fields only (flags, notes, identifiers); entries go through <see cref="SaveTimeEntriesAsync"/>.</summary>
		Task<DeploymentTimeReport> UpdateTimeReportAsync(DeploymentTimeReport report, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Replaces the report's entries as a batch after validation; errors leave the stored entries untouched.</summary>
		Task<TimeReportSaveResult> SaveTimeEntriesAsync(string deploymentTimeReportId, int departmentId, List<DeploymentTimeEntry> entries, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		TimeReportValidation Validate(DeploymentTimeReport report, IReadOnlyList<DeploymentTimeEntry> entries, IReadOnlyCollection<string> rosterSubjectIds);

		Task<TimeReportSaveResult> SubmitTimeReportAsync(string deploymentTimeReportId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<DeploymentTimeReport> ApproveTimeReportAsync(string deploymentTimeReportId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<DeploymentTimeReport> VoidTimeReportAsync(string deploymentTimeReportId, int departmentId, string reason, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Approved → Billed for the reports an invoice now covers (contractor billing, plan C4). Reports already billed or not Approved are skipped.</summary>
		Task<int> MarkTimeReportsBilledAsync(IEnumerable<string> deploymentTimeReportIds, int departmentId, string invoiceId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Contractor signature (the acting user) and/or the customer signer's typed name.</summary>
		Task<DeploymentTimeReport> SignTimeReportAsync(string deploymentTimeReportId, int departmentId, bool contractorSigned, string customerSignerName, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		Task<string> RenderTimeReportHtmlAsync(string deploymentTimeReportId, int departmentId);
		Task<byte[]> GetTimeReportPdfAsync(string deploymentTimeReportId, int departmentId);
		/// <summary>Renders the DTR to PDF and files it as a TimeReportPdf attachment on the deployment.</summary>
		Task<DeploymentAttachment> GenerateTimeReportPdfAsync(string deploymentTimeReportId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>CSV of every entry on the deployment (agency reconciliation).</summary>
		Task<string> ExportTimeEntriesCsvAsync(string deploymentId, int departmentId);

		Task<List<DeploymentExpense>> GetExpensesAsync(string deploymentId, int departmentId);
		Task<DeploymentExpense> GetExpenseByIdAsync(string deploymentExpenseId, int departmentId);
		/// <summary>Creates or updates an expense; a receipt (bytes + file name + content type) becomes a Receipt attachment linked by ReceiptAttachmentId.</summary>
		Task<DeploymentExpense> SaveExpenseAsync(DeploymentExpense expense, byte[] receipt, string receiptFileName, string receiptContentType, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteExpenseAsync(string deploymentExpenseId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
	}
}
