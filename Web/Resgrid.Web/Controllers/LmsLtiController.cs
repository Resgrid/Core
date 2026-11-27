using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Controllers
{
	/// <summary>
	/// The LTI 1.3 platform endpoints of a department (Training T5 item 3): the OIDC authorization endpoint the tool sends the learner's browser
	/// to (authenticated by the launch's single-use message hint, which names the member who started it), the department's public key set, and Assignment and Grade
	/// Services (the client-credentials token endpoint and a lesson's line item and scores). These run on this web site's own address; no
	/// other port is opened (no-inbound-external-ports policy). The tool endpoints are bearer-token only (no cookies, no antiforgery).
	/// </summary>
	[Route("lti")]
	public class LmsLtiController : Controller
	{
		private readonly ILmsLtiService _lti;

		public LmsLtiController(ILmsLtiService lti)
		{
			_lti = lti;
		}

		private static IActionResult Reply(int status, object body) =>
			new ContentResult { StatusCode = status, ContentType = "application/json", Content = body as string ?? JsonConvert.SerializeObject(body) };

		/// <summary>
		/// The tool's OIDC authentication request (third-party-initiated login, step 2). On success the browser posts the signed id_token to the
		/// tool's registered redirect URI (auto-submitted; a button without script). The page allows no framing and no script but ours.
		/// Security review (2026-11-25, LTI SameSite): the member cookie is SameSite=Strict, so the tool's cross-site redirect here arrives
		/// without it. The request is therefore authenticated by the lti_message_hint the launch minted (256 random bits, single use, five
		/// minutes, bound to the department, member, tool and lesson); a member session that does come with it must be that same member.
		/// </summary>
		[AllowAnonymous]
		[HttpGet("authorize")]
		[HttpPost("authorize")]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
		{
			var request = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var pair in Request.Query)
				request[pair.Key] = pair.Value.ToString();
			if (HttpMethods.IsPost(Request.Method) && Request.HasFormContentType)
				foreach (var pair in await Request.ReadFormAsync(cancellationToken))
					request[pair.Key] = pair.Value.ToString();

			// The member session only when one came with the request (same-site); never required.
			var signedIn = User?.Identity?.IsAuthenticated == true;
			var userId = signedIn ? User.FindFirst(ClaimTypes.PrimarySid)?.Value : null;
			var departmentId = signedIn && int.TryParse(User.FindFirst(ClaimTypes.PrimaryGroupSid)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : 0;
			var step = await _lti.AuthorizeAsync(departmentId, string.IsNullOrWhiteSpace(userId) ? null : userId, request, CultureInfo.CurrentUICulture.Name, cancellationToken);
			Response.Headers["X-Frame-Options"] = "DENY";
			Response.Headers["Referrer-Policy"] = "no-referrer";
			if (!step.Succeeded)
			{
				Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'";
				return new ContentResult
				{
					StatusCode = 400,
					ContentType = "text/html; charset=utf-8",
					Content = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Resgrid</title></head><body style=\"font-family:sans-serif;padding:2em\">" +
						"<p>This launch link is no longer valid. Close this window and open the lesson again from Resgrid.</p></body></html>"
				};
			}

			Response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'unsafe-inline'; form-action https:; frame-ancestors 'none'; base-uri 'none'";
			return new ContentResult { StatusCode = 200, ContentType = "text/html; charset=utf-8", Content = AutoPostPage(step) };
		}

		/// <summary>The form post of the id_token (values HTML-encoded), submitted by resgrid.lms.autopost.js.</summary>
		public static string AutoPostPage(LmsLtiLaunchStep step)
		{
			var encoder = HtmlEncoder.Default;
			var html = new StringBuilder();
			html.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Resgrid</title></head><body style=\"font-family:sans-serif;padding:2em\">");
			html.Append("<form id=\"lti-launch\" method=\"post\" action=\"").Append(encoder.Encode(step.Url)).Append("\">");
			foreach (var field in step.Form)
				html.Append("<input type=\"hidden\" name=\"").Append(encoder.Encode(field.Key)).Append("\" value=\"").Append(encoder.Encode(field.Value ?? string.Empty)).Append("\" />");
			html.Append("<noscript><button type=\"submit\">Continue</button></noscript></form>");
			html.Append("<script src=\"/js/app/internal/lms/resgrid.lms.autopost.js\"></script></body></html>");
			return html.ToString();
		}

		/// <summary>The department's public key set (the active platform key and keys retired within a week).</summary>
		[AllowAnonymous]
		[HttpGet("{departmentId:int}/jwks")]
		public async Task<IActionResult> Jwks(int departmentId)
		{
			Response.Headers["Cache-Control"] = "public, max-age=300";
			return Reply(200, await _lti.GetKeySetJsonAsync(departmentId, DateTime.UtcNow));
		}

		/// <summary>AGS token endpoint: client credentials with a signed client assertion (RFC 7523).</summary>
		[AllowAnonymous]
		[HttpPost("{departmentId:int}/token")]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> Token(int departmentId, CancellationToken cancellationToken)
		{
			if (!Request.HasFormContentType)
				return Reply(400, new { error = "invalid_request" });
			var form = (await Request.ReadFormAsync(cancellationToken)).ToDictionary(p => p.Key, p => p.Value.ToString(), StringComparer.Ordinal);
			var result = await _lti.IssueTokenAsync(departmentId, form, cancellationToken);
			if (!result.Succeeded)
				return Reply(result.OAuthError == "invalid_client" ? 401 : 400, new { error = result.OAuthError });
			return Reply(200, new { access_token = result.AccessToken, token_type = "Bearer", expires_in = result.ExpiresIn, scope = result.Scope });
		}

		private string Bearer()
		{
			var header = Request.Headers["Authorization"].ToString();
			return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header.Substring(7).Trim() : null;
		}

		[AllowAnonymous]
		[HttpGet("{departmentId:int}/ags/{lessonId:int}/lineitem")]
		public async Task<IActionResult> LineItem(int departmentId, int lessonId)
		{
			var outcome = await _lti.GetLineItemAsync(departmentId, lessonId, Bearer());
			if (outcome.Status >= 400)
				return StatusCode(outcome.Status);
			return new ContentResult { StatusCode = 200, ContentType = "application/vnd.ims.lis.v2.lineitem+json", Content = outcome.Value as string };
		}

		[AllowAnonymous]
		[HttpPost("{departmentId:int}/ags/{lessonId:int}/lineitem/scores")]
		[RequestSizeLimit(64 * 1024)]
		public async Task<IActionResult> Scores(int departmentId, int lessonId, CancellationToken cancellationToken)
		{
			string body;
			using (var reader = new System.IO.StreamReader(Request.Body, Encoding.UTF8))
				body = await reader.ReadToEndAsync(cancellationToken);
			var outcome = await _lti.PostScoreAsync(departmentId, lessonId, Bearer(), body, cancellationToken);
			return outcome.Status >= 400 ? StatusCode(outcome.Status) : NoContent();
		}
	}
}
