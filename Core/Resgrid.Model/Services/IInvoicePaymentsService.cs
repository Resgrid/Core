using System.Threading.Tasks;
using Resgrid.Model.Invoicing;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Online payment collection on Resgrid invoices through a department's own Stripe account (Workforce &amp;
	/// Business Operations plan, Phase B2). Scaffolded 2026-09-18 with the health read only; the connect, pay-link,
	/// webhook-apply and reconciliation members arrive with migration M0212 and the Phase B invoicing tables.
	/// </summary>
	public interface IInvoicePaymentsService
	{
		/// <summary>
		/// Health of the Stripe Connect webhook path for the v4 Health endpoint (plan B2.5a). Never throws; every
		/// field falls back to its safe value, and a cluster with payment collection off reports healthy.
		/// </summary>
		Task<PaymentsWebhookHealth> GetWebhookHealthAsync();
	}
}
