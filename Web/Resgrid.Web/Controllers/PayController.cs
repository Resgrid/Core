using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Config;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Controllers
{
	/// <summary>
	/// The anonymous pay page (Workforce &amp; Business Operations plan, B2.5): /pay/{token}. Shows the department's
	/// name, the invoice number, the amount due and the due date; nothing else about the customer or the invoice.
	/// "Pay" opens the hosted provider page server-side and redirects; the return and cancel pages confirm nothing
	/// and never mark anything paid — only the verified webhook does. No-store, noindex (the layout), rate-limited
	/// per IP by PaymentConnectConfig.PayPageRateLimitPerMinute on a counter shared by every web process (the cache
	/// provider, as the password-recovery and SSO limits are), with the in-process limiter only while that counter is
	/// unavailable. A token never grants access to the invoice itself.
	/// </summary>
	[AllowAnonymous]
	[Route("pay")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Filters.AllowDuringDepartmentLock]
	public class PayController : Controller
	{
		private const string RateLimitCachePrefix = "payments:pay-page:rate:";
		private static readonly PayPageRateLimiter Limiter = new PayPageRateLimiter();

		private readonly IInvoicePaymentsService _payments;
		private readonly ICacheProvider _cacheProvider;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Invoicing.Invoicing> _strings;

		public PayController(IInvoicePaymentsService payments, ICacheProvider cacheProvider, IStringLocalizer<Resgrid.Localization.Areas.User.Invoicing.Invoicing> strings)
		{
			_payments = payments;
			_cacheProvider = cacheProvider;
			_strings = strings;
		}

		[HttpGet("{token}")]
		public async Task<IActionResult> Index(string token)
		{
			if (await ThrottledAsync()) return TooMany();
			var model = await _payments.GetPayPageModelAsync(token);
			ViewData["PayToken"] = token;
			return View("Index", model);
		}

		[HttpPost("{token}")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Start(string token, CancellationToken cancellationToken)
		{
			if (await ThrottledAsync()) return TooMany();
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
			if (await ThrottledAsync()) return TooMany();
			var model = await _payments.GetPayPageModelAsync(token);
			ViewData["PayToken"] = token;
			return View("Return", model);
		}

		[HttpGet("{token}/cancel")]
		public async Task<IActionResult> Cancel(string token)
		{
			if (await ThrottledAsync()) return TooMany();
			var model = await _payments.GetPayPageModelAsync(token);
			ViewData["PayToken"] = token;
			return View("Cancel", model);
		}

		private async Task<bool> ThrottledAsync()
		{
			var ip = IpAddressHelper.GetRequestIP(Request, true) ?? "unknown";
			var perMinute = Math.Max(1, PaymentConnectConfig.PayPageRateLimitPerMinute);
			var now = DateTime.UtcNow;

			// Fixed one-minute window keyed by a hash of the address (no raw addresses in cache keys); the key expires
			// with the window. IncrementAsync returns 0 only when the cache is disabled or unreachable, and then the
			// process-local limiter still bounds this instance rather than failing the pay page closed.
			var window = now.Ticks / TimeSpan.TicksPerMinute;
			var count = await _cacheProvider.IncrementAsync($"{RateLimitCachePrefix}{Hash(ip)}:{window}", TimeSpan.FromMinutes(2));
			if (count > 0)
				return count > perMinute;
			return !Limiter.Allow(ip, perMinute, now);
		}

		private static string Hash(string value) =>
			Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

		private IActionResult TooMany()
		{
			Response.Headers["Retry-After"] = "60";
			return StatusCode(429, _strings["TooManyRequests"].Value);
		}
	}

	/// <summary>In-process sliding-window limiter for the anonymous pay page: N requests per IP per minute. Small, self-pruning; the fallback while the shared counter is unavailable.</summary>
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
