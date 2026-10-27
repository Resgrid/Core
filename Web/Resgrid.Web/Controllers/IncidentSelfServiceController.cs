using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Controllers
{
	/// <summary>
	/// The anonymous Incident Back Office self-service page (large-incident plan §9.28, D17): /incident-self-service/{token}. Shows one
	/// participant their own support information — status, check-in code, positions, meal windows, and lodging after a one-time code
	/// when the department requires it. An unknown, revoked or expired token looks the same as any other unavailable link. No-store,
	/// noindex (the layout), rate-limited per IP on the shared counter with the in-process limiter as the fallback (the pay page
	/// precedent).
	/// </summary>
	[AllowAnonymous]
	[Route("incident-self-service")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Filters.AllowDuringDepartmentLock]
	public class IncidentSelfServiceController : Controller
	{
		private const string RateLimitCachePrefix = "incident-self-service:rate:";
		private static readonly PayPageRateLimiter Limiter = new PayPageRateLimiter();

		private readonly IIncidentSelfServiceService _selfService;
		private readonly ICacheProvider _cacheProvider;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.IncidentBackOffice.IncidentBackOffice> _strings;

		public IncidentSelfServiceController(IIncidentSelfServiceService selfService, ICacheProvider cacheProvider,
			IStringLocalizer<Resgrid.Localization.Areas.User.IncidentBackOffice.IncidentBackOffice> strings)
		{
			_selfService = selfService;
			_cacheProvider = cacheProvider;
			_strings = strings;
		}

		[HttpGet("{token}")]
		public async Task<IActionResult> Index(string token, CancellationToken cancellationToken)
		{
			if (await ThrottledAsync()) return TooMany();
			ViewData["SelfServiceToken"] = token;
			if (TempData["SelfServiceMessage"] is string message) ViewData["SelfServiceMessage"] = message;
			return View("Index", await _selfService.GetPageAsync(token, cancellationToken));
		}

		[HttpPost("{token}/code")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SendCode(string token, CancellationToken cancellationToken)
		{
			if (await ThrottledAsync()) return TooMany();
			TempData["SelfServiceMessage"] = await _selfService.SendCodeAsync(token, cancellationToken) ? _strings["SelfServiceCodeSent"].Value : _strings["SelfServiceCodeUnavailable"].Value;
			return Redirect(Url.Action(nameof(Index), new { token }));
		}

		[HttpPost("{token}/verify")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Verify(string token, string code, CancellationToken cancellationToken)
		{
			if (await ThrottledAsync()) return TooMany();
			if (!await _selfService.VerifyCodeAsync(token, code, cancellationToken))
				TempData["SelfServiceMessage"] = _strings["SelfServiceCodeInvalid"].Value;
			return Redirect(Url.Action(nameof(Index), new { token }));
		}

		private async Task<bool> ThrottledAsync()
		{
			var ip = IpAddressHelper.GetRequestIP(Request, true) ?? "unknown";
			var perMinute = Math.Max(1, PaymentConnectConfig.PayPageRateLimitPerMinute);
			var window = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute;
			var count = await _cacheProvider.IncrementAsync($"{RateLimitCachePrefix}{Hash(ip)}:{window}", TimeSpan.FromMinutes(2));
			if (count > 0)
				return count > perMinute;
			return !Limiter.Allow(ip, perMinute, DateTime.UtcNow);
		}

		private static string Hash(string value) =>
			Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

		private IActionResult TooMany()
		{
			Response.Headers["Retry-After"] = "60";
			return StatusCode(429, _strings["SelfServiceTooManyRequests"].Value);
		}
	}
}
