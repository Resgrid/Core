using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Controllers
{
	/// <summary>
	/// Round 10 A8: the in-app launch page of SCORM 1.2 / 2004, cmi5 and LTI 1.3 lessons (/lms-app/...), which the Omni app opens in its web view
	/// from the single-use launch link of v4 Lms/GetLessonLaunch. Anonymous and cookie-free: the link's token is exchanged once for a page
	/// session bound to one member, enrollment and lesson, whose token the page carries in its own form fields and state requests (never in a
	/// URL, never a sign-in: no claims principal, no cookie, so no other page or API accepts it). This controller depends on the app launch
	/// service and the strings only (a guard test pins its constructor). Per-address limits on every endpoint; strict policy (this site's script,
	/// stylesheet, frames and requests only), framed only by the configured app origins. Offline is not supported.
	/// </summary>
	[AllowAnonymous]
	[Route("lms-app")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class LmsAppController : Controller
	{
		/// <summary>Launch links exchanged, items relaunched and LTI launches started per address per minute.</summary>
		public const int LaunchesPerMinute = 30;

		/// <summary>State requests per address per minute (a page asks every few seconds; a station's members may share an address).</summary>
		public const int StateRequestsPerMinute = 600;

		public static readonly LmsAppLaunchLimiter Limiter = new LmsAppLaunchLimiter();

		private readonly ILmsAppLaunchService _apps;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.LmsStandards.LmsStandards> _localizer;

		public LmsAppController(ILmsAppLaunchService apps, IStringLocalizer<Resgrid.Localization.Areas.User.LmsStandards.LmsStandards> localizer)
		{
			_apps = apps;
			_localizer = localizer;
		}

		/// <summary>The clock of the rate limits (a seam for tests).</summary>
		public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

		private string L(string key)
		{
			var value = _localizer?[key];
			return value == null || value.ResourceNotFound || string.IsNullOrEmpty(value.Value) ? key : value.Value;
		}

		private string Address()
		{
			try
			{
				return IpAddressHelper.GetRequestIP(Request, true) ?? "unknown";
			}
			catch (Exception)
			{
				// No address to be found (the helper throws): one shared bucket rather than a failed page.
				return "unknown";
			}
		}

		private bool Allow(string scope, int perMinute) => Limiter.Allow(scope + "|" + Address(), perMinute, Clock());

		private string SiteBase() => Request.Scheme + "://" + Request.Host + Request.PathBase;

		/// <summary>The language the page was asked in (?ui-culture=), kept on its forms.</summary>
		private string AskedCulture()
		{
			var asked = Request.Query["ui-culture"].ToString();
			return string.IsNullOrWhiteSpace(asked) ? null : CultureInfo.CurrentUICulture.Name;
		}

		/// <summary>The headers every page carries (the refusals too).</summary>
		private IReadOnlyCollection<string> Secure()
		{
			var origins = LmsAppLaunchRules.FrameOrigins();
			Response.Headers["Content-Security-Policy"] = LmsAppPages.PolicyFor(origins);
			// X-Frame-Options cannot name several origins: it is dropped when the policy's frame-ancestors lists the app origins.
			if (origins.Count == 0)
				Response.Headers["X-Frame-Options"] = "DENY";
			else
				Response.Headers.Remove("X-Frame-Options");
			Response.Headers["Referrer-Policy"] = "no-referrer";
			Response.Headers["X-Content-Type-Options"] = "nosniff";
			Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
			Response.Headers["Cache-Control"] = "no-store";
			return origins;
		}

		private ContentResult Html(int status, string html) => new ContentResult { StatusCode = status, ContentType = "text/html; charset=utf-8", Content = html };

		private IActionResult Refusal(int status, LmsAppPage page, IReadOnlyCollection<string> origins)
		{
			var error = page?.Error ?? Resgrid.Model.Lms.LmsErrors.CodeExpired;
			return Html(status, LmsAppPages.Notice(page?.LessonTitle ?? "Resgrid", L("Std.Error." + error), LmsAppPages.ModeError, page?.State, origins, L));
		}

		private IActionResult Busy(IReadOnlyCollection<string> origins) =>
			Html(429, LmsAppPages.Notice("Resgrid", L("Std.App.Busy"), LmsAppPages.ModeError, null, origins, L));

		/// <summary>
		/// Opens the launch link: exchanges (consumes) its token and shows the lesson's runtime. A used, expired or unknown link, or a lesson that
		/// may not run any more, is a page that says so (and posts the refusal to the app when it names a lesson).
		/// </summary>
		[HttpGet("launch")]
		public async Task<IActionResult> Launch([FromQuery] string token, CancellationToken cancellationToken)
		{
			var origins = Secure();
			if (!Allow("launch", LaunchesPerMinute))
				return Busy(origins);
			LmsAppPage page;
			try
			{
				page = await _apps.ExchangeAsync(token, SiteBase(), Address(), Request.Headers["User-Agent"].ToString(), cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "An in-app LMS launch could not be opened.");
				page = new LmsAppPage { Error = Resgrid.Model.Lms.LmsErrors.MediaUnavailable };
			}
			if (page == null || !page.Succeeded)
				return Refusal(page?.Error == Resgrid.Model.Lms.LmsErrors.CodeExpired ? 410 : 403, page, origins);
			return Html(200, LmsAppPages.Player(page, L, origins, AskedCulture()));
		}

		/// <summary>Relaunches a SCO or AU of the page session's lesson (the item menu of a multi-item package).</summary>
		[HttpPost("item")]
		[IgnoreAntiforgeryToken] // No cookie authenticates this request: the page session token in the body is the only authority.
		[RequestSizeLimit(16 * 1024)]
		public async Task<IActionResult> Item([FromForm] string session, [FromForm] string item, CancellationToken cancellationToken)
		{
			var origins = Secure();
			if (!Allow("launch", LaunchesPerMinute))
				return Busy(origins);
			var page = await _apps.OpenItemAsync(session, item, SiteBase(), cancellationToken);
			if (page == null || !page.Succeeded)
				return Refusal(page?.Error == Resgrid.Model.Lms.LmsErrors.CodeExpired ? 410 : 403, page, origins);
			return Html(200, LmsAppPages.Player(page, L, origins, AskedCulture()));
		}

		/// <summary>Starts the page session's LTI lesson: the web view goes to the tool's login initiation (OIDC third-party-initiated login).</summary>
		[HttpPost("lti")]
		[IgnoreAntiforgeryToken] // No cookie authenticates this request: the page session token in the body is the only authority.
		[RequestSizeLimit(16 * 1024)]
		public async Task<IActionResult> Lti([FromForm] string session, CancellationToken cancellationToken)
		{
			var origins = Secure();
			if (!Allow("launch", LaunchesPerMinute))
				return Busy(origins);
			var step = await _apps.BeginLtiAsync(session, cancellationToken);
			if (step == null || !step.Succeeded || !LmsStandardsUrls.IsAcceptableHttpsUrl(step.Url))
				return Refusal(step?.Error == Resgrid.Model.Lms.LmsErrors.CodeExpired ? 410 : 403, new LmsAppPage { Error = step?.Error ?? Resgrid.Model.Lms.LmsErrors.MediaUnavailable }, origins);
			return Redirect(step.Url);
		}

		/// <summary>
		/// The page session's lesson state (the completion message, JSON). Same-origin only (no CORS headers): the page asks with its session token
		/// in the body. 401 with status unavailable / CodeExpired once the session is unknown or over.
		/// </summary>
		[HttpPost("state")]
		[IgnoreAntiforgeryToken] // No cookie authenticates this request: the page session token in the body is the only authority.
		[RequestSizeLimit(4 * 1024)]
		public async Task<IActionResult> State([FromForm] string session)
		{
			Response.Headers["Cache-Control"] = "no-store";
			Response.Headers["X-Content-Type-Options"] = "nosniff";
			if (!Allow("state", StateRequestsPerMinute))
				return StatusCode(429);
			var state = await _apps.GetStateAsync(session);
			if (state == null)
				return new ContentResult
				{
					StatusCode = 401,
					ContentType = "application/json",
					Content = new LmsAppLessonMessage { Status = LmsAppLaunchRules.StatusUnavailable, Error = nameof(Resgrid.Model.Lms.LmsErrors.CodeExpired) }.ToJson()
				};
			return new ContentResult { StatusCode = 200, ContentType = "application/json", Content = state.ToJson() };
		}

		/// <summary>
		/// The LTI tool's return (launch_presentation return_url of an in-app launch): a signed link that shows that one lesson's state and posts
		/// it to the app; a popup closes itself on the button.
		/// </summary>
		[HttpGet("return")]
		public async Task<IActionResult> Return([FromQuery(Name = LmsAppLaunchRules.ReturnDepartmentParameter)] string d, [FromQuery(Name = LmsAppLaunchRules.ReturnSessionParameter)] string a,
			[FromQuery(Name = LmsAppLaunchRules.ReturnExpiresParameter)] string e, [FromQuery(Name = LmsAppLaunchRules.ReturnSignatureParameter)] string sig)
		{
			var origins = Secure();
			if (!Allow("state", StateRequestsPerMinute))
				return Busy(origins);
			if (!LmsAppLaunchRules.TryValidateReturn(d, a, e, sig, DateTime.UtcNow, out var departmentId, out var appSessionId))
				return Refusal(410, null, origins);
			var state = await _apps.GetReturnStateAsync(departmentId, appSessionId);
			if (state == null)
				return Refusal(410, null, origins);
			return Html(200, LmsAppPages.Notice("Resgrid", L("Std.App.Returned"), LmsAppPages.ModeDone, state, origins, L, closeButton: true));
		}
	}
}
