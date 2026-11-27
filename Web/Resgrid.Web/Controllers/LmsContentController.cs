using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Framework;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Web.Controllers
{
	/// <summary>
	/// The content side of the LMS standards runtime (Training T5 item 3), on its own path: /lms-content/{token}/... serves a launch's package
	/// files to the player's sandboxed iframe. Every response carries a CSP whose sandbox directive gives the document an opaque origin even
	/// when opened on its own (no access to Resgrid's cookies, storage or pages), connections only back to this site, and no framing outside
	/// it. Nothing here uses the sign-in cookie: the launch token in the path (only its hash is stored, 8 hours) is the authority, and the
	/// SCORM adapter's commits, the cmi5 fetch URL and the minimal LRS answer CORS for the opaque frame with that token alone.
	/// </summary>
	[AllowAnonymous]
	[Route("lms-content")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class LmsContentController : Controller
	{
		private const int MaxHtmlBytes = 10 * 1024 * 1024;
		private const int MaxBodyChars = 2 * 1024 * 1024;

		/// <summary>The content policy: sandboxed (opaque origin), scripts and media from this site only, connections back to this site only.</summary>
		public const string ContentSecurityPolicy =
			"sandbox allow-scripts allow-forms allow-popups allow-modals allow-downloads; " +
			"default-src 'self' 'unsafe-inline' 'unsafe-eval' data: blob:; connect-src 'self'; img-src 'self' data: blob:; media-src 'self' data: blob:; " +
			"font-src 'self' data:; frame-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'";

		/// <summary>
		/// The policy with this site's origin named beside 'self': in a sandboxed document (an opaque origin) browsers differ on what 'self'
		/// matches, so the site is spelled out and nothing else is added.
		/// </summary>
		public static string ContentPolicyFor(string siteOrigin) => ContentPolicyFor(siteOrigin, null);

		/// <summary>
		/// Round 10 A8: the policy of a launch from the app's page (its session carries the app session's id). frame-ancestors checks every
		/// ancestor, and in the app's web or desktop build the launch page itself sits in the app's iframe, so the configured app origins
		/// (<see cref="LmsAppLaunchRules.FrameOrigins"/>) are added to frame-ancestors, and to nothing else. A web launch never gets them.
		/// </summary>
		public static string ContentPolicyFor(string siteOrigin, IReadOnlyCollection<string> appFrameOrigins)
		{
			var policy = string.IsNullOrWhiteSpace(siteOrigin) || !Uri.TryCreate(siteOrigin, UriKind.Absolute, out var origin) || (origin.Scheme != Uri.UriSchemeHttps && origin.Scheme != Uri.UriSchemeHttp)
				? ContentSecurityPolicy
				: ContentSecurityPolicy.Replace("'self'", "'self' " + origin.GetLeftPart(UriPartial.Authority));
			if (appFrameOrigins == null || appFrameOrigins.Count == 0)
				return policy;
			// The origins were validated (scheme://host[:port], no spaces, quotes or semicolons), so they cannot add a directive.
			var ancestors = policy.LastIndexOf("frame-ancestors ", StringComparison.Ordinal);
			if (ancestors < 0)
				return policy;
			var end = policy.IndexOf(';', ancestors);
			end = end < 0 ? policy.Length : end;
			return policy.Substring(0, end) + " " + string.Join(" ", appFrameOrigins) + policy.Substring(end);
		}

		private static readonly string AdapterVersion = typeof(LmsContentController).Assembly.GetName().Version?.ToString() ?? "1";

		private readonly ILmsStandardsRuntimeService _runtime;

		public LmsContentController(ILmsStandardsRuntimeService runtime)
		{
			_runtime = runtime;
		}

		private void ContentHeaders(bool html, LmsRuntimeSession session = null)
		{
			Response.Headers["Content-Security-Policy"] = ContentPolicyFor(Request.Scheme + "://" + Request.Host,
				session?.AppSessionId != null ? LmsAppLaunchRules.FrameOrigins() : null);
			Response.Headers["X-Content-Type-Options"] = "nosniff";
			Response.Headers["Referrer-Policy"] = "no-referrer";
			// No Cross-Origin-Resource-Policy: the sandboxed document has an opaque origin, so "same-site" would block its own files.
			Response.Headers["Cache-Control"] = html ? "no-store" : "private, max-age=600";
		}

		/// <summary>CORS for the opaque frame (token-authenticated endpoints: no credentials, any origin).</summary>
		private void Cors()
		{
			Response.Headers["Access-Control-Allow-Origin"] = "*";
			Response.Headers["Access-Control-Allow-Methods"] = "GET, PUT, POST, DELETE, OPTIONS";
			Response.Headers["Access-Control-Allow-Headers"] = "Authorization, Content-Type, X-Experience-API-Version, If-Match, If-None-Match";
			Response.Headers["Access-Control-Expose-Headers"] = "X-Experience-API-Version, ETag, Last-Modified";
			Response.Headers["Access-Control-Max-Age"] = "600";
			Response.Headers["X-Content-Type-Options"] = "nosniff";
		}

		private IActionResult Status(int status, object body = null)
		{
			if (body == null)
				return StatusCode(status);
			return new ContentResult { StatusCode = status, ContentType = "application/json", Content = body is string s ? s : JsonConvert.SerializeObject(body) };
		}

		private async Task<string> ReadBodyAsync()
		{
			using var reader = new StreamReader(Request.Body, Encoding.UTF8);
			var buffer = new char[8192];
			var text = new StringBuilder();
			int read;
			while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
			{
				text.Append(buffer, 0, read);
				if (text.Length > MaxBodyChars)
					return null;
			}
			return text.ToString();
		}

		#region Package files and the SCORM adapter

		/// <summary>The SCORM adapter's start values for this launch (a script the injected adapter reads before the SCO runs).</summary>
		[HttpGet("{token}/__rg/config.js")]
		public async Task<IActionResult> Config(string token)
		{
			ContentHeaders(html: true);
			var config = await _runtime.GetScormConfigAsync(token, "/lms-content/" + token + "/__rg/commit");
			if (config == null)
				return NotFound();
			var json = JsonConvert.SerializeObject(config, new JsonSerializerSettings { StringEscapeHandling = StringEscapeHandling.EscapeHtml });
			return Content("window.RG_LMS = " + json + ";", "text/javascript; charset=utf-8");
		}

		[HttpOptions("{token}/__rg/commit")]
		public IActionResult CommitOptions()
		{
			Cors();
			return NoContent();
		}

		/// <summary>One SCORM commit (text/plain JSON so the opaque frame needs no preflight): {"values": {...}, "finish": true|false}.</summary>
		[HttpPost("{token}/__rg/commit")]
		[RequestSizeLimit(MaxBodyChars)]
		public async Task<IActionResult> Commit(string token, CancellationToken cancellationToken)
		{
			Cors();
			var body = LmsStandardsJson.ParseObject(await ReadBodyAsync());
			if (body == null || !(body["values"] is JObject values))
				return Status(400, new { ok = false });
			var commit = new LmsScormCommit { Finish = body["finish"]?.Type == JTokenType.Boolean && (bool)body["finish"] };
			foreach (var property in values.Properties())
				commit.Values[property.Name] = property.Value.Type == JTokenType.String ? (string)property.Value : property.Value.ToString(Formatting.None);

			var outcome = await _runtime.CommitScormAsync(token, commit, cancellationToken);
			if (outcome.Status >= 400)
				return Status(outcome.Status, new { ok = false });
			return Status(200, new { ok = true, refused = outcome.Value, satisfied = outcome.Registration?.Satisfied == true, courseCompleted = outcome.CourseCompleted });
		}

		/// <summary>
		/// A package file. HTML of a SCORM launch gets the adapter (window.API / API_1484_11) injected into its head, so the SCO finds the API
		/// in its own window; nothing else is altered.
		/// </summary>
		[HttpGet("{token}/{**path}")]
		public async Task<IActionResult> PackageFile(string token, string path, CancellationToken cancellationToken)
		{
			var session = await _runtime.ResolveContentAsync(token);
			if (session == null || string.IsNullOrWhiteSpace(path))
				return NotFound();
			var stream = await _runtime.OpenContentAsync(session, path, cancellationToken);
			if (stream == null)
				return NotFound();

			var html = LmsPackageInspector.IsHtml(path);
			ContentHeaders(html, session.Session);
			var contentType = LmsPackageInspector.ContentTypeFor(path);
			var scorm = session.Session.Kind == (int)LmsRuntimeKinds.Scorm12 || session.Session.Kind == (int)LmsRuntimeKinds.Scorm2004;
			if (!html || !scorm)
				return new FileStreamResult(stream, contentType);

			byte[] bytes;
			using (stream)
			using (var buffer = new MemoryStream())
			{
				var chunk = new byte[81920];
				int read;
				while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken)) > 0)
				{
					buffer.Write(chunk, 0, read);
					if (buffer.Length > MaxHtmlBytes)
						return StatusCode(413);
				}
				bytes = buffer.ToArray();
			}
			var tags = "<script src=\"/lms-content/" + Uri.EscapeDataString(token) + "/__rg/config.js\"></script>" +
				"<script src=\"/js/app/internal/lms/resgrid.lms.adapter.js?v=" + Uri.EscapeDataString(AdapterVersion) + "\"></script>";
			return File(InjectIntoHead(bytes, Encoding.ASCII.GetBytes(tags)), contentType);
		}

		/// <summary>Inserts <paramref name="tags"/> right after the document's &lt;head&gt; (or &lt;html&gt;), or at the start when it has neither.</summary>
		public static byte[] InjectIntoHead(byte[] document, byte[] tags)
		{
			int After(string tag)
			{
				var limit = Math.Min(document.Length, 256 * 1024);
				var needle = Encoding.ASCII.GetBytes(tag);
				for (var i = 0; i + needle.Length < limit; i++)
				{
					var match = true;
					for (var j = 0; j < needle.Length && match; j++)
						match = char.ToLowerInvariant((char)document[i + j]) == needle[j];
					if (!match)
						continue;
					var next = (char)document[i + needle.Length];
					if (next != '>' && next != ' ' && next != '\t' && next != '\r' && next != '\n' && next != '/')
						continue;
					for (var k = i + needle.Length; k < limit; k++)
						if (document[k] == (byte)'>')
							return k + 1;
					return -1;
				}
				return -1;
			}

			var at = After("<head");
			if (at < 0)
				at = After("<html");
			if (at < 0)
			{
				// Keep a byte order mark first.
				at = document.Length >= 3 && document[0] == 0xEF && document[1] == 0xBB && document[2] == 0xBF ? 3 : 0;
			}
			var result = new byte[document.Length + tags.Length];
			Buffer.BlockCopy(document, 0, result, 0, at);
			Buffer.BlockCopy(tags, 0, result, at, tags.Length);
			Buffer.BlockCopy(document, at, result, at + tags.Length, document.Length - at);
			return result;
		}

		#endregion Package files and the SCORM adapter

		#region cmi5 fetch URL and minimal LRS

		private string BasicToken()
		{
			var header = Request.Headers["Authorization"].ToString();
			return header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) ? header.Substring(6).Trim() : null;
		}

		private void Lrs()
		{
			Cors();
			Response.Headers["X-Experience-API-Version"] = "1.0.3";
		}

		[HttpOptions("cmi5/fetch/{fetchToken}")]
		[HttpOptions("xapi/{**rest}")]
		public IActionResult Preflight()
		{
			Lrs();
			return NoContent();
		}

		/// <summary>cmi5 §8.2: the fetch URL, POSTed once by the AU, returns its auth token.</summary>
		[HttpPost("cmi5/fetch/{fetchToken}")]
		public async Task<IActionResult> Fetch(string fetchToken, CancellationToken cancellationToken)
		{
			Cors();
			var outcome = await _runtime.FetchCmi5TokenAsync(fetchToken, cancellationToken);
			return Status(outcome.Status, (outcome.Value as JObject)?.ToString(Formatting.None) ?? "{}");
		}

		[HttpGet("xapi/about")]
		public IActionResult About()
		{
			Lrs();
			return Status(200, "{\"version\":[\"1.0.3\"]}");
		}

		[HttpPost("xapi/statements")]
		[HttpPut("xapi/statements")]
		[RequestSizeLimit(MaxBodyChars)]
		public async Task<IActionResult> SaveStatements([FromQuery] string statementId, CancellationToken cancellationToken)
		{
			Lrs();
			var put = HttpMethods.IsPut(Request.Method);
			if (put && string.IsNullOrWhiteSpace(statementId))
				return Status(400);
			var body = await ReadBodyAsync();
			var outcome = await _runtime.SaveCmi5StatementsAsync(BasicToken(), body, put ? statementId : null, cancellationToken);
			if (outcome.Status >= 400)
				return Status(outcome.Status);
			return put ? NoContent() : Status(200, JsonConvert.SerializeObject(outcome.Value ?? new List<string>()));
		}

		[HttpGet("xapi/statements")]
		public async Task<IActionResult> GetStatements([FromQuery] string statementId)
		{
			Lrs();
			if (string.IsNullOrWhiteSpace(statementId))
				return Status(200, "{\"statements\":[],\"more\":\"\"}");
			var outcome = await _runtime.GetCmi5StatementAsync(BasicToken(), statementId);
			return outcome.Status >= 400 ? Status(outcome.Status) : Status(200, outcome.Value as string);
		}

		[HttpGet("xapi/activities/state")]
		public async Task<IActionResult> GetState([FromQuery] string activityId, [FromQuery] string stateId, [FromQuery] string registration, [FromQuery] string agent)
		{
			Lrs();
			var outcome = await _runtime.GetCmi5StateAsync(BasicToken(), activityId, stateId, registration, agent);
			if (outcome.Status >= 400)
				return Status(outcome.Status);
			var text = outcome.Value as string ?? string.Empty;
			return new ContentResult { StatusCode = 200, ContentType = LmsStandardsJson.Parse(text) != null ? "application/json" : "application/octet-stream", Content = text };
		}

		[HttpPut("xapi/activities/state")]
		[HttpPost("xapi/activities/state")]
		[RequestSizeLimit(MaxBodyChars)]
		public async Task<IActionResult> SaveState([FromQuery] string activityId, [FromQuery] string stateId, [FromQuery] string registration, [FromQuery] string agent,
			CancellationToken cancellationToken)
		{
			Lrs();
			var body = await ReadBodyAsync();
			if (body == null)
				return Status(413);
			var outcome = await _runtime.SaveCmi5StateAsync(BasicToken(), activityId, stateId, registration, agent, Request.ContentType, body, HttpMethods.IsPost(Request.Method),
				cancellationToken);
			return outcome.Status >= 400 ? Status(outcome.Status) : NoContent();
		}

		[HttpDelete("xapi/activities/state")]
		public async Task<IActionResult> DeleteState([FromQuery] string activityId, [FromQuery] string stateId, [FromQuery] string registration, [FromQuery] string agent,
			CancellationToken cancellationToken)
		{
			Lrs();
			var outcome = await _runtime.DeleteCmi5StateAsync(BasicToken(), activityId, stateId, registration, agent, cancellationToken);
			return outcome.Status >= 400 ? Status(outcome.Status) : NoContent();
		}

		/// <summary>Agent profiles (cmi5LearnerPreferences): none are kept; the AU uses its defaults on a 404 (cmi5 §11).</summary>
		[HttpGet("xapi/agents/profile")]
		public IActionResult GetAgentProfile()
		{
			Lrs();
			return Status(404);
		}

		#endregion cmi5 fetch URL and minimal LRS
	}
}
