using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Invoicing;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The contractor billing engine (Workforce &amp; Business Operations plan, C4): approved, unbilled daily time
	/// reports → charge set → draft invoice with deployment/DTR provenance, plus the invoice-submission packet
	/// (invoice PDF, DTR PDFs, receipts, manifest and compliance documents). The arithmetic lives in the pure
	/// calculator; this service loads the graph and writes the invoice. Callers authorize.
	/// </summary>
	public interface IContractorBillingEngine
	{
		/// <summary>Charges for every Approved, unbilled DTR of the deployment dated on or before <paramref name="throughDate"/> (all when null). Never writes.</summary>
		Task<ContractorChargeSet> CalculateDeploymentChargesAsync(string deploymentId, int departmentId, DateTime? throughDate = null);

		/// <summary>Charge set → draft invoice (lines carry CallId + DeploymentTimeReportId; header carries DeploymentId/ServiceContractId, contract terms, discount snapshot); the DTRs move to Billed.</summary>
		Task<Invoice> GenerateInvoiceFromDeploymentAsync(string deploymentId, int departmentId, DateTime? throughDate, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		/// <summary>The packet for an invoice generated from a deployment: invoice PDF + DTR PDFs + receipts + manifest + compliance documents the contract requires at invoice submission. Protected files the caller cannot reveal are listed as missing.</summary>
		Task<ContractorInvoicePacket> BuildInvoicePacketAsync(string invoiceId, int departmentId);

		/// <summary>Sends the invoice with the packet attached (contract submission address by default).</summary>
		Task<Invoice> SendDeploymentInvoiceAsync(string invoiceId, int departmentId, string toEmail, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		/// <summary>Daily sweep (worker 32): billable deployments with approved DTRs unbilled longer than <paramref name="unbilledDays"/>, or completed with any unbilled DTR, digest to department administrators. Returns departments notified.</summary>
		Task<int> RunFinanceReminderSweepAsync(DateTime asOfUtc, int unbilledDays, Func<int, Task<bool>> departmentEnabled = null, CancellationToken cancellationToken = default);
	}
}
