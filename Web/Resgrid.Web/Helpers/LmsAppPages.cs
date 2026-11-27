using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using Newtonsoft.Json;
using Resgrid.Model.Lms;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// Round 10 A8: the in-app launch page's HTML (/lms-app/...), built here so it can be checked as a whole: no site navigation, a small
	/// stylesheet and one script from this site, and nothing inline (the policy allows no inline script or style). SCORM / cmi5 run in the same
	/// sandboxed frame as the web player (opaque origin: no access to this page, its session token or any cookie); LTI posts to /lms-app/lti,
	/// which sends the web view to the tool's login initiation. The page's session token is written into the page for its own requests only
	/// (form fields and the state request's body), never into a URL.
	/// </summary>
	public static class LmsAppPages
	{
		public const string StylesheetPath = "/css/lms-app.css";
		public const string ScriptPath = "/js/app/internal/lms/resgrid.lms.app.js";
		public const string ContentSandbox = "allow-scripts allow-forms allow-popups allow-modals allow-downloads";

		public const string ModeContent = "content";
		public const string ModeLti = "lti";
		public const string ModeDone = "done";
		public const string ModeError = "error";

		private static readonly string AssetVersion = typeof(LmsAppPages).Assembly.GetName().Version?.ToString() ?? "1";

		/// <summary>
		/// The page's policy: nothing but this site's script, stylesheet, frames (the package content) and requests; forms to this site (the item and
		/// LTI forms) and on to an https tool; framed only by the configured app origins ('none' and X-Frame-Options DENY without any).
		/// </summary>
		public static string PolicyFor(IEnumerable<string> appFrameOrigins) =>
			"default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-src 'self'; " +
			"form-action 'self' https:; base-uri 'none'; object-src 'none'; frame-ancestors " + LmsAppLaunchRules.FrameAncestors(appFrameOrigins);

		/// <summary>The player page of a lesson that may run (SCORM / cmi5 content, or the LTI tool to open).</summary>
		public static string Player(LmsAppPage page, Func<string, string> text, IReadOnlyCollection<string> appFrameOrigins, string cultureName = null)
		{
			var e = HtmlEncoder.Default;
			var lti = page.Kind == LmsAppLaunchKinds.Lti13;
			var body = new StringBuilder();
			body.Append("<header class=\"rg-lms-app__bar\"><h1 class=\"rg-lms-app__title\">").Append(e.Encode(page.LessonTitle ?? string.Empty)).Append("</h1>");
			body.Append("<p class=\"rg-lms-app__status\" id=\"rg-lms-app-status\" role=\"status\" aria-live=\"polite\">").Append(e.Encode(StatusText(page.State, text))).Append("</p></header>");

			if (lti)
			{
				body.Append("<section class=\"rg-lms-app__lti\">");
				body.Append("<p>").Append(e.Encode(Format(text("Std.Player.LtiIntro"), page.ToolName))).Append("</p>");
				body.Append("<p class=\"rg-lms-app__help\" id=\"rg-lms-app-lti-framed\" hidden>").Append(e.Encode(text("Std.App.LtiFramed"))).Append("</p>");
				body.Append("<p class=\"rg-lms-app__help\" id=\"rg-lms-app-lti-opening\" hidden>").Append(e.Encode(text("Std.App.LtiOpening"))).Append("</p>");
				body.Append("<form id=\"rg-lms-app-lti\" method=\"post\" rel=\"noopener noreferrer\" action=\"").Append(e.Encode(Action("/lms-app/lti", cultureName))).Append("\">");
				body.Append(Hidden("session", page.SessionToken));
				body.Append("<button type=\"submit\" class=\"rg-lms-app__button\">").Append(e.Encode(text("Std.Player.LaunchTool"))).Append("</button></form>");
				body.Append("<p class=\"rg-lms-app__help\">").Append(e.Encode(page.ToolGradePassback ? text("Std.App.LtiScoreHelp") : text("Std.Player.LtiOpenHelp"))).Append("</p>");
				body.Append("</section>");
			}
			else
			{
				var launch = page.Launch;
				if (launch?.Items != null && launch.Items.Count > 1)
				{
					body.Append("<nav class=\"rg-lms-app__items\" aria-label=\"").Append(e.Encode(text("Std.Player.Items"))).Append("\"><ul>");
					foreach (var item in launch.Items)
					{
						var current = string.Equals(item.Identifier, launch.Item?.Identifier, StringComparison.Ordinal);
						var done = launch.SatisfiedItems != null && launch.SatisfiedItems.Contains(item.Identifier ?? string.Empty);
						body.Append("<li><form method=\"post\" action=\"").Append(e.Encode(Action("/lms-app/item", cultureName))).Append("\">");
						body.Append(Hidden("session", page.SessionToken)).Append(Hidden("item", item.Identifier));
						body.Append("<button type=\"submit\" class=\"rg-lms-app__item").Append(current ? " rg-lms-app__item--current" : string.Empty).Append("\"")
							.Append(current ? " aria-current=\"page\"" : string.Empty).Append('>').Append(e.Encode(item.Title ?? item.Identifier ?? string.Empty));
						if (done)
							body.Append(" <span class=\"rg-lms-app__done\">(").Append(e.Encode(text("Std.Player.Done"))).Append(")</span>");
						body.Append("</button></form></li>");
					}
					body.Append("</ul></nav>");
				}
				body.Append("<div class=\"rg-lms-app__frame\"><iframe id=\"rg-lms-app-content\" src=\"").Append(e.Encode(launch?.Url ?? "about:blank")).Append("\" title=\"")
					.Append(e.Encode(page.LessonTitle ?? string.Empty)).Append("\" sandbox=\"").Append(ContentSandbox)
					.Append("\" allow=\"fullscreen; autoplay\" allowfullscreen referrerpolicy=\"no-referrer\" loading=\"eager\"></iframe></div>");
			}

			return Document(page.LessonTitle, lti ? ModeLti : ModeContent, page.Kind, page.EnrollmentId, page.LessonId, page.SessionToken, page.State, page.PostInitial,
				appFrameOrigins, text, body.ToString(), autostart: lti);
		}

		/// <summary>A page that only reports: a refusal (mode error) or the LTI tool's return (mode done); either posts its state once.</summary>
		public static string Notice(string title, string message, string mode, LmsAppLessonMessage state, IReadOnlyCollection<string> appFrameOrigins, Func<string, string> text,
			bool closeButton = false)
		{
			var e = HtmlEncoder.Default;
			var body = new StringBuilder();
			body.Append("<header class=\"rg-lms-app__bar\"><h1 class=\"rg-lms-app__title\">").Append(e.Encode(title ?? string.Empty)).Append("</h1></header>");
			body.Append("<section class=\"rg-lms-app__notice\"><p id=\"rg-lms-app-status\" role=\"status\">").Append(e.Encode(message ?? string.Empty)).Append("</p>");
			if (state != null && mode == ModeDone)
				body.Append("<p class=\"rg-lms-app__help\">").Append(e.Encode(StatusText(state, text))).Append("</p>");
			if (closeButton)
				body.Append("<button type=\"button\" class=\"rg-lms-app__button\" id=\"rg-lms-app-close\">").Append(e.Encode(text("Std.App.Close"))).Append("</button>");
			body.Append("</section>");
			var kind = Enum.TryParse<LmsAppLaunchKinds>(state?.Kind, out var parsed) ? parsed : (LmsAppLaunchKinds?)null;
			return Document(title, mode, kind, state?.EnrollmentId ?? 0, state?.LessonId ?? 0, null, state, state != null && state.EnrollmentId > 0, appFrameOrigins, text, body.ToString(),
				autostart: false);
		}

		/// <summary>The status line: the completion rule met or not, and the score when one was reported.</summary>
		public static string StatusText(LmsAppLessonMessage state, Func<string, string> text)
		{
			if (state == null)
				return string.Empty;
			var line = state.Satisfied ? text("Std.Player.Satisfied") : text("Std.Player.NotSatisfied");
			if (state.Score.HasValue)
				line += " · " + text("Std.Player.Score") + " " + state.Score.Value.ToString("0.##", CultureInfo.CurrentCulture) + "%";
			return line;
		}

		private static string Document(string title, string mode, LmsAppLaunchKinds? kind, int enrollmentId, int lessonId, string sessionToken, LmsAppLessonMessage state,
			bool postInitial, IReadOnlyCollection<string> appFrameOrigins, Func<string, string> text, string body, bool autostart)
		{
			var e = HtmlEncoder.Default;
			var culture = CultureInfo.CurrentUICulture;
			var html = new StringBuilder();
			html.Append("<!DOCTYPE html><html lang=\"").Append(e.Encode(string.IsNullOrEmpty(culture.Name) ? "en" : culture.Name)).Append("\" dir=\"")
				.Append(culture.TextInfo.IsRightToLeft ? "rtl" : "ltr").Append("\"><head><meta charset=\"utf-8\">");
			html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1, viewport-fit=cover\"><meta name=\"referrer\" content=\"no-referrer\">");
			html.Append("<meta name=\"robots\" content=\"noindex, nofollow\"><title>").Append(e.Encode(string.IsNullOrWhiteSpace(title) ? "Resgrid" : title)).Append("</title>");
			html.Append("<link rel=\"stylesheet\" href=\"").Append(StylesheetPath).Append("?v=").Append(Uri.EscapeDataString(AssetVersion)).Append("\"></head><body>");

			html.Append("<main id=\"rg-lms-app\" class=\"rg-lms-app rg-lms-app--").Append(mode).Append("\" data-mode=\"").Append(mode).Append('"');
			if (kind.HasValue)
				html.Append(" data-kind=\"").Append(kind.Value.ToString()).Append('"');
			html.Append(" data-enrollment=\"").Append(enrollmentId.ToString(CultureInfo.InvariantCulture)).Append("\" data-lesson=\"").Append(lessonId.ToString(CultureInfo.InvariantCulture)).Append('"');
			if (!string.IsNullOrEmpty(sessionToken))
				html.Append(" data-session=\"").Append(e.Encode(sessionToken)).Append("\" data-state-url=\"/lms-app/state\"");
			html.Append(" data-origins=\"").Append(e.Encode(JsonConvert.SerializeObject((appFrameOrigins ?? Array.Empty<string>()).ToArray()))).Append('"');
			if (state != null)
				html.Append(" data-state=\"").Append(e.Encode(state.ToJson())).Append('"');
			html.Append(" data-post-initial=\"").Append(postInitial ? "true" : "false").Append("\" data-autostart=\"").Append(autostart ? "true" : "false").Append('"');
			html.Append(" data-text-satisfied=\"").Append(e.Encode(text("Std.Player.Satisfied"))).Append('"');
			html.Append(" data-text-not-satisfied=\"").Append(e.Encode(text("Std.Player.NotSatisfied"))).Append('"');
			html.Append(" data-text-score=\"").Append(e.Encode(text("Std.Player.Score"))).Append('"');
			html.Append(" data-text-ended=\"").Append(e.Encode(text("Std.App.Ended"))).Append("\">");
			html.Append(body);
			html.Append("<p class=\"rg-lms-app__offline\" id=\"rg-lms-app-offline\" role=\"alert\" hidden>").Append(e.Encode(text("Std.App.Offline"))).Append("</p>");
			html.Append("</main><script src=\"").Append(ScriptPath).Append("?v=").Append(Uri.EscapeDataString(AssetVersion)).Append("\"></script></body></html>");
			return html.ToString();
		}

		private static string Hidden(string name, string value) =>
			"<input type=\"hidden\" name=\"" + HtmlEncoder.Default.Encode(name) + "\" value=\"" + HtmlEncoder.Default.Encode(value ?? string.Empty) + "\">";

		/// <summary>A form action on this site, keeping the page's language when it came as ui-culture (the query-string culture provider).</summary>
		private static string Action(string path, string cultureName) =>
			string.IsNullOrWhiteSpace(cultureName) || cultureName.Length > 16 || !cultureName.All(c => char.IsLetterOrDigit(c) || c == '-') ? path : path + "?ui-culture=" + cultureName;

		private static string Format(string pattern, string value)
		{
			try
			{
				return string.Format(CultureInfo.CurrentCulture, pattern ?? string.Empty, value ?? string.Empty);
			}
			catch (FormatException)
			{
				return pattern ?? string.Empty;
			}
		}
	}
}
