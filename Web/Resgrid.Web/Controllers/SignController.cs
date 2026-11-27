using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Controllers
{
	/// <summary>
	/// The Resgrid signing page for external signers (e-Signature S2, plan §22.3): anonymous, rate limited, never cached, framed by
	/// nobody. /Sign/{token} only shows the landing step (mail scanners prefetch links); continuing exchanges the single-use link
	/// for a short signing session held in an HttpOnly cookie. Consent to do business electronically, then a Resgrid one-time code
	/// by e-mail or SMS; only then does the page embed the e-sign platform's signer (a plain iframe on the platform's origin — the
	/// repo pins no external scripts on this page; the frame-src allows that one origin). Department branding wraps it.
	/// </summary>
	[AllowAnonymous]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Filters.AllowDuringDepartmentLock]
	public class SignController : Controller
	{
		public const string SessionCookie = "rg-esign";
		private const string RateLimitCachePrefix = "ESignPage:";
		private static readonly PayPageRateLimiter Limiter = new PayPageRateLimiter();

		private readonly ISignatureRequestService _requests;
		private readonly ICacheProvider _cache;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Signatures.Signatures> _text;

		public SignController(ISignatureRequestService requests, ICacheProvider cache, IStringLocalizer<Resgrid.Localization.Areas.User.Signatures.Signatures> text)
		{
			_requests = requests;
			_cache = cache;
			_text = text;
		}

		[HttpGet("Sign/{token}")]
		public async Task<IActionResult> Index(string token)
		{
			if (await ThrottledAsync()) return TooMany();
			var state = await _requests.OpenLinkAsync(token);
			return Page(state, token);
		}

		[HttpPost("Sign/{token}")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Start(string token, CancellationToken cancellationToken)
		{
			if (await ThrottledAsync()) return TooMany();
			var (state, session) = await _requests.StartSessionAsync(token, Ip, cancellationToken);
			if (session == null)
				return Page(state, null);

			Response.Cookies.Append(SessionCookie, session, new CookieOptions
			{
				HttpOnly = true,
				Secure = Request.IsHttps,
				SameSite = SameSiteMode.Strict,
				Path = "/Sign",
				IsEssential = true,
				Expires = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(ESignConfig.SessionMinutes, 5, 240))
			});
			return Redirect("/Sign/Session/Step");
		}

		[HttpGet("Sign/Session/Step")]
		public async Task<IActionResult> Step()
		{
			if (await ThrottledAsync()) return TooMany();
			return Page(await _requests.GetSessionStateAsync(Session), null);
		}

		[HttpPost("Sign/Session/Consent")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Consent(string consentVersion, bool agree, CancellationToken cancellationToken)
		{
			if (await ThrottledAsync()) return TooMany();
			if (!agree)
				return Page(await _requests.GetSessionStateAsync(Session), null, _text["ConsentRequired"].Value);
			var state = await _requests.ConsentAsync(Session, consentVersion, Ip, cancellationToken);
			if (state.Step == SigningPageSteps.Code && !state.CodeSent)
				state = await _requests.SendCodeAsync(Session, CultureInfo.CurrentUICulture.Name, cancellationToken);
			return Page(state, null);
		}

		[HttpPost("Sign/Session/Code")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SendCode(CancellationToken cancellationToken)
		{
			if (await ThrottledAsync()) return TooMany();
			var state = await _requests.SendCodeAsync(Session, CultureInfo.CurrentUICulture.Name, cancellationToken);
			return Page(state, null, state.Refusal == SignatureRequestRefusals.None ? null : _text["CodeNotSent"].Value);
		}

		[HttpPost("Sign/Session/Verify")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Verify(string code, CancellationToken cancellationToken)
		{
			if (await ThrottledAsync()) return TooMany();
			var state = await _requests.VerifyCodeAsync(Session, code, cancellationToken);
			return Page(state, null, state.Step == SigningPageSteps.Code && state.Refusal != SignatureRequestRefusals.None ? _text["CodeWrong"].Value : null);
		}

		[HttpPost("Sign/Session/Done")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Done(CancellationToken cancellationToken)
		{
			if (await ThrottledAsync()) return TooMany();
			var state = await _requests.CheckCompletionAsync(Session, cancellationToken);
			return Page(state, null, state.Step == SigningPageSteps.Sign ? _text["SigningBeingConfirmed"].Value : null);
		}

		private string Session => Request.Cookies[SessionCookie];

		private string Ip => IpAddressHelper.GetRequestIP(Request, true);

		private IActionResult Page(SigningPageState state, string linkToken, string notice = null)
		{
			// The page holds a one-time code form and (after the gate) the signer's embed: nobody frames it, nothing caches it,
			// scripts are this site's own, and the only frame allowed is the e-sign platform's origin.
			var frame = string.IsNullOrWhiteSpace(state?.EmbedOrigin) ? "'none'" : state.EmbedOrigin;
			Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
				"frame-src " + frame + "; frame-ancestors 'none'; form-action 'self'; base-uri 'none'; object-src 'none'";
			Response.Headers["X-Frame-Options"] = "DENY";
			Response.Headers["Referrer-Policy"] = "no-referrer";
			Response.Headers["X-Content-Type-Options"] = "nosniff";
			Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
			ViewData["LinkToken"] = linkToken;
			ViewData["Notice"] = notice;
			ViewData["ConsentText"] = _text["ConsentText"].Value;
			return View("~/Views/Sign/Index.cshtml", state ?? new SigningPageState { Step = SigningPageSteps.Closed });
		}

		private async Task<bool> ThrottledAsync()
		{
			var ip = Ip ?? "unknown";
			var perMinute = Math.Max(1, ESignConfig.SigningPageRateLimitPerMinute);
			var now = DateTime.UtcNow;
			var window = now.Ticks / TimeSpan.TicksPerMinute;
			var count = await _cache.IncrementAsync($"{RateLimitCachePrefix}{Hash(ip)}:{window}", TimeSpan.FromMinutes(2));
			if (count > 0)
				return count > perMinute;
			return !Limiter.Allow(ip, perMinute, now);
		}

		private static string Hash(string value) =>
			Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

		private IActionResult TooMany()
		{
			Response.Headers["Retry-After"] = "60";
			return StatusCode(429, _text["TooManyRequests"].Value);
		}
	}
}
