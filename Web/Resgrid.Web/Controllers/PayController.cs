using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Config;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Controllers
{
	/// <summary>
	/// The anonymous pay page (Workforce &amp; Business Operations plan, B2.5): /pay/{token}. Shows the department's
	/// name, the invoice number, the amount due and the due date; nothing else about the customer or the invoice.
	/// "Pay" opens the hosted provider page server-side and redirects; the return and cancel pages confirm nothing
	/// and never mark anything paid — only the verified webhook does. No-store, noindex (the layout), rate-limited
	/// per IP by PaymentConnectConfig.PayPageRateLimitPerMinute. A token never grants access to the invoice itself.
	/// </summary>
	[AllowAnonymous]
	[Route("pay")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Filters.AllowDuringDepartmentLock]
	public class PayController : Controller
	{
		private static readonly PayPageRateLimiter Limiter = new PayPageRateLimiter();

		private readonly IInvoicePaymentsService _payments;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Invoicing.Invoicing> _strings;

		public PayController(IInvoicePaymentsService payments, IStringLocalizer<Resgrid.Localization.Areas.User.Invoicing.Invoicing> strings)
		{
			_payments = payments;
			_strings = strings;
		}

		[HttpGet("{token}")]
		public async Task<IActionResult> Index(string token)
		{
			if (Throttled()) return TooMany();
			var model = await _payments.GetPayPageModelAsync(token);
			ViewData["PayToken"] = token;
			return View("Index", model);
		}

		[HttpPost("{token}")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Start(string token, CancellationToken cancellationToken)
		{
			if (Throttled()) return TooMany();
			var model = await _payments.GetPayPageModelAsync(token);
			ViewData["PayToken"] = token;
			if (!model.Available)
				return View("Index", model);

			try
			{
				var request = await _payments.CreatePaymentRequestAsync(model.InvoiceId, model.DepartmentId, (int)PaymentRequestSources.PayPage, null,
					IpAddressHelper.GetRequestIP(Request, true), Request.Headers["User-Agent"].ToString(), cancellationToken);
				if (Uri.TryCreate(request.HostedUrl, UriKind.Absolute, out var hosted) && hosted.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(hosted.UserInfo))
					return Redirect(hosted.AbsoluteUri);
				model.Available = false;
				model.UnavailableReason = "pay_not_offered";
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("payments_", StringComparison.Ordinal) || ex.Message.StartsWith("invoicing_", StringComparison.Ordinal))
			{
				model.Available = false;
				model.UnavailableReason = ex.Message == "payments_invoice_paid" ? "pay_invoice_paid" : "pay_not_offered";
			}
			return View("Index", model);
		}

		[HttpGet("{token}/return")]
		public async Task<IActionResult> Return(string token)
		{
			if (Throttled()) return TooMany();
			var model = await _payments.GetPayPageModelAsync(token);
			ViewData["PayToken"] = token;
			return View("Return", model);
		}

		[HttpGet("{token}/cancel")]
		public async Task<IActionResult> Cancel(string token)
		{
			if (Throttled()) return TooMany();
			var model = await _payments.GetPayPageModelAsync(token);
			ViewData["PayToken"] = token;
			return View("Cancel", model);
		}

		private bool Throttled()
		{
			var ip = IpAddressHelper.GetRequestIP(Request, true) ?? "unknown";
			return !Limiter.Allow(ip, Math.Max(1, PaymentConnectConfig.PayPageRateLimitPerMinute), DateTime.UtcNow);
		}

		private IActionResult TooMany()
		{
			Response.Headers["Retry-After"] = "60";
			return StatusCode(429, _strings["TooManyRequests"].Value);
		}
	}

	/// <summary>In-process sliding-window limiter for the anonymous pay page: N requests per IP per minute. Small, self-pruning, no external state.</summary>
	public sealed class PayPageRateLimiter
	{
		private readonly ConcurrentDictionary<string, Queue<DateTime>> _hits = new ConcurrentDictionary<string, Queue<DateTime>>();
		private DateTime _lastPrune = DateTime.MinValue;

		public bool Allow(string key, int perMinute, DateTime now)
		{
			var window = now.AddMinutes(-1);
			var queue = _hits.GetOrAdd(key, _ => new Queue<DateTime>());
			lock (queue)
			{
				while (queue.Count > 0 && queue.Peek() < window) queue.Dequeue();
				if (queue.Count >= perMinute) return false;
				queue.Enqueue(now);
			}
			Prune(now);
			return true;
		}

		private void Prune(DateTime now)
		{
			if (now - _lastPrune < TimeSpan.FromMinutes(5)) return;
			_lastPrune = now;
			var window = now.AddMinutes(-1);
			foreach (var pair in _hits)
			{
				lock (pair.Value)
				{
					while (pair.Value.Count > 0 && pair.Value.Peek() < window) pair.Value.Dequeue();
					if (pair.Value.Count == 0) _hits.TryRemove(pair.Key, out _);
				}
			}
		}
	}
}
