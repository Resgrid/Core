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
	/// lifecycle, and the customer-contact decrypt for delivery renders. Every dependency here is optional: without
	/// the payments service no pay URL is offered, and without the protection services contacts render as stored.
	/// The invoicing UI has no step-up grant plumbing yet, so user reads see REDACTED for protected values and every
	/// write runs as a workload caller (encrypted at rest, sentinel-safe); the reveal path is a follow-up.
	/// </summary>
	public partial class InvoicingService
	{
		private readonly Lazy<IInvoicePaymentsService> _paymentsService;
		private readonly Lazy<IProtectedReadService> _protectedRead;

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

		// ---- Customer-facing renders ---------------------------------------------------------------------------
		//
		// Invoice, line, payment, billing-profile and billing-identity columns are not under Advanced Data Protection:
		// customers who are not signed in read invoices (PDF, pay page) and must see them whole. The customer's own
		// Contact row may be protected (Contacts family), so the delivery render decrypts its name through the
		// broker's "invoicing" workload lane; a user render shows what the caller may see.

		private const string WorkloadPurpose = "invoicing";

		/// <summary>Decrypts a protected customer contact for a system workload (delivery). Never throws; leaves the row as-is on failure.</summary>
		private async Task ResolveContactForWorkloadAsync(Contact contact, int departmentId)
		{
			if (contact == null || _protectedRead?.Value == null) return;
			try { await _protectedRead.Value.ResolveRecordsEntitiesForWorkloadAsync(departmentId, WorkloadPurpose, new[] { (contact, contact.ContactId) }, ProtectedReadService.ContactFieldAccessors); }
			catch (Exception ex) { Logging.LogException(ex, $"Contact {contact.ContactId} could not be resolved for the invoice workload."); }
		}
	}
}
