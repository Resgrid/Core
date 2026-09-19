using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Invoicing;

namespace Resgrid.Services.Invoicing
{
	/// <summary>Rendering and delivery (plan B4): HTML → PDF through IPdfProvider, e-mail with the PDF attached. Customer-facing output never contains internal cost.</summary>
	public partial class InvoicingService
	{
		public Task<string> RenderInvoiceHtmlAsync(string invoiceId, int departmentId) => RenderInvoiceHtmlCoreAsync(invoiceId, departmentId, workload: false);

		/// <summary>User rendering shows REDACTED for protected values; the delivery workload (e-mail, PDF attachment) decrypts them.</summary>
		private async Task<string> RenderInvoiceHtmlCoreAsync(string invoiceId, int departmentId, bool workload)
		{
			var invoice = workload ? await GetInvoiceForWorkloadAsync(invoiceId, departmentId) : await GetInvoiceByIdAsync(invoiceId, departmentId);
			if (invoice == null)
				return null;

			var identity = await GetDepartmentBillingIdentityAsync(departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			var profile = await _profiles.GetByIdForDepartmentAsync(invoice.CustomerBillingProfileId, departmentId);
			if (workload) await ResolveWorkloadAsync(profile); else await ResolveReadAsync(profile);
			var contact = await _contactsService.GetContactByIdAsync(invoice.ContactId);

			var remitTo = identity?.RemitToAddressId.HasValue == true ? await SafeAddressAsync(identity.RemitToAddressId.Value) : null;
			Address billTo = null;
			if (profile != null && !profile.UseContactMailingAddress && profile.BillingAddressId.HasValue)
				billTo = await SafeAddressAsync(profile.BillingAddressId.Value);
			else if (contact?.MailingAddressId != null)
				billTo = await SafeAddressAsync(contact.MailingAddressId.Value);
			else if (contact?.PhysicalAddressId != null)
				billTo = await SafeAddressAsync(contact.PhysicalAddressId.Value);

			var model = new InvoiceRenderModel
			{
				Invoice = invoice,
				DepartmentName = string.IsNullOrWhiteSpace(identity?.LegalBusinessName) ? department?.Name : identity.LegalBusinessName,
				RemitTo = remitTo,
				TaxRegistrationNumber = identity?.TaxRegistrationNumber,
				SecondaryTaxRegistrationNumber = identity?.SecondaryTaxRegistrationNumber,
				FooterText = identity?.InvoiceFooterText,
				CustomerName = invoice.IsProtected ? ProtectedDataEnvelope.RedactionValue : contact?.Name,
				CustomerEmail = invoice.IsProtected ? null : (profile?.BillingEmail ?? contact?.Email),
				BillTo = invoice.IsProtected ? null : billTo,
				TaxComponents = ParseTaxComponents(invoice.TaxComponentsJson),
				// Phase B2: the pay-page link is printed only when the department shows it on documents and online payment is offered right now.
				PayUrl = identity?.ShowPayOnlineOnDocuments == false ? null : await PayUrlAsync(invoice)
			};

			return RenderInvoiceHtml(model);
		}

		public Task<byte[]> GetInvoicePdfAsync(string invoiceId, int departmentId) => GetInvoicePdfCoreAsync(invoiceId, departmentId, workload: false);

		private async Task<byte[]> GetInvoicePdfCoreAsync(string invoiceId, int departmentId, bool workload)
		{
			var html = await RenderInvoiceHtmlCoreAsync(invoiceId, departmentId, workload);
			return html == null ? null : _pdfProvider.ConvertHtmlToPdf(html);
		}

		public async Task<Invoice> SendInvoiceAsync(string invoiceId, int departmentId, string toEmail, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var invoice = await _invoices.GetByIdForDepartmentAsync(invoiceId, departmentId);
			if (invoice == null) throw new InvalidOperationException("invoicing_invoice_not_found");
			if (invoice.Status == (int)InvoiceStatus.Void) throw new InvalidOperationException("invoicing_invoice_void");

			var profile = await _profiles.GetByIdForDepartmentAsync(invoice.CustomerBillingProfileId, departmentId);
			await ResolveWorkloadAsync(profile);
			var recipient = string.IsNullOrWhiteSpace(toEmail) ? profile?.BillingEmail : toEmail.Trim();
			if (string.IsNullOrWhiteSpace(recipient)) throw new InvalidOperationException("invoicing_no_recipient_email");

			// A draft becomes Sent first so the PDF carries the issue and due dates and the number is final.
			if (invoice.Status == (int)InvoiceStatus.Draft)
				invoice = await MarkSentAsync(invoiceId, departmentId, recipient, userId, ipAddress, userAgent, cancellationToken);

			var pdf = await GetInvoicePdfCoreAsync(invoiceId, departmentId, workload: true);
			if (pdf == null || pdf.Length == 0) throw new InvalidOperationException("invoicing_pdf_unavailable");

			var label = $"Invoice #{invoice.InvoiceNumber}";
			var notification = new EmailNotification
			{
				To = recipient,
				Subject = $"{label} from {await DepartmentDisplayNameAsync(departmentId)}",
				Body = $"{label} for {FormatMoney(invoice.Total, invoice.Currency)} is attached." + (invoice.DueOn.HasValue ? $" Payment is due by {invoice.DueOn.Value:yyyy-MM-dd}." : string.Empty),
				AttachmentName = $"invoice-{invoice.InvoiceNumber}.pdf",
				AttachmentData = pdf
			};

			var sent = await _emailService.SendInvoiceAsync(notification, departmentId, InvoiceUrl(invoice.InvoiceId), await PayUrlAsync(invoice), label);
			if (!sent)
			{
				// The invoice stays issued (its number and dates are final) but is not stamped as sent: the caller sees
				// the failure instead of a success message and can send again once the address or provider is fixed.
				Logging.LogError($"Invoice {invoice.InvoiceId} e-mail to the customer was not sent (department {departmentId}).");
				throw new InvalidOperationException("invoicing_email_not_sent");
			}

			if (invoice.Status != (int)InvoiceStatus.Draft && (invoice.SentToEmail != recipient || invoice.SentOn == null))
			{
				var pristine = invoice.CloneJson();
				var audit = NewAuditEvent(departmentId, userId, AuditLogTypes.InvoiceSent, ipAddress, userAgent);
				audit.Before = Snapshot(invoice);
				invoice.SentOn = DateTime.UtcNow;
				invoice.SentToEmail = recipient;
				invoice.EditedOn = invoice.SentOn;
				invoice.EditedByUserId = userId;
				await SaveProtectedAsync(_invoices, invoice, pristine, i => i.InvoiceId, InvoicingProtectedFields.Invoice, MarkProtected, departmentId, cancellationToken);
				audit.After = Snapshot(invoice);
				_eventAggregator.SendMessage<AuditEvent>(audit);
			}

			return await GetInvoiceByIdAsync(invoiceId, departmentId);
		}

		private async Task<Address> SafeAddressAsync(int addressId)
		{
			try { return await _addressService.GetAddressByIdAsync(addressId); }
			catch (Exception ex) { Logging.LogException(ex, $"Invoice rendering: address {addressId} could not be read."); return null; }
		}

		private async Task<string> DepartmentDisplayNameAsync(int departmentId)
		{
			var identity = await _identities.GetByDepartmentIdAsync(departmentId);
			if (!string.IsNullOrWhiteSpace(identity?.LegalBusinessName)) return identity.LegalBusinessName;
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			return department?.Name ?? "Resgrid";
		}

		private static string InvoiceUrl(string invoiceId) =>
			$"{(Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/')}/User/Invoicing/View/{invoiceId}";

		/// <summary>Pure HTML rendering; unit-tested. HTML-encodes every user value.</summary>
		public static string RenderInvoiceHtml(InvoiceRenderModel model)
		{
			var invoice = model.Invoice;
			var currency = invoice.Currency ?? "USD";
			var sb = new StringBuilder();
			sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>").Append(E($"Invoice #{invoice.InvoiceNumber}")).Append("</title>");
			sb.Append("<style>body{font-family:Helvetica,Arial,sans-serif;font-size:12px;color:#222;margin:32px}h1{font-size:22px;margin:0 0 4px}h2{font-size:14px;margin:18px 0 6px}table{border-collapse:collapse;width:100%}th,td{padding:6px 8px;text-align:left;vertical-align:top}th{border-bottom:2px solid #444;font-size:11px;text-transform:uppercase}td.num,th.num{text-align:right;white-space:nowrap}tr.line td{border-bottom:1px solid #ddd}table.totals{width:auto;margin-left:auto;margin-top:12px}table.totals td{padding:4px 8px}table.totals tr.grand td{border-top:2px solid #444;font-weight:bold;font-size:14px}.meta td{padding:2px 8px 2px 0}.muted{color:#666}.status{display:inline-block;padding:2px 8px;border:1px solid #444;border-radius:3px;font-size:11px;text-transform:uppercase}.footer{margin-top:28px;font-size:11px;color:#555;white-space:pre-wrap}</style></head><body>");

			sb.Append("<table><tr><td style=\"width:55%\">");
			sb.Append("<h1>").Append(E(model.DepartmentName ?? string.Empty)).Append("</h1>");
			if (model.RemitTo != null) sb.Append("<div class=\"muted\">").Append(E(FormatAddress(model.RemitTo))).Append("</div>");
			if (!string.IsNullOrWhiteSpace(model.TaxRegistrationNumber)) sb.Append("<div class=\"muted\">Tax registration: ").Append(E(model.TaxRegistrationNumber)).Append("</div>");
			if (!string.IsNullOrWhiteSpace(model.SecondaryTaxRegistrationNumber)) sb.Append("<div class=\"muted\">Secondary tax registration: ").Append(E(model.SecondaryTaxRegistrationNumber)).Append("</div>");
			sb.Append("</td><td style=\"text-align:right\">");
			sb.Append("<h1>INVOICE</h1><table class=\"meta\" style=\"width:auto;margin-left:auto\">");
			sb.Append("<tr><td class=\"muted\">Invoice #</td><td class=\"num\"><strong>").Append(invoice.InvoiceNumber).Append("</strong></td></tr>");
			sb.Append("<tr><td class=\"muted\">Status</td><td class=\"num\"><span class=\"status\">").Append(E(StatusLabel(invoice.Status))).Append("</span></td></tr>");
			if (invoice.IssuedOn.HasValue) sb.Append("<tr><td class=\"muted\">Issued</td><td class=\"num\">").Append(invoice.IssuedOn.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("</td></tr>");
			if (invoice.DueOn.HasValue) sb.Append("<tr><td class=\"muted\">Due</td><td class=\"num\">").Append(invoice.DueOn.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("</td></tr>");
			sb.Append("<tr><td class=\"muted\">Currency</td><td class=\"num\">").Append(E(currency)).Append("</td></tr>");
			sb.Append("</table></td></tr></table>");

			sb.Append("<h2>Bill to</h2><div>").Append(E(model.CustomerName ?? string.Empty)).Append("</div>");
			if (model.BillTo != null) sb.Append("<div class=\"muted\">").Append(E(FormatAddress(model.BillTo))).Append("</div>");
			if (!string.IsNullOrWhiteSpace(model.CustomerEmail)) sb.Append("<div class=\"muted\">").Append(E(model.CustomerEmail)).Append("</div>");

			sb.Append("<h2>Items</h2><table><thead><tr><th>Description</th><th class=\"num\">Qty</th><th class=\"num\">Rate</th><th class=\"num\">Amount</th></tr></thead><tbody>");
			foreach (var line in (invoice.LineItems ?? new List<InvoiceLineItem>()).OrderBy(x => x.SortOrder))
			{
				sb.Append("<tr class=\"line\"><td>").Append(E(line.Description)).Append(line.Taxable ? string.Empty : " <span class=\"muted\">(non-taxable)</span>")
				  .Append("</td><td class=\"num\">").Append(line.Quantity.ToString("0.####", CultureInfo.InvariantCulture))
				  .Append("</td><td class=\"num\">").Append(FormatMoney(line.UnitRate, currency, 4))
				  .Append("</td><td class=\"num\">").Append(FormatMoney(line.Amount, currency)).Append("</td></tr>");
			}
			sb.Append("</tbody></table>");

			sb.Append("<table class=\"totals\">");
			sb.Append("<tr><td>Subtotal</td><td class=\"num\">").Append(FormatMoney(invoice.SubTotal, currency)).Append("</td></tr>");
			if (invoice.DiscountAmount > 0)
				sb.Append("<tr><td>Discount").Append(invoice.DiscountPercent.HasValue ? $" ({invoice.DiscountPercent.Value.ToString("0.##", CultureInfo.InvariantCulture)}%)" : string.Empty).Append("</td><td class=\"num\">-").Append(FormatMoney(invoice.DiscountAmount, currency)).Append("</td></tr>");
			if (model.TaxComponents != null && model.TaxComponents.Count > 0)
			{
				foreach (var component in model.TaxComponents)
				{
					sb.Append("<tr><td>").Append(E(component.Name)).Append(" (").Append(component.RatePercent.ToString("0.##", CultureInfo.InvariantCulture)).Append("%)");
					if (!string.IsNullOrWhiteSpace(component.RegistrationNumber)) sb.Append(" <span class=\"muted\">").Append(E(component.RegistrationNumber)).Append("</span>");
					sb.Append("</td><td class=\"num\">").Append(FormatMoney(component.Amount ?? 0m, currency)).Append("</td></tr>");
				}
			}
			else if (invoice.TaxAmount > 0)
				sb.Append("<tr><td>Tax</td><td class=\"num\">").Append(FormatMoney(invoice.TaxAmount, currency)).Append("</td></tr>");
			sb.Append("<tr class=\"grand\"><td>Total</td><td class=\"num\">").Append(FormatMoney(invoice.Total, currency)).Append("</td></tr>");
			if (invoice.AmountPaid > 0)
			{
				sb.Append("<tr><td>Paid</td><td class=\"num\">-").Append(FormatMoney(invoice.AmountPaid, currency)).Append("</td></tr>");
				sb.Append("<tr class=\"grand\"><td>Balance due</td><td class=\"num\">").Append(FormatMoney(invoice.Balance, currency)).Append("</td></tr>");
			}
			sb.Append("</table>");

			if (!string.IsNullOrWhiteSpace(invoice.TermsText)) sb.Append("<h2>Terms</h2><div class=\"footer\">").Append(E(invoice.TermsText)).Append("</div>");
			if (!string.IsNullOrWhiteSpace(invoice.Notes) && !invoice.IsProtected) sb.Append("<h2>Notes</h2><div class=\"footer\">").Append(E(invoice.Notes)).Append("</div>");
			if (!string.IsNullOrWhiteSpace(model.PayUrl)) sb.Append("<p><a href=\"").Append(E(model.PayUrl)).Append("\">Pay this invoice online</a></p>");
			if (!string.IsNullOrWhiteSpace(model.FooterText)) sb.Append("<div class=\"footer\">").Append(E(model.FooterText)).Append("</div>");
			sb.Append("</body></html>");
			return sb.ToString();
		}

		private static string StatusLabel(int status) => status switch
		{
			(int)InvoiceStatus.PartiallyPaid => "Partially paid",
			_ => ((InvoiceStatus)status).ToString()
		};

		public static string FormatMoney(decimal amount, string currency, int decimals = 2) =>
			$"{amount.ToString("N" + decimals, CultureInfo.InvariantCulture)} {(currency ?? "USD").ToUpperInvariant()}";

		private static string FormatAddress(Address address)
		{
			if (address == null) return string.Empty;
			var parts = new[] { address.Address1, string.Join(" ", new[] { address.City, address.State, address.PostalCode }.Where(x => !string.IsNullOrWhiteSpace(x))), address.Country }
				.Where(x => !string.IsNullOrWhiteSpace(x));
			return string.Join(", ", parts);
		}

		private static string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
	}

	/// <summary>Everything the HTML renderer needs; assembled by RenderInvoiceHtmlAsync, hand-built in tests.</summary>
	public class InvoiceRenderModel
	{
		public Invoice Invoice { get; set; }
		public string DepartmentName { get; set; }
		public Address RemitTo { get; set; }
		public string TaxRegistrationNumber { get; set; }
		public string SecondaryTaxRegistrationNumber { get; set; }
		public string FooterText { get; set; }
		public string CustomerName { get; set; }
		public string CustomerEmail { get; set; }
		public Address BillTo { get; set; }
		public List<TaxComponent> TaxComponents { get; set; }
		/// <summary>Phase B2: the pay-page link; null until online payments are enabled.</summary>
		public string PayUrl { get; set; }
	}
}
