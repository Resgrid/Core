using System;

namespace Resgrid.Model.Invoicing
{
	/// <summary>
	/// Workflow routing for the Phase B invoice lifecycle triggers (plan B6, registry 52–57). The payload carries
	/// identifiers, status, amounts and dates only: never a billing e-mail, note, void reason, payer e-mail or receipt
	/// URL (the Phase B ADP catalog fields), and the contact name is REDACTED on a protected row.
	/// </summary>
	public static class InvoiceWorkflowPayload
	{
		/// <summary>(template variable, payload property) pairs; drives the variable catalog, context builder and sample data.</summary>
		public static readonly (string Variable, string Property)[] Variables =
		{
			("id", "InvoiceId"), ("number", "InvoiceNumber"), ("status", "Status"), ("contact_id", "ContactId"), ("contact_name", "ContactName"),
			("currency", "Currency"), ("sub_total", "SubTotal"), ("discount_amount", "DiscountAmount"), ("tax_amount", "TaxAmount"),
			("total", "Total"), ("amount_paid", "AmountPaid"), ("balance", "Balance"), ("issued_on", "IssuedOn"), ("due_on", "DueOn"),
			("sent_on", "SentOn"), ("paid_on", "PaidOn"), ("payment_amount", "PaymentAmount"), ("payment_method", "PaymentMethod"),
			("payment_id", "PaymentId"), ("old_status", "OldStatus")
		};

		public static readonly int[] Triggers = { 52, 53, 54, 55, 56, 57, 94, 95 };

		public static bool IsInvoice(int trigger) => Array.IndexOf(Triggers, trigger) >= 0;

		/// <summary>The outbox producer subsystem name for every invoicing event.</summary>
		public const string Producer = "Invoicing";
	}
}
