using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Controllers
{
	/// <summary>
	/// Public protocol pages (Protocols P5, decision 6): /public/protocols/{token} shows one protocol's version in force (or a category as a set)
	/// to anyone, and /public/protocols/{token}/embed is the same page for an iframe. Opt-in and revocable per link; the service re-checks every
	/// protocol on every request, so drafts, audience-restricted and licensed content never show. Strict CSP: no scripts, no frames, this site's
	/// styles only; the page itself is never framed (frame-ancestors 'none', X-Frame-Options DENY) and the embed route allows only the link's
	/// origins (any https site when none are listed). Unknown and revoked tokens are a plain 404. No cookies, noindex, short public caching.
	/// </summary>
	[AllowAnonymous]
	[Route("public/protocols")]
	public class PublicProtocolsController : Controller
	{
		public const int RequestsPerMinute = 120;
		private static readonly PayPageRateLimiter Limiter = new PayPageRateLimiter();

		private readonly IProtocolPublicPageService _pages;

		public PublicProtocolsController(IProtocolPublicPageService pages)
		{
			_pages = pages;
		}

		[HttpGet("{token}")]
		public async Task<IActionResult> Index(string token, string lang = null)
		{
			if (!Limiter.Allow(IpAddressHelper.GetRequestIP(Request, true) ?? "unknown", RequestsPerMinute, DateTime.UtcNow))
				return StatusCode(429);

			var page = await LoadAsync(token, lang);
			Secure(ProtocolPublicPages.PageCsp, frameable: false);
			if (page == null)
				return NotFound();

			ViewData["Embedded"] = false;
			return View("~/Views/PublicProtocols/Index.cshtml", page);
		}

		[HttpGet("{token}/embed")]
		public async Task<IActionResult> Embed(string token, string lang = null)
		{
			if (!Limiter.Allow(IpAddressHelper.GetRequestIP(Request, true) ?? "unknown", RequestsPerMinute, DateTime.UtcNow))
				return StatusCode(429);

			PublicProtocolEmbedPolicy policy;
			try
			{
				policy = await _pages.GetEmbedPolicyAsync(token);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Public protocol embed policy failed.");
				policy = null;
			}

			if (policy == null || !policy.Enabled)
			{
				Secure(ProtocolPublicPages.PageCsp, frameable: false);
				return NotFound();
			}

			var page = await LoadAsync(token, lang);
			Secure(ProtocolPublicPages.EmbedCsp(policy.FrameAncestors), frameable: true);
			if (page == null)
				return NotFound();

			ViewData["Embedded"] = true;
			return View("~/Views/PublicProtocols/Index.cshtml", page);
		}

		private async Task<PublicProtocolPage> LoadAsync(string token, string lang)
		{
			if (!ProtocolPublicPages.IsTokenShape(token))
				return null;
			try
			{
				var locale = string.IsNullOrWhiteSpace(lang) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : lang.Trim();
				return await _pages.GetPageAsync(token, locale.Length > 16 ? null : locale);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Public protocol page failed.");
				return null;
			}
		}

		/// <summary>The headers every answer carries (the 404s too); replaces the site-wide frame-ancestors 'self'.</summary>
		private void Secure(string csp, bool frameable)
		{
			Response.Headers["Content-Security-Policy"] = csp;
			if (frameable)
				Response.Headers.Remove("X-Frame-Options");
			else
				Response.Headers["X-Frame-Options"] = "DENY";
			Response.Headers["Referrer-Policy"] = "no-referrer";
			Response.Headers["X-Content-Type-Options"] = "nosniff";
			Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
			// A revoked link must stop showing quickly: shared caches keep a page for a minute at most.
			Response.Headers["Cache-Control"] = "public, max-age=60";
		}
	}
}
