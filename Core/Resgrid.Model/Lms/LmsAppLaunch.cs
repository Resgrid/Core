using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Resgrid.Model.Lms
{
	// Round 10 A8 (owner request 2026-11-26, "SCORM, cmi5 and LTI lessons in the Omni app"): a bearer client asks for a short-lived, single-use
	// launch link (v4 Lms/GetLessonLaunch) and opens it in a web view. The Core web page behind it (/lms-app/launch) exchanges the link for a
	// session bound to that member, enrollment and lesson alone (no sign-in, no cookie), runs the existing SCORM / cmi5 runtime or LTI 1.3 launch,
	// and tells the app when the runtime records a result (the completion message below). Offline is not supported: every step needs the server.

	/// <summary>What an in-app launch runs (the v4 GetLessonLaunch Kind). Not persisted.</summary>
	public enum LmsAppLaunchKinds
	{
		Scorm12 = 0,
		Scorm2004 = 1,
		Cmi5 = 2,
		Lti13 = 3
	}

	/// <summary>A launch link handed to a bearer client: the opaque single-use token (never the bearer), what it runs, when it stops.</summary>
	public sealed class LmsAppLaunchIssue
	{
		public LmsErrors Error { get; set; }

		public bool Succeeded => Error == LmsErrors.None && !string.IsNullOrEmpty(Token);

		public LmsAppLaunchKinds Kind { get; set; }

		/// <summary>The link's token (256 random bits, base64url); only its SHA-256 is stored.</summary>
		public string Token { get; set; }

		public DateTime ExpiresOn { get; set; }

		public int EnrollmentId { get; set; }

		public int LessonId { get; set; }
	}

	/// <summary>
	/// The completion message the launch page posts to the app (window.ReactNativeWebView.postMessage as a JSON string, and window.parent.postMessage
	/// to each configured app origin) and returns from its state endpoint. camelCase JSON; <see cref="Score"/> and <see cref="Error"/> only when set.
	/// </summary>
	public sealed class LmsAppLessonMessage
	{
		[JsonProperty("type")]
		public string Type { get; set; } = LmsAppLaunchRules.MessageType;

		[JsonProperty("version")]
		public int Version { get; set; } = LmsAppLaunchRules.MessageVersion;

		[JsonProperty("enrollmentId")]
		public int EnrollmentId { get; set; }

		[JsonProperty("lessonId")]
		public int LessonId { get; set; }

		/// <summary>Scorm12, Scorm2004, Cmi5 or Lti13 (absent on a refusal that came before the lesson's content was looked at).</summary>
		[JsonProperty("kind", NullValueHandling = NullValueHandling.Ignore)]
		public string Kind { get; set; }

		/// <summary>One of <see cref="LmsAppLaunchRules.Statuses"/>.</summary>
		[JsonProperty("status")]
		public string Status { get; set; }

		/// <summary>The lesson's completion rule is met by the runtime (what completes the lesson's progress).</summary>
		[JsonProperty("satisfied")]
		public bool Satisfied { get; set; }

		/// <summary>The score as a percentage (0–100) when the content reported one.</summary>
		[JsonProperty("score", NullValueHandling = NullValueHandling.Ignore)]
		public decimal? Score { get; set; }

		/// <summary>Status "unavailable": why the lesson may not run any more (an LmsErrors name: NotLicensed, LapseWindowEnded, CodeExpired ...).</summary>
		[JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
		public string Error { get; set; }

		public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None);
	}

	/// <summary>What the in-app launch page shows: the runtime of one lesson and the page session that may relaunch it and read its state.</summary>
	public sealed class LmsAppPage
	{
		public LmsErrors Error { get; set; }

		public bool Succeeded => Error == LmsErrors.None;

		public LmsAppLaunchKinds Kind { get; set; }

		public int DepartmentId { get; set; }

		public int EnrollmentId { get; set; }

		public int LessonId { get; set; }

		public string LessonTitle { get; set; }

		/// <summary>The page session's token (rendered into the page for its own requests; never in a URL).</summary>
		public string SessionToken { get; set; }

		public DateTime SessionExpiresOn { get; set; }

		/// <summary>SCORM / cmi5: the content launch (the sandboxed frame's URL and the package's items).</summary>
		public LmsContentLaunch Launch { get; set; }

		/// <summary>LTI: the tool's name and whether its score completes the lesson (else opening it does).</summary>
		public string ToolName { get; set; }

		public bool ToolGradePassback { get; set; }

		/// <summary>The lesson's state when the page was built.</summary>
		public LmsAppLessonMessage State { get; set; }

		/// <summary>
		/// The launch itself recorded a result (a cmi5 AU whose moveOn is NotApplicable is satisfied by its launch), so the page posts
		/// <see cref="State"/> at once; otherwise it posts only what changes after it opened (reopening a finished lesson to review it is no news).
		/// </summary>
		public bool PostInitial { get; set; }
	}

	/// <summary>The in-app launch's rules: lifetimes, the app origins that may frame it, the lesson state and the signed LTI return link.</summary>
	public static class LmsAppLaunchRules
	{
		public const string MessageType = "resgrid.lms.lesson";
		public const int MessageVersion = 1;

		public const string StatusNotAttempted = "notAttempted";
		public const string StatusIncomplete = "incomplete";
		public const string StatusCompleted = "completed";
		public const string StatusPassed = "passed";
		public const string StatusFailed = "failed";
		public const string StatusTerminated = "terminated";
		public const string StatusUnavailable = "unavailable";

		/// <summary>Every status a message can carry.</summary>
		public static readonly string[] Statuses = { StatusNotAttempted, StatusIncomplete, StatusCompleted, StatusPassed, StatusFailed, StatusTerminated, StatusUnavailable };

		/// <summary>The statuses that are a result the app should act on (refresh the course).</summary>
		public static readonly string[] ResultStatuses = { StatusCompleted, StatusPassed, StatusFailed, StatusTerminated, StatusUnavailable };

		public const int MaxFrameOrigins = 10;

		/// <summary>How long a launch link may wait for the web view (1–5 minutes).</summary>
		public static TimeSpan LaunchLifetime => TimeSpan.FromMinutes(Math.Clamp(Config.LmsStandardsConfig.AppLaunchMinutes, 1, 5));

		/// <summary>How long the launch page's session lasts (the content launch hours).</summary>
		public static TimeSpan SessionLifetime => TimeSpan.FromHours(Math.Max(1, Config.LmsStandardsConfig.LaunchTokenHours));

		private static readonly Regex OriginShape = new Regex(@"^(?<scheme>[a-z][a-z0-9+.\-]{0,30})://(?<host>[a-z0-9](?:[a-z0-9\-]{0,62}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9\-]{0,62}[a-z0-9])?)*)(?::(?<port>[0-9]{1,5}))?$",
			RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		private static readonly HashSet<string> RefusedSchemes = new HashSet<string>(StringComparer.Ordinal)
		{
			"javascript", "data", "blob", "file", "filesystem", "about", "ws", "wss", "ftp", "mailto", "tel", "vbscript", "chrome", "chrome-extension", "resource"
		};

		/// <summary>The configured app origins (<see cref="Config.LmsStandardsConfig.AppFrameOrigins"/>), validated.</summary>
		public static List<string> FrameOrigins() => ParseOrigins(Config.LmsStandardsConfig.AppFrameOrigins);

		/// <summary>
		/// The valid origins of a list (spaces, commas or semicolons between): scheme://host[:port] with nothing after it; https anywhere, http only
		/// for localhost or a .local / .localhost host, an app's own scheme (app://bundle) as written; no wildcards, paths, credentials or the
		/// schemes of documents (javascript:, data:, file: ...). Lower case, de-duplicated, at most <see cref="MaxFrameOrigins"/>.
		/// </summary>
		public static List<string> ParseOrigins(string value)
		{
			var result = new List<string>();
			foreach (var raw in (value ?? string.Empty).Split(new[] { ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
			{
				var entry = raw.Trim().ToLowerInvariant();
				if (entry.EndsWith("/", StringComparison.Ordinal))
					entry = entry.Substring(0, entry.Length - 1);
				if (entry.Length == 0 || entry.Length > 200)
					continue;
				var match = OriginShape.Match(entry);
				if (!match.Success)
					continue;
				var scheme = match.Groups["scheme"].Value;
				var host = match.Groups["host"].Value;
				if (RefusedSchemes.Contains(scheme))
					continue;
				if (match.Groups["port"].Success && (!int.TryParse(match.Groups["port"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port < 1 || port > 65535))
					continue;
				if (scheme == "http" && !(host == "localhost" || host == "127.0.0.1" || host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".localhost", StringComparison.Ordinal)))
					continue;
				if (!result.Contains(entry, StringComparer.Ordinal))
					result.Add(entry);
				if (result.Count >= MaxFrameOrigins)
					break;
			}
			return result;
		}

		/// <summary>The CSP frame-ancestors sources for an app launch: the configured app origins, or 'none' without any.</summary>
		public static string FrameAncestors(IEnumerable<string> origins)
		{
			var list = (origins ?? Enumerable.Empty<string>()).ToList();
			return list.Count == 0 ? "'none'" : string.Join(" ", list);
		}

		/// <summary>What a launch runs: SCORM 1.2 or 2004 by the package's standard, cmi5, or LTI 1.3; null for any other lesson kind.</summary>
		public static LmsAppLaunchKinds? KindFor(LmsLesson lesson, LmsContentPackage package)
		{
			switch ((LmsLessonKinds)(lesson?.Kind ?? -1))
			{
				case LmsLessonKinds.Scorm:
					if (package == null || package.Standard == (int)LmsContentStandards.Cmi5)
						return null;
					return package.Standard == (int)LmsContentStandards.Scorm2004 ? LmsAppLaunchKinds.Scorm2004 : LmsAppLaunchKinds.Scorm12;
				case LmsLessonKinds.Cmi5:
					return package?.Standard == (int)LmsContentStandards.Cmi5 ? LmsAppLaunchKinds.Cmi5 : (LmsAppLaunchKinds?)null;
				case LmsLessonKinds.LtiLink:
					return LmsAppLaunchKinds.Lti13;
				default:
					return null;
			}
		}

		/// <summary>
		/// The lesson's status from its runtime registration and the launch page's latest content session: passed / failed (the content's
		/// success), completed (its completion, or the completion rule met), terminated (the SCO finished or the AU terminated without a
		/// result), else incomplete (notAttempted before anything was recorded).
		/// </summary>
		public static string Status(LmsRuntimeRegistration registration, LmsRuntimeSession latestContentSession)
		{
			if (registration == null)
				return latestContentSession?.EndedOn != null ? StatusTerminated : StatusNotAttempted;
			if (string.Equals(registration.SuccessStatus, "passed", StringComparison.Ordinal))
				return StatusPassed;
			if (string.Equals(registration.SuccessStatus, "failed", StringComparison.Ordinal))
				return StatusFailed;
			if (registration.Satisfied || string.Equals(registration.CompletionStatus, "completed", StringComparison.Ordinal))
				return StatusCompleted;
			if (latestContentSession?.EndedOn != null)
				return StatusTerminated;
			return string.IsNullOrEmpty(registration.CompletionStatus) || string.Equals(registration.CompletionStatus, "not attempted", StringComparison.Ordinal)
				? StatusNotAttempted : StatusIncomplete;
		}

		/// <summary>The message for a lesson's state (or its refusal: status unavailable with the error's name).</summary>
		public static LmsAppLessonMessage Message(int enrollmentId, int lessonId, LmsAppLaunchKinds? kind, LmsRuntimeRegistration registration, LmsRuntimeSession latestContentSession,
			LmsErrors refusal = LmsErrors.None) => new LmsAppLessonMessage
		{
			EnrollmentId = enrollmentId,
			LessonId = lessonId,
			Kind = kind?.ToString(),
			Status = refusal != LmsErrors.None ? StatusUnavailable : Status(registration, latestContentSession),
			Satisfied = registration?.Satisfied == true,
			Score = registration?.ScorePercent,
			Error = refusal != LmsErrors.None ? refusal.ToString() : null
		};

		#region The LTI return link

		public const string ReturnPath = "/lms-app/return";
		public const string ReturnDepartmentParameter = "d";
		public const string ReturnSessionParameter = "a";
		public const string ReturnExpiresParameter = "e";
		public const string ReturnSignatureParameter = "sig";

		/// <summary>Signed return links need the external-link passphrase (a key is derived from it); without it a tool gets no return URL.</summary>
		public static bool CanSignReturnLinks => !string.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);

		/// <summary>
		/// The launch_presentation return_url of an LTI launch an app session started: the department, the app session and an expiry under an HMAC.
		/// It only shows that one lesson's state (no relaunch, no other data). Null when links cannot be signed.
		/// </summary>
		public static string ReturnUrl(int departmentId, int appSessionId, DateTime expiresUtc)
		{
			if (!CanSignReturnLinks || departmentId <= 0 || appSessionId <= 0)
				return null;
			var expires = new DateTimeOffset(DateTime.SpecifyKind(expiresUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
			return LmsStandardsUrls.SiteBase + ReturnPath + "?" +
				ReturnDepartmentParameter + "=" + departmentId.ToString(CultureInfo.InvariantCulture) + "&" +
				ReturnSessionParameter + "=" + appSessionId.ToString(CultureInfo.InvariantCulture) + "&" +
				ReturnExpiresParameter + "=" + expires.ToString(CultureInfo.InvariantCulture) + "&" +
				ReturnSignatureParameter + "=" + SignReturn(departmentId, appSessionId, expires);
		}

		/// <summary>True when the return link is well formed, unexpired, not further out than a session lasts, and its signature matches.</summary>
		public static bool TryValidateReturn(string departmentText, string sessionText, string expiresText, string signature, DateTime utcNow, out int departmentId, out int appSessionId)
		{
			departmentId = appSessionId = 0;
			if (!CanSignReturnLinks || string.IsNullOrWhiteSpace(signature) || signature.Length > 64)
				return false;
			if (!int.TryParse(departmentText, NumberStyles.None, CultureInfo.InvariantCulture, out var department) || department <= 0 ||
				!int.TryParse(sessionText, NumberStyles.None, CultureInfo.InvariantCulture, out var session) || session <= 0 ||
				!long.TryParse(expiresText, NumberStyles.None, CultureInfo.InvariantCulture, out var expires))
				return false;
			var now = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)).ToUnixTimeSeconds();
			if (now > expires || expires > now + (long)SessionLifetime.TotalSeconds + 60)
				return false;
			var expected = SignReturn(department, session, expires);
			if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature.Trim())))
				return false;
			departmentId = department;
			appSessionId = session;
			return true;
		}

		private static string SignReturn(int departmentId, int appSessionId, long expires)
		{
			var payload = string.Join("|", "lms-app-return", departmentId.ToString(CultureInfo.InvariantCulture), appSessionId.ToString(CultureInfo.InvariantCulture),
				expires.ToString(CultureInfo.InvariantCulture));
			// Derived, so this signature can never be replayed as a map URL, a media link or any other value signed with the passphrase.
			var key = SHA256.HashData(Encoding.UTF8.GetBytes("resgrid-lms-app-return|" + (Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase ?? string.Empty)));
			using var hmac = new HMACSHA256(key);
			var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
			return Convert.ToBase64String(hash, 0, 24).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		}

		#endregion The LTI return link
	}

	/// <summary>
	/// A per-server sliding one-minute window by key (a member for the launch links, an address for the launch page): the in-app launch's
	/// rate limit. Thread safe; idle keys are pruned.
	/// </summary>
	public sealed class LmsAppLaunchLimiter
	{
		private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Queue<DateTime>> _hits =
			new System.Collections.Concurrent.ConcurrentDictionary<string, Queue<DateTime>>(StringComparer.Ordinal);
		private DateTime _lastPrune = DateTime.MinValue;

		/// <summary>Counts this use; false (refuse it) when the key already had <paramref name="perMinute"/> uses in the last minute.</summary>
		public bool Allow(string key, int perMinute, DateTime utcNow)
		{
			if (perMinute <= 0)
				return true;
			var window = utcNow.AddMinutes(-1);
			var queue = _hits.GetOrAdd(key ?? string.Empty, _ => new Queue<DateTime>());
			lock (queue)
			{
				while (queue.Count > 0 && queue.Peek() < window)
					queue.Dequeue();
				if (queue.Count >= perMinute)
					return false;
				queue.Enqueue(utcNow);
			}
			Prune(utcNow);
			return true;
		}

		/// <summary>Forgets every count (tests).</summary>
		public void Clear() => _hits.Clear();

		private void Prune(DateTime utcNow)
		{
			if (utcNow - _lastPrune < TimeSpan.FromMinutes(5) && _hits.Count < 50000)
				return;
			_lastPrune = utcNow;
			var window = utcNow.AddMinutes(-1);
			foreach (var pair in _hits)
			{
				lock (pair.Value)
				{
					while (pair.Value.Count > 0 && pair.Value.Peek() < window)
						pair.Value.Dequeue();
					if (pair.Value.Count == 0)
						_hits.TryRemove(pair.Key, out _);
				}
			}
		}
	}
}
