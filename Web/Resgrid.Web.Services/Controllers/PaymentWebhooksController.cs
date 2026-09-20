using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Controllers
{
	/// <summary>
	/// Connect webhook receiver for department-connected payment accounts (Workforce &amp; Business Operations plan,
	/// B2.5). A sibling of the SaaS StripeHandlerController that never shares its secret: this endpoint is registered
	/// at Stripe as a Connect endpoint with its own signing secret. 503 while PaymentConnectConfig.Enabled is false
	/// (the EU cluster at launch), 400 on a bad signature or live-mode mismatch (audited, hashed body), 500 when a
	/// verified event failed to apply so the provider retries, 200 once the event is recorded (applied, ignored or
	/// duplicate). Routes for v2 providers arrive with their adapters.
	/// </summary>
	[ApiController]
	[Route("api/PaymentWebhooks")]
	[ApiExplorerSettings(IgnoreApi = true)]
	[AllowAnonymous]
	public class PaymentWebhooksController : ControllerBase
	{
		private readonly IInvoicePaymentsService _payments;

		public PaymentWebhooksController(IInvoicePaymentsService payments)
		{
			_payments = payments;
		}

		[HttpPost("stripe")]
		public async Task<IActionResult> Stripe(CancellationToken cancellationToken)
		{
			if (!PaymentConnectConfig.Enabled)
				return StatusCode(503);

			string body;
			using (var reader = new StreamReader(HttpContext.Request.Body, Encoding.UTF8))
				body = await reader.ReadToEndAsync(cancellationToken);

			// An unsigned or empty POST cannot be a Stripe delivery; it is refused here rather than parsed, recorded as a
			// rejected event and stored (which every signed-but-invalid delivery still is).
			var signature = Request.Headers["Stripe-Signature"].ToString();
			if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(body))
				return BadRequest();

			var receipt = await _payments.ReceiveWebhookAsync((int)PaymentProviders.Stripe, signature, body,
				HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

			if (receipt.Accepted)
				return Ok();
			if (receipt.Error == "payments_disabled")
				return StatusCode(503);
			return receipt.Outcome == PaymentEventOutcomes.Failed ? StatusCode(500) : BadRequest();
		}
	}
}
