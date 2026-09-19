using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Invoicing
{
	/// <summary>
	/// Phase B2 seams on the invoicing service: the pay-page URL for documents and Workflow payloads, the dispute
	/// lifecycle, and the Advanced Data Protection write/read seam for the catalog-26 columns (customer e-mail,
	/// invoice notes, payer e-mail, payment reference/receipt/notes). Every dependency here is optional: without the
	/// payments service no pay URL is offered, and without the protection services rows are written and read as-is.
	/// The invoicing UI has no step-up grant plumbing yet, so user reads see REDACTED for protected values and every
	/// write runs as a workload caller (encrypted at rest, sentinel-safe); the reveal path is a follow-up.
	/// </summary>
	public partial class InvoicingService
	{
		private readonly Lazy<IInvoicePaymentsService> _paymentsService;
		private readonly Lazy<IProtectedWriteService> _protectedWrite;
		private readonly Lazy<IProtectedReadService> _protectedRead;

		private const string WorkloadPurpose = "invoicing";

		public async Task<InvoicePayment> ApplyPaymentDisputeAsync(string invoicePaymentId, int departmentId, InvoiceDisputeStages stage, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var payment = await _payments.GetByIdForDepartmentAsync(invoicePaymentId, departmentId);
			if (payment == null) throw new InvalidOperationException("invoicing_payment_not_found");
			var invoice = await _invoices.GetByIdForDepartmentAsync(payment.InvoiceId, departmentId);
			if (invoice == null) throw new InvalidOperationException("invoicing_invoice_not_found");

			var wasDisputed = payment.Status is (int)InvoicePaymentStatuses.Disputed or (int)InvoicePaymentStatuses.DisputeLost;
			var audit = NewAuditEvent(departmentId, userId, AuditLogTypes.InvoicePaymentDisputed, ipAddress, userAgent);
			audit.Before = Snapshot(invoice);

			switch (stage)
			{
				case InvoiceDisputeStages.Opened:
					payment.Status = (int)InvoicePaymentStatuses.Disputed;
					break;
				case InvoiceDisputeStages.Won:
					payment.Status = payment.RefundedAmount >= payment.Amount ? (int)InvoicePaymentStatuses.Refunded
						: payment.RefundedAmount > 0 ? (int)InvoicePaymentStatuses.PartiallyRefunded
						: (int)InvoicePaymentStatuses.Succeeded;
					break;
				case InvoiceDisputeStages.Lost:
					payment.Status = (int)InvoicePaymentStatuses.DisputeLost;
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(stage));
			}
			await _payments.SaveOrUpdateAsync(payment, cancellationToken);

			var oldStatus = invoice.Status;
			await ApplyPaymentStateAsync(invoice, userId, DateTime.UtcNow, cancellationToken);
			audit.After = Snapshot(invoice);
			_eventAggregator.SendMessage<AuditEvent>(audit);

			// Trigger 95 fires once per dispute: when it opens, or when it is lost without ever having been seen open.
			if (!wasDisputed && stage != InvoiceDisputeStages.Won)
				await PublishAsync(invoice, WorkflowTriggerEventType.InvoicePaymentDisputed, payment, oldStatus, cancellationToken);
			return payment;
		}

		/// <summary>The pay-page URL for an invoice, or null when online payment is not offered right now (any reason). Never throws.</summary>
		private async Task<string> PayUrlAsync(Invoice invoice)
		{
			if (invoice == null || _paymentsService == null) return null;
			try { return await _paymentsService.Value.BuildPayPageUrlAsync(invoice.InvoiceId, invoice.DepartmentId); }
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Pay-page URL for invoice {invoice.InvoiceId} could not be built.");
				return null;
			}
		}

		// ---- Advanced Data Protection seam (catalog 26) ----------------------------------------------------------

		private static void MarkProtected(CustomerBillingProfile row) { row.IsProtected = true; row.ProtectedCatalogVersion = Math.Max(row.ProtectedCatalogVersion, InvoicingProtectedFields.CatalogVersion); }
		private static void MarkProtected(Invoice row) { row.IsProtected = true; row.ProtectedCatalogVersion = Math.Max(row.ProtectedCatalogVersion, InvoicingProtectedFields.CatalogVersion); }
		private static void MarkProtected(InvoicePayment row) { row.IsProtected = true; row.ProtectedCatalogVersion = Math.Max(row.ProtectedCatalogVersion, InvoicingProtectedFields.CatalogVersion); }

		/// <summary>Encrypts the cataloged columns (when the department enforces protection) and saves. <paramref name="existing"/> lets the sentinel policy keep an untouched envelope when the caller sent REDACTED back.</summary>
		private async Task<T> SaveProtectedAsync<T>(IRepository<T> repository, T entity, T existing, Func<T, string> rowKey,
			IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors, Action<T> markProtected, int departmentId, CancellationToken cancellationToken) where T : class, IEntity
		{
			if (_protectedWrite == null)
				return await repository.SaveOrUpdateAsync(entity, cancellationToken);

			var key = rowKey(entity);
			if (string.IsNullOrWhiteSpace(key))
				return await InsertProtectedAsync(repository, entity, rowKey, accessors, markProtected, departmentId, cancellationToken);

			var result = await _protectedWrite.Value.PrepareRecordsEntityWriteAsync(departmentId, entity, existing, key, accessors, () => markProtected(entity), null, null, true, cancellationToken);
			if (result != null && !result.Success)
				throw new InvalidOperationException("invoicing_protected_write_refused");
			return await repository.SaveOrUpdateAsync(entity, cancellationToken);
		}

		/// <summary>A new row has no key until it is inserted, and the key is part of the envelope binding: the cataloged columns are held back, the row allocated, then encrypted and written (the work-order precedent).</summary>
		private async Task<T> InsertProtectedAsync<T>(IRepository<T> repository, T entity, Func<T, string> rowKey,
			IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors, Action<T> markProtected, int departmentId, CancellationToken cancellationToken) where T : class, IEntity
		{
			if (_protectedWrite == null)
				return await repository.SaveOrUpdateAsync(entity, cancellationToken);

			var held = accessors.ToDictionary(a => a.Key, a => a.Value.Get(entity), StringComparer.OrdinalIgnoreCase);
			if (held.Values.All(string.IsNullOrEmpty))
				return await repository.SaveOrUpdateAsync(entity, cancellationToken);

			foreach (var accessor in accessors) accessor.Value.Set(entity, null);
			var allocated = await repository.SaveOrUpdateAsync(entity, cancellationToken);
			foreach (var accessor in accessors) accessor.Value.Set(allocated, held[accessor.Key]);

			var result = await _protectedWrite.Value.PrepareRecordsEntityWriteAsync(departmentId, allocated, null, rowKey(allocated), accessors, () => markProtected(allocated), null, null, true, cancellationToken);
			if (result != null && !result.Success)
				throw new InvalidOperationException("invoicing_protected_write_refused");
			return await repository.SaveOrUpdateAsync(allocated, cancellationToken);
		}

		/// <summary>User-facing read: protected values become REDACTED (no grant is carried on this path yet). Never throws.</summary>
		private async Task ResolveReadAsync<T>(IReadOnlyList<T> rows, Func<T, string> rowKey, IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors, int departmentId) where T : class
		{
			if (_protectedRead == null || rows == null || rows.Count == 0) return;
			try { await _protectedRead.Value.ResolveRecordsEntitiesForReadAsync(departmentId, rows.Select(r => (r, rowKey(r))).ToList(), accessors, null, null); }
			catch (Exception ex) { Logging.LogException(ex, "Protected invoicing rows could not be resolved for read."); }
		}

		/// <summary>System read (delivery, payments): protected values are decrypted for the workload. Never throws.</summary>
		private async Task ResolveWorkloadAsync<T>(IReadOnlyList<T> rows, Func<T, string> rowKey, IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors, int departmentId) where T : class
		{
			if (_protectedRead == null || rows == null || rows.Count == 0) return;
			try { await _protectedRead.Value.ResolveRecordsEntitiesForWorkloadAsync(departmentId, WorkloadPurpose, rows.Select(r => (r, rowKey(r))).ToList(), accessors); }
			catch (Exception ex) { Logging.LogException(ex, "Protected invoicing rows could not be resolved for the workload."); }
		}

		private Task ResolveReadAsync(Invoice invoice) => invoice == null ? Task.CompletedTask : ResolveReadAsync(new[] { invoice }, i => i.InvoiceId, InvoicingProtectedFields.Invoice, invoice.DepartmentId);
		private Task ResolveReadAsync(IReadOnlyList<Invoice> invoices, int departmentId) => ResolveReadAsync(invoices, i => i.InvoiceId, InvoicingProtectedFields.Invoice, departmentId);
		private Task ResolveReadAsync(IReadOnlyList<InvoicePayment> payments, int departmentId) => ResolveReadAsync(payments, p => p.InvoicePaymentId, InvoicingProtectedFields.Payment, departmentId);
		private Task ResolveReadAsync(CustomerBillingProfile profile) => profile == null ? Task.CompletedTask : ResolveReadAsync(new[] { profile }, p => p.CustomerBillingProfileId, InvoicingProtectedFields.BillingProfile, profile.DepartmentId);
		private Task ResolveReadAsync(IReadOnlyList<CustomerBillingProfile> profiles, int departmentId) => ResolveReadAsync(profiles, p => p.CustomerBillingProfileId, InvoicingProtectedFields.BillingProfile, departmentId);
		private Task ResolveWorkloadAsync(CustomerBillingProfile profile) => profile == null ? Task.CompletedTask : ResolveWorkloadAsync(new[] { profile }, p => p.CustomerBillingProfileId, InvoicingProtectedFields.BillingProfile, profile.DepartmentId);
		private Task ResolveWorkloadAsync(Invoice invoice) => invoice == null ? Task.CompletedTask : ResolveWorkloadAsync(new[] { invoice }, i => i.InvoiceId, InvoicingProtectedFields.Invoice, invoice.DepartmentId);

		/// <summary>The invoice with children, decrypted for a system workload (delivery).</summary>
		private async Task<Invoice> GetInvoiceForWorkloadAsync(string invoiceId, int departmentId)
		{
			var invoice = await _invoices.GetByIdForDepartmentAsync(invoiceId, departmentId);
			if (invoice == null) return null;
			await LoadChildrenAsync(invoice);
			await ResolveWorkloadAsync(invoice);
			await ResolveWorkloadAsync(invoice.Payments, p => p.InvoicePaymentId, InvoicingProtectedFields.Payment, departmentId);
			return invoice;
		}
	}
}
