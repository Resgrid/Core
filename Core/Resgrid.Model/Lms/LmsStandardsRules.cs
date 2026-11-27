using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Resgrid.Model.Lms
{
	/// <summary>URLs a department enters for the standards features (an LRS endpoint, an LTI tool's endpoints): absolute https only.</summary>
	public static class LmsStandardsUrls
	{
		public const int MaxLength = 2000;

		public static bool IsAcceptableHttpsUrl(string value)
		{
			if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
				return false;
			if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
				return false;
			return uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(uri.Host) && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment);
		}

		/// <summary>The site base every absolute standards URL hangs off (SystemBehaviorConfig.ResgridBaseUrl, no trailing slash).</summary>
		public static string SiteBase => (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/');
	}

	/// <summary>JSON the standards endpoints read: strings stay strings (an xAPI timestamp is stored as the AU wrote it, never re-formatted).</summary>
	public static class LmsStandardsJson
	{
		public static JToken Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;
			try
			{
				using var reader = new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(json)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None, FloatParseHandling = Newtonsoft.Json.FloatParseHandling.Decimal, MaxDepth = 64 };
				var token = JToken.ReadFrom(reader);
				return reader.Read() ? null : token;
			}
			catch (Newtonsoft.Json.JsonException)
			{
				return null;
			}
		}

		public static JObject ParseObject(string json) => Parse(json) as JObject;
	}

	/// <summary>
	/// The pseudonymous learner id a department's members carry outside Resgrid (the LTI subject, the cmi5 and xAPI account name, the SCORM
	/// learner id): stable per department and member, not reversible without the database, never the internal user id or the e-mail address.
	/// </summary>
	public static class LmsLearnerIdentity
	{
		public static string Subject(int departmentId, string userId)
		{
			var key = Encoding.UTF8.GetBytes("resgrid-lms-learner:" + (Config.LmsStandardsConfig.LearnerIdPepper ?? string.Empty) + ":" +
				departmentId.ToString(CultureInfo.InvariantCulture));
			using var hmac = new HMACSHA256(key);
			var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes((userId ?? string.Empty).Trim().ToLowerInvariant()));
			return string.Concat(hash.Take(16).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
		}

		/// <summary>The account home page the xAPI actor's account name lives under: the department's LMS on this Resgrid site.</summary>
		public static string HomePage(int departmentId) => LmsStandardsUrls.SiteBase + "/lms/" + departmentId.ToString(CultureInfo.InvariantCulture);

		/// <summary>The xAPI Agent for the department-scoped account.</summary>
		public static JObject AccountActor(int departmentId, string userId) => new JObject
		{
			["objectType"] = "Agent",
			["account"] = new JObject { ["homePage"] = HomePage(departmentId), ["name"] = Subject(departmentId, userId) }
		};

		/// <summary>SHA-256 (lower-case hex) of a bearer token: only the hash is stored.</summary>
		public static string HashToken(string token) =>
			string.IsNullOrEmpty(token) ? null : string.Concat(SHA256.HashData(Encoding.UTF8.GetBytes(token)).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));

		/// <summary>A new random bearer token (32 bytes, base64url without padding).</summary>
		public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}

	/// <summary>
	/// The SCORM 1.2 and 2004 run-time data model as the server enforces it: which elements a SCO may write (and how long or of which
	/// vocabulary), what the adapter starts with, and what the stored values mean for the lesson (completion, success, score). The adapter in
	/// the sandboxed frame answers GetValue from its own copy; every commit is re-validated here, and read-only elements are never taken from it.
	/// </summary>
	public static class LmsScormRuntime
	{
		public const int MaxCollectionIndex = 249;
		public const int MaxValuesPerSco = 5000;
		public const int MaxStoredCharsPerSco = 300 * 1024;

		private enum Shape { Text, Vocab, Decimal, Integer }

		private sealed class Spec
		{
			public Shape Shape;
			public int MaxLength;
			public HashSet<string> Vocabulary;
			public decimal Min = decimal.MinValue;
			public decimal Max = decimal.MaxValue;
		}

		private static Spec Text(int max) => new Spec { Shape = Shape.Text, MaxLength = max };
		private static Spec Vocab(params string[] words) => new Spec { Shape = Shape.Vocab, Vocabulary = new HashSet<string>(words, StringComparer.Ordinal) };
		private static Spec Number(decimal min = -1000000m, decimal max = 1000000m) => new Spec { Shape = Shape.Decimal, Min = min, Max = max };
		private static Spec Integer(int min, int max) => new Spec { Shape = Shape.Integer, Min = min, Max = max };

		private static readonly Dictionary<string, Spec> Scorm12 = new Dictionary<string, Spec>(StringComparer.Ordinal)
		{
			["cmi.core.lesson_location"] = Text(255),
			["cmi.core.lesson_status"] = Vocab("passed", "completed", "failed", "incomplete", "browsed"),
			["cmi.core.score.raw"] = Number(),
			["cmi.core.score.min"] = Number(),
			["cmi.core.score.max"] = Number(),
			["cmi.core.exit"] = Vocab("time-out", "suspend", "logout", ""),
			["cmi.core.session_time"] = Text(32),
			["cmi.suspend_data"] = Text(4096),
			["cmi.comments"] = Text(4096),
			["cmi.student_preference.audio"] = Integer(-1, 100),
			["cmi.student_preference.language"] = Text(255),
			["cmi.student_preference.speed"] = Integer(-100, 100),
			["cmi.student_preference.text"] = Integer(-1, 1),
			["cmi.objectives.n.id"] = Text(255),
			["cmi.objectives.n.score.raw"] = Number(),
			["cmi.objectives.n.score.min"] = Number(),
			["cmi.objectives.n.score.max"] = Number(),
			["cmi.objectives.n.status"] = Vocab("passed", "completed", "failed", "incomplete", "browsed", "not attempted"),
			["cmi.interactions.n.id"] = Text(255),
			["cmi.interactions.n.objectives.n.id"] = Text(255),
			["cmi.interactions.n.time"] = Text(64),
			["cmi.interactions.n.type"] = Vocab("true-false", "choice", "fill-in", "matching", "performance", "sequencing", "likert", "numeric"),
			["cmi.interactions.n.correct_responses.n.pattern"] = Text(255),
			["cmi.interactions.n.weighting"] = Number(),
			["cmi.interactions.n.student_response"] = Text(255),
			["cmi.interactions.n.result"] = Text(64),
			["cmi.interactions.n.latency"] = Text(64)
		};

		private static readonly Dictionary<string, Spec> Scorm2004 = new Dictionary<string, Spec>(StringComparer.Ordinal)
		{
			["cmi.location"] = Text(1000),
			["cmi.completion_status"] = Vocab("completed", "incomplete", "not attempted", "unknown"),
			["cmi.success_status"] = Vocab("passed", "failed", "unknown"),
			["cmi.score.scaled"] = Number(-1m, 1m),
			["cmi.score.raw"] = Number(),
			["cmi.score.min"] = Number(),
			["cmi.score.max"] = Number(),
			["cmi.progress_measure"] = Number(0m, 1m),
			["cmi.exit"] = Vocab("time-out", "suspend", "logout", "normal", ""),
			["cmi.session_time"] = Text(64),
			["cmi.suspend_data"] = Text(64000),
			["cmi.learner_preference.audio_level"] = Number(0m, 1000000m),
			["cmi.learner_preference.language"] = Text(250),
			["cmi.learner_preference.delivery_speed"] = Number(0m, 1000000m),
			["cmi.learner_preference.audio_captioning"] = Vocab("-1", "0", "1"),
			["cmi.objectives.n.id"] = Text(4000),
			["cmi.objectives.n.score.scaled"] = Number(-1m, 1m),
			["cmi.objectives.n.score.raw"] = Number(),
			["cmi.objectives.n.score.min"] = Number(),
			["cmi.objectives.n.score.max"] = Number(),
			["cmi.objectives.n.success_status"] = Vocab("passed", "failed", "unknown"),
			["cmi.objectives.n.completion_status"] = Vocab("completed", "incomplete", "not attempted", "unknown"),
			["cmi.objectives.n.progress_measure"] = Number(0m, 1m),
			["cmi.objectives.n.description"] = Text(250),
			["cmi.interactions.n.id"] = Text(4000),
			["cmi.interactions.n.type"] = Text(64),
			["cmi.interactions.n.objectives.n.id"] = Text(4000),
			["cmi.interactions.n.timestamp"] = Text(64),
			["cmi.interactions.n.correct_responses.n.pattern"] = Text(4000),
			["cmi.interactions.n.weighting"] = Number(),
			["cmi.interactions.n.learner_response"] = Text(4000),
			["cmi.interactions.n.result"] = Text(64),
			["cmi.interactions.n.latency"] = Text(64),
			["cmi.interactions.n.description"] = Text(250),
			["cmi.comments_from_learner.n.comment"] = Text(4000),
			["cmi.comments_from_learner.n.location"] = Text(250),
			["cmi.comments_from_learner.n.timestamp"] = Text(64),
			["adl.nav.request"] = Text(250)
		};

		private static readonly Regex Index = new Regex(@"\.(\d{1,4})(?=\.|$)", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		public static bool Is2004(int kind) => kind == (int)LmsRuntimeKinds.Scorm2004;

		/// <summary>The element pattern of a key ("cmi.interactions.3.id" is "cmi.interactions.n.id"), or null when an index is out of range.</summary>
		public static string Pattern(string key)
		{
			if (string.IsNullOrWhiteSpace(key) || key.Length > 200)
				return null;
			var outOfRange = false;
			var pattern = Index.Replace(key, m =>
			{
				if (!int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n > MaxCollectionIndex)
					outOfRange = true;
				return ".n";
			});
			return outOfRange ? null : pattern;
		}

		/// <summary>Whether a SCO may write <paramref name="key"/> = <paramref name="value"/> (the server's check; read-only elements are refused).</summary>
		public static bool IsWritable(int kind, string key, string value)
		{
			var pattern = Pattern(key);
			if (pattern == null)
				return false;
			var specs = Is2004(kind) ? Scorm2004 : Scorm12;
			if (!specs.TryGetValue(pattern, out var spec))
				return false;
			value ??= string.Empty;
			switch (spec.Shape)
			{
				case Shape.Text:
					return value.Length <= spec.MaxLength && !value.Any(c => c < 0x20 && c != '\t' && c != '\n' && c != '\r');
				case Shape.Vocab:
					return spec.Vocabulary.Contains(value);
				case Shape.Integer:
					return int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var i) && i >= spec.Min && i <= spec.Max;
				default:
					if (!Is2004(kind) && value.Length == 0)
						return true;                                                  // CMIBlank is a valid 1.2 score
					return decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) && d >= spec.Min && d <= spec.Max;
			}
		}

		/// <summary>
		/// Applies one commit to a SCO's stored state: every writable value is kept (bounded), anything else is reported back as refused. On
		/// finish the session time (never more than <paramref name="maxSessionSeconds"/>, what really elapsed) is added to the total once and
		/// the exit decides whether the next launch resumes. The derived completion, success, score and satisfaction are refreshed.
		/// </summary>
		public static List<string> Apply(int kind, LmsScoState sco, IDictionary<string, string> values, bool finish, LmsPackageItem item, int maxSessionSeconds)
		{
			var refused = new List<string>();
			foreach (var pair in values ?? new Dictionary<string, string>())
			{
				if (!IsWritable(kind, pair.Key, pair.Value))
				{
					refused.Add(pair.Key);
					continue;
				}
				if (!sco.Values.ContainsKey(pair.Key) && sco.Values.Count >= MaxValuesPerSco)
				{
					refused.Add(pair.Key);
					continue;
				}
				sco.Values[pair.Key] = pair.Value ?? string.Empty;
			}

			if (sco.Values.Sum(v => v.Key.Length + (v.Value?.Length ?? 0)) > MaxStoredCharsPerSco)
				throw new InvalidOperationException("lms_runtime_state_too_large");

			var sessionKey = Is2004(kind) ? "cmi.session_time" : "cmi.core.session_time";
			var exitKey = Is2004(kind) ? "cmi.exit" : "cmi.core.exit";
			if (finish)
			{
				if (sco.Values.TryGetValue(sessionKey, out var session))
				{
					var seconds = Is2004(kind) ? ParseDuration(session) : ParseTimespan(session);
					sco.TotalSeconds += Math.Max(0, Math.Min(seconds ?? 0, Math.Max(0, maxSessionSeconds)));
					sco.Values.Remove(sessionKey);
				}
				sco.Suspended = sco.Values.TryGetValue(exitKey, out var exit) && exit == "suspend";
				sco.Values.Remove(exitKey);
			}

			Derive(kind, sco, item);
			return refused;
		}

		/// <summary>Completion, success, score percent and satisfaction from the stored values (and the item's mastery / thresholds).</summary>
		public static void Derive(int kind, LmsScoState sco, LmsPackageItem item)
		{
			string Get(string key) => sco.Values.TryGetValue(key, out var v) ? v : null;
			decimal? Num(string key) => decimal.TryParse(Get(key), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) ? d : (decimal?)null;

			if (Is2004(kind))
			{
				var completion = Get("cmi.completion_status") ?? "unknown";
				var progress = Num("cmi.progress_measure");
				if (item?.CompletionThreshold.HasValue == true && progress.HasValue)
					completion = progress.Value >= item.CompletionThreshold.Value ? "completed" : "incomplete";

				var success = Get("cmi.success_status") ?? "unknown";
				var scaled = Num("cmi.score.scaled");
				if (item?.MasteryScore.HasValue == true && scaled.HasValue)
					success = scaled.Value >= item.MasteryScore.Value ? "passed" : "failed";

				sco.Completion = completion;
				sco.Success = success;
				sco.ScorePercent = scaled.HasValue ? Clamp(scaled.Value * 100m) : Percent(Num("cmi.score.raw"), Num("cmi.score.min"), Num("cmi.score.max"));
				sco.Satisfied = success == "passed" || (completion == "completed" && success != "failed");
				return;
			}

			var status = Get("cmi.core.lesson_status") ?? "not attempted";
			var raw = Num("cmi.core.score.raw");
			// SCORM 1.2 §3.4.4: with a mastery score and a raw score the LMS decides passed or failed once the SCO reported it finished.
			if (item?.MasteryScore.HasValue == true && raw.HasValue && (status == "completed" || status == "passed" || status == "failed"))
				status = raw.Value >= item.MasteryScore.Value ? "passed" : "failed";
			sco.Completion = status == "passed" || status == "failed" ? "completed" : status;
			sco.Success = status == "passed" ? "passed" : status == "failed" ? "failed" : "unknown";
			sco.ScorePercent = Percent(raw, Num("cmi.core.score.min"), Num("cmi.core.score.max"));
			sco.Satisfied = status == "passed" || status == "completed";
		}

		private static decimal Clamp(decimal value) => Math.Round(Math.Max(0m, Math.Min(100m, value)), 2);

		private static decimal? Percent(decimal? raw, decimal? min, decimal? max)
		{
			if (!raw.HasValue)
				return null;
			if (max.HasValue && max.Value > (min ?? 0m))
				return Clamp((raw.Value - (min ?? 0m)) * 100m / (max.Value - (min ?? 0m)));
			return Clamp(raw.Value);
		}

		/// <summary>
		/// What the adapter starts with: the stored writable values (session time and exit excluded, they are per session) and the read-only
		/// elements the LMS owns (learner id and name, credit, entry, mode, total time, launch data, mastery or passing score).
		/// </summary>
		public static Dictionary<string, string> LaunchValues(int kind, LmsScoState sco, LmsPackageItem item, string learnerId, string learnerName, bool firstLaunch)
		{
			sco ??= new LmsScoState();
			var values = new Dictionary<string, string>(sco.Values ?? new Dictionary<string, string>(), StringComparer.Ordinal);
			var entry = sco.Suspended ? "resume" : firstLaunch ? "ab-initio" : string.Empty;
			if (Is2004(kind))
			{
				values["cmi._version"] = "1.0";
				values["cmi.learner_id"] = learnerId ?? string.Empty;
				values["cmi.learner_name"] = learnerName ?? string.Empty;
				values["cmi.credit"] = "credit";
				values["cmi.entry"] = entry;
				values["cmi.mode"] = "normal";
				values["cmi.total_time"] = FormatDuration(sco.TotalSeconds);
				values["cmi.launch_data"] = item?.LaunchData ?? string.Empty;
				values["cmi.scaled_passing_score"] = item?.MasteryScore?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
				values["cmi.completion_threshold"] = item?.CompletionThreshold?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
				values["cmi.max_time_allowed"] = string.Empty;
				values["cmi.time_limit_action"] = "continue,no message";
				if (!values.ContainsKey("cmi.completion_status")) values["cmi.completion_status"] = firstLaunch ? "not attempted" : "unknown";
				if (!values.ContainsKey("cmi.success_status")) values["cmi.success_status"] = "unknown";
				values.Remove("cmi.session_time");
				values.Remove("cmi.exit");
			}
			else
			{
				values["cmi.core.student_id"] = learnerId ?? string.Empty;
				values["cmi.core.student_name"] = learnerName ?? string.Empty;
				values["cmi.core.credit"] = "credit";
				values["cmi.core.entry"] = entry;
				values["cmi.core.lesson_mode"] = "normal";
				values["cmi.core.total_time"] = FormatTimespan(sco.TotalSeconds);
				values["cmi.launch_data"] = item?.LaunchData ?? string.Empty;
				values["cmi.student_data.mastery_score"] = item?.MasteryScore?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
				values["cmi.student_data.max_time_allowed"] = string.Empty;
				values["cmi.student_data.time_limit_action"] = "continue,no message";
				if (!values.ContainsKey("cmi.core.lesson_status")) values["cmi.core.lesson_status"] = "not attempted";
				values.Remove("cmi.core.session_time");
				values.Remove("cmi.core.exit");
			}
			return values;
		}

		/// <summary>SCORM 1.2 CMITimespan "HHHH:MM:SS.SS" in seconds; null when malformed.</summary>
		public static int? ParseTimespan(string value)
		{
			var m = Regex.Match(value ?? string.Empty, @"^(\d{2,4}):(\d{2}):(\d{2})(\.\d{1,2})?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
			if (!m.Success)
				return null;
			var minutes = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
			var seconds = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
			if (minutes > 59 || seconds > 59)
				return null;
			return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 3600 + minutes * 60 + seconds;
		}

		/// <summary>SCORM 2004 timeinterval (ISO 8601 duration, e.g. "PT1H2M3.5S") in whole seconds; null when malformed.</summary>
		public static int? ParseDuration(string value)
		{
			var m = Regex.Match(value ?? string.Empty, @"^P(?:(\d+)Y)?(?:(\d+)M)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+(?:\.\d{1,2})?)S)?)?$",
				RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
			if (!m.Success || value == "P" || value.EndsWith("T", StringComparison.Ordinal))
				return null;
			double Part(int group) => m.Groups[group].Success ? double.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture) : 0;
			var total = Part(1) * 365 * 86400 + Part(2) * 30 * 86400 + Part(3) * 86400 + Part(4) * 3600 + Part(5) * 60 + Part(6);
			return total > int.MaxValue ? int.MaxValue : (int)Math.Floor(total);
		}

		public static string FormatTimespan(int seconds)
		{
			seconds = Math.Max(0, seconds);
			return Math.Min(9999, seconds / 3600).ToString("0000", CultureInfo.InvariantCulture) + ":" + (seconds / 60 % 60).ToString("00", CultureInfo.InvariantCulture) + ":" +
				(seconds % 60).ToString("00", CultureInfo.InvariantCulture) + ".00";
		}

		public static string FormatDuration(int seconds)
		{
			seconds = Math.Max(0, seconds);
			return "PT" + (seconds / 3600).ToString(CultureInfo.InvariantCulture) + "H" + (seconds / 60 % 60).ToString(CultureInfo.InvariantCulture) + "M" +
				(seconds % 60).ToString(CultureInfo.InvariantCulture) + "S";
		}

		/// <summary>A registration is satisfied when every SCO of the package is.</summary>
		public static bool AllSatisfied(LmsPackageManifest manifest, LmsScormState state) =>
			manifest?.Items != null && manifest.Items.Count > 0 && manifest.Items.All(i => state?.Scos != null && state.Scos.TryGetValue(i.Identifier ?? string.Empty, out var s) && s.Satisfied);
	}

	/// <summary>
	/// cmi5 (the xAPI profile for LMS launches): the verbs and extensions it defines, the moveOn rule, the LMS.LaunchData document, and the
	/// checks a statement from an AU must pass before the minimal LRS stores it (right actor, registration, session and activity).
	/// </summary>
	public static class LmsCmi5Rules
	{
		public const string Launched = "http://adlnet.gov/expapi/verbs/launched";
		public const string Initialized = "http://adlnet.gov/expapi/verbs/initialized";
		public const string Completed = "http://adlnet.gov/expapi/verbs/completed";
		public const string Passed = "http://adlnet.gov/expapi/verbs/passed";
		public const string Failed = "http://adlnet.gov/expapi/verbs/failed";
		public const string Abandoned = "https://w3id.org/xapi/adl/verbs/abandoned";
		public const string Waived = "https://w3id.org/xapi/adl/verbs/waived";
		public const string Terminated = "http://adlnet.gov/expapi/verbs/terminated";
		public const string Satisfied = "https://w3id.org/xapi/adl/verbs/satisfied";

		public const string CategoryCmi5 = "https://w3id.org/xapi/cmi5/context/categories/cmi5";
		public const string CategoryMoveOn = "https://w3id.org/xapi/cmi5/context/categories/moveon";
		public const string SessionIdExtension = "https://w3id.org/xapi/cmi5/context/extensions/sessionid";
		public const string LaunchDataStateId = "LMS.LaunchData";

		private static readonly HashSet<string> DefinedByAu = new HashSet<string>(StringComparer.Ordinal) { Initialized, Completed, Passed, Failed, Terminated };
		private static readonly HashSet<string> LmsOnly = new HashSet<string>(StringComparer.Ordinal) { Launched, Abandoned, Waived, Satisfied };

		/// <summary>The AU's moveOn criterion is met (cmi5 §9.6.2; NotApplicable, the default, is met on launch).</summary>
		public static bool IsSatisfied(string moveOn, LmsCmi5AuState au)
		{
			au ??= new LmsCmi5AuState();
			switch ((moveOn ?? "NotApplicable").Trim())
			{
				case "Passed": return au.Passed;
				case "Completed": return au.Completed;
				case "CompletedAndPassed": return au.Completed && au.Passed;
				case "CompletedOrPassed": return au.Completed || au.Passed;
				default: return true;
			}
		}

		/// <summary>The LMS.LaunchData state document the AU reads on start (cmi5 §10.2.1).</summary>
		public static JObject LaunchData(LmsPackageItem au, string sessionId, string packageActivityId)
		{
			var data = new JObject
			{
				["contextTemplate"] = new JObject
				{
					["contextActivities"] = new JObject { ["grouping"] = new JArray(new JObject { ["objectType"] = "Activity", ["id"] = packageActivityId }) },
					["extensions"] = new JObject { [SessionIdExtension] = sessionId }
				},
				["launchMode"] = "Normal",
				["launchMethod"] = string.IsNullOrWhiteSpace(au?.LaunchMethod) ? "AnyWindow" : au.LaunchMethod,
				["moveOn"] = string.IsNullOrWhiteSpace(au?.MoveOn) ? "NotApplicable" : au.MoveOn
			};
			if (au?.MasteryScore.HasValue == true)
				data["masteryScore"] = au.MasteryScore.Value;
			if (!string.IsNullOrWhiteSpace(au?.LaunchData))
				data["launchParameters"] = au.LaunchData;
			return data;
		}

		/// <summary>The result of checking one statement an AU sent.</summary>
		public sealed class StatementCheck
		{
			/// <summary>0 when accepted; 400 (malformed) or 403 (not this learner's, session or AU) otherwise.</summary>
			public int Status { get; set; }
			public string Reason { get; set; }
			public string StatementId { get; set; }
			public string VerbId { get; set; }
			public JObject Statement { get; set; }
			public bool IsCmi5Defined { get; set; }
		}

		/// <summary>
		/// Checks one statement from the AU of a launch: the actor is the launch's account, the registration and session are the launch's, an
		/// LMS-only verb is refused, and a cmi5-defined statement is about the AU itself with the cmi5 category and its result rules. The
		/// accepted statement gets an id (when it had none), a timestamp, "stored" and the LMS as its authority.
		/// </summary>
		public static StatementCheck Check(JObject statement, string homePage, string accountName, string registration, string auId, string sessionId, string authorityHomePage,
			DateTime utcNow)
		{
			StatementCheck Refuse(int status, string reason) => new StatementCheck { Status = status, Reason = reason };
			if (statement == null)
				return Refuse(400, "statement");

			var actor = statement["actor"] as JObject;
			var account = actor?["account"] as JObject;
			if (account == null || actor["mbox"] != null || actor["mbox_sha1sum"] != null || actor["openid"] != null ||
				!string.Equals((string)account["homePage"], homePage, StringComparison.Ordinal) || !string.Equals((string)account["name"], accountName, StringComparison.Ordinal))
				return Refuse(403, "actor");

			var verbId = (string)(statement["verb"] as JObject)?["id"];
			if (string.IsNullOrWhiteSpace(verbId) || !Uri.TryCreate(verbId, UriKind.Absolute, out _))
				return Refuse(400, "verb");
			if (LmsOnly.Contains(verbId))
				return Refuse(403, "lms-verb");

			var objectId = (string)(statement["object"] as JObject)?["id"];
			if (string.IsNullOrWhiteSpace(objectId))
				return Refuse(400, "object");

			var context = statement["context"] as JObject;
			if (!string.Equals((string)context?["registration"], registration, StringComparison.OrdinalIgnoreCase))
				return Refuse(403, "registration");
			if (!string.Equals((string)(context?["extensions"] as JObject)?[SessionIdExtension], sessionId, StringComparison.OrdinalIgnoreCase))
				return Refuse(403, "session");

			var defined = DefinedByAu.Contains(verbId);
			if (defined)
			{
				if (!string.Equals(objectId, auId, StringComparison.Ordinal))
					return Refuse(403, "activity");
				var categories = (context["contextActivities"] as JObject)?["category"] as JArray;
				if (categories == null || !categories.OfType<JObject>().Any(c => string.Equals((string)c["id"], CategoryCmi5, StringComparison.Ordinal)))
					return Refuse(400, "category");
				var result = statement["result"] as JObject;
				if (verbId == Completed && result?["completion"]?.Type != JTokenType.Boolean)
					return Refuse(400, "completion");
				if ((verbId == Passed || verbId == Failed) && result?["success"]?.Type != JTokenType.Boolean)
					return Refuse(400, "success");
			}

			var id = (string)statement["id"];
			if (string.IsNullOrWhiteSpace(id))
				id = Guid.NewGuid().ToString();
			else if (!Guid.TryParse(id, out _))
				return Refuse(400, "id");

			var normalized = (JObject)statement.DeepClone();
			normalized["id"] = id.ToLowerInvariant();
			if (normalized["timestamp"] == null)
				normalized["timestamp"] = utcNow.ToString("o", CultureInfo.InvariantCulture);
			normalized["stored"] = utcNow.ToString("o", CultureInfo.InvariantCulture);
			normalized["authority"] = new JObject { ["objectType"] = "Agent", ["account"] = new JObject { ["homePage"] = authorityHomePage, ["name"] = "resgrid-lms" } };
			return new StatementCheck { StatementId = id.ToLowerInvariant(), VerbId = verbId, Statement = normalized, IsCmi5Defined = defined };
		}

		/// <summary>Applies an accepted cmi5-defined statement to the AU's results (completed, passed, failed, the best scaled score).</summary>
		public static void Apply(LmsCmi5AuState au, string verbId, JObject statement)
		{
			var token = ((statement?["result"] as JObject)?["score"] as JObject)?["scaled"];
			decimal? scaled = token != null && (token.Type == JTokenType.Float || token.Type == JTokenType.Integer) ? (decimal)token : (decimal?)null;
			switch (verbId)
			{
				case Completed:
					au.Completed = true;
					break;
				case Passed:
					au.Passed = true;
					break;
				case Failed:
					au.Failed = true;
					break;
			}
			if (scaled.HasValue && (verbId == Passed || verbId == Failed) && (!au.ScoreScaled.HasValue || scaled.Value > au.ScoreScaled.Value))
				au.ScoreScaled = Math.Max(-1m, Math.Min(1m, scaled.Value));
		}

		/// <summary>A statement the LMS itself records in the registration (launched, satisfied).</summary>
		public static JObject LmsStatement(string verbId, string display, JObject actor, string objectId, string registration, string sessionId, string authorityHomePage, DateTime utcNow)
		{
			var context = new JObject
			{
				["registration"] = registration,
				["contextActivities"] = new JObject { ["category"] = new JArray(new JObject { ["id"] = CategoryCmi5 }) }
			};
			if (sessionId != null)
				context["extensions"] = new JObject { [SessionIdExtension] = sessionId };
			return new JObject
			{
				["id"] = Guid.NewGuid().ToString(),
				["actor"] = actor,
				["verb"] = new JObject { ["id"] = verbId, ["display"] = new JObject { ["en-US"] = display } },
				["object"] = new JObject { ["objectType"] = "Activity", ["id"] = objectId },
				["context"] = context,
				["timestamp"] = utcNow.ToString("o", CultureInfo.InvariantCulture),
				["stored"] = utcNow.ToString("o", CultureInfo.InvariantCulture),
				["authority"] = new JObject { ["objectType"] = "Agent", ["account"] = new JObject { ["homePage"] = authorityHomePage, ["name"] = "resgrid-lms" } }
			};
		}
	}

	/// <summary>Builds the xAPI statements the outbox sends (course- and lesson-level learning events).</summary>
	public static partial class LmsXapiStatements
	{
		public const string CourseActivityType = "http://adlnet.gov/expapi/activities/course";
		public const string LessonActivityType = "http://adlnet.gov/expapi/activities/lesson";

		/// <summary>The activity id of a department course (and lesson) on this Resgrid site.</summary>
		public static string ActivityId(int departmentId, int courseId, int? lessonId) =>
			LmsLearnerIdentity.HomePage(departmentId) + "/courses/" + courseId.ToString(CultureInfo.InvariantCulture) +
			(lessonId.HasValue ? "/lessons/" + lessonId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);

		/// <summary>
		/// The statement for <paramref name="e"/>. The actor is the department-scoped account, or the member's e-mail address only when
		/// <paramref name="email"/> is given (the department opted in). Nothing else about the member is sent.
		/// </summary>
		public static JObject Build(LmsXapiEvent e, string statementId, string email)
		{
			JObject actor = !string.IsNullOrWhiteSpace(email)
				? new JObject { ["objectType"] = "Agent", ["mbox"] = "mailto:" + email.Trim() }
				: LmsLearnerIdentity.AccountActor(e.DepartmentId, e.UserId);

			var title = e.LmsLessonId.HasValue ? e.LessonTitle ?? e.CourseTitle : e.CourseTitle;
			var activity = new JObject
			{
				["objectType"] = "Activity",
				["id"] = ActivityId(e.DepartmentId, e.LmsCourseId, e.LmsLessonId),
				["definition"] = new JObject
				{
					["type"] = e.LmsLessonId.HasValue ? LessonActivityType : CourseActivityType,
					["name"] = new JObject { ["en-US"] = string.IsNullOrWhiteSpace(title) ? "Course" : title }
				}
			};

			var statement = new JObject
			{
				["id"] = statementId,
				["actor"] = actor,
				["verb"] = new JObject { ["id"] = e.VerbId, ["display"] = new JObject { ["en-US"] = LmsXapiVerbs.Display(e.VerbId) ?? "experienced" } },
				["object"] = activity,
				["timestamp"] = DateTime.SpecifyKind(e.OccurredOn, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture),
				["context"] = new JObject { ["platform"] = "Resgrid" }
			};
			if (e.LmsLessonId.HasValue)
				((JObject)statement["context"])["contextActivities"] = new JObject
				{
					["parent"] = new JArray(new JObject { ["objectType"] = "Activity", ["id"] = ActivityId(e.DepartmentId, e.LmsCourseId, null) })
				};

			var result = new JObject();
			if (e.Completion.HasValue) result["completion"] = e.Completion.Value;
			if (e.Success.HasValue) result["success"] = e.Success.Value;
			if (e.ScorePercent.HasValue) result["score"] = new JObject { ["scaled"] = Math.Round(Math.Max(0m, Math.Min(100m, e.ScorePercent.Value)) / 100m, 4) };
			if (e.DurationSeconds.HasValue && e.DurationSeconds.Value > 0) result["duration"] = LmsScormRuntime.FormatDuration(e.DurationSeconds.Value);
			if (result.HasValues)
				statement["result"] = result;
			return statement;
		}

		/// <summary>The wait before attempt number <paramref name="attempts"/> + 1: doubling from a minute, capped, with up to 20% jitter.</summary>
		public static TimeSpan Backoff(int attempts, Random random)
		{
			var minutes = Math.Min(Math.Pow(2, Math.Max(0, Math.Min(attempts, 20) - 1)), Math.Max(1, Config.LmsStandardsConfig.XapiMaxBackoffMinutes));
			var jitter = 1.0 + (random?.NextDouble() ?? 0) * 0.2;
			return TimeSpan.FromMinutes(Math.Min(minutes * jitter, Math.Max(1, Config.LmsStandardsConfig.XapiMaxBackoffMinutes)));
		}

		/// <summary>Whether an LRS answer is final (delivered, or refused for good) or worth another attempt.</summary>
		public static (bool Delivered, bool Retry) Classify(int statusCode)
		{
			if (statusCode >= 200 && statusCode < 300)
				return (true, false);
			if (statusCode == 409)
				return (true, false);                                // the LRS already holds a statement with this id
			if (statusCode == 408 || statusCode == 429 || statusCode == 401 || statusCode == 403 || statusCode >= 500 || statusCode <= 0)
				return (false, true);                                // timeouts, throttling, credentials being fixed, server and network errors
			return (false, false);                                   // 400, 404, 413 ... will keep failing
		}
	}

	/// <summary>LTI 1.3 (platform side): the claims of a resource link launch, the tool's settings as entered, and the score rule.</summary>
	public static class LmsLtiRules
	{
		public const string ClaimMessageType = "https://purl.imsglobal.org/spec/lti/claim/message_type";
		public const string ClaimVersion = "https://purl.imsglobal.org/spec/lti/claim/version";
		public const string ClaimDeploymentId = "https://purl.imsglobal.org/spec/lti/claim/deployment_id";
		public const string ClaimTargetLinkUri = "https://purl.imsglobal.org/spec/lti/claim/target_link_uri";
		public const string ClaimResourceLink = "https://purl.imsglobal.org/spec/lti/claim/resource_link";
		public const string ClaimRoles = "https://purl.imsglobal.org/spec/lti/claim/roles";
		public const string ClaimContext = "https://purl.imsglobal.org/spec/lti/claim/context";
		public const string ClaimToolPlatform = "https://purl.imsglobal.org/spec/lti/claim/tool_platform";
		public const string ClaimLaunchPresentation = "https://purl.imsglobal.org/spec/lti/claim/launch_presentation";
		public const string ClaimCustom = "https://purl.imsglobal.org/spec/lti/claim/custom";
		public const string ClaimAgsEndpoint = "https://purl.imsglobal.org/spec/lti-ags/claim/endpoint";

		public const string RoleLearner = "http://purl.imsglobal.org/vocab/lis/v2/membership#Learner";
		public const string ContextCourseOffering = "http://purl.imsglobal.org/vocab/lis/v2/course#CourseOffering";
		public const string ScopeScore = "https://purl.imsglobal.org/spec/lti-ags/scope/score";
		public const string ScopeLineItemReadOnly = "https://purl.imsglobal.org/spec/lti-ags/scope/lineitem.readonly";
		public const string ScopeResultReadOnly = "https://purl.imsglobal.org/spec/lti-ags/scope/result.readonly";
		public const string ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

		public static TimeSpan RetiredKeyPublishedFor => TimeSpan.FromDays(Math.Max(1, Config.LmsStandardsConfig.LtiRetiredKeyDays));

		private static readonly Regex CustomKey = new Regex("^[A-Za-z0-9_]{1,64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		/// <summary>The platform issuer of a department (each department is its own platform instance; its key set is its own).</summary>
		public static string Issuer(int departmentId) => LmsStandardsUrls.SiteBase + "/lti/" + departmentId.ToString(CultureInfo.InvariantCulture);

		public static string JwksUrl(int departmentId) => Issuer(departmentId) + "/jwks";

		public static string TokenUrl(int departmentId) => Issuer(departmentId) + "/token";

		public static string AuthorizationUrl => LmsStandardsUrls.SiteBase + "/lti/authorize";

		public static string LineItemUrl(int departmentId, int lessonId) => Issuer(departmentId) + "/ags/" + lessonId.ToString(CultureInfo.InvariantCulture) + "/lineitem";

		/// <summary>Custom parameters as entered ("key=value" per line); malformed lines are dropped, at most 50 kept.</summary>
		public static Dictionary<string, string> ParseCustom(params string[] blocks)
		{
			var result = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var block in blocks ?? Array.Empty<string>())
				foreach (var raw in (block ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
				{
					var equals = raw.IndexOf('=');
					if (equals <= 0)
						continue;
					var key = raw.Substring(0, equals).Trim();
					var value = raw.Substring(equals + 1).Trim();
					if (!CustomKey.IsMatch(key) || value.Length > 1000 || (result.Count >= 50 && !result.ContainsKey(key)))
						continue;
					result[key] = value;
				}
			return result;
		}

		/// <summary>The redirect URIs an id_token may go to: the registered list, or the launch URL alone.</summary>
		public static List<string> RedirectUris(LmsLtiTool tool)
		{
			var list = (tool?.RedirectUris ?? string.Empty).Replace("\r", string.Empty).Split('\n').Select(u => u.Trim()).Where(LmsStandardsUrls.IsAcceptableHttpsUrl).ToList();
			if (list.Count == 0 && LmsStandardsUrls.IsAcceptableHttpsUrl(tool?.LaunchUrl))
				list.Add(tool.LaunchUrl.Trim());
			return list;
		}

		/// <summary>The claims of an LtiResourceLinkRequest id_token (the JWT standard claims are added when it is signed).</summary>
		public static Dictionary<string, object> LaunchClaims(int departmentId, LmsLtiTool tool, LmsLesson lesson, LmsCourse course, string subject, string nonce, string locale,
			string name, string givenName, string familyName, string email, string returnUrl = null)
		{
			var target = LmsStandardsUrls.IsAcceptableHttpsUrl(lesson?.LtiTargetLinkUri) ? lesson.LtiTargetLinkUri.Trim() : tool.LaunchUrl;
			var claims = new Dictionary<string, object>(StringComparer.Ordinal)
			{
				["sub"] = subject,
				["nonce"] = nonce,
				["azp"] = tool.ClientId,
				[ClaimMessageType] = "LtiResourceLinkRequest",
				[ClaimVersion] = "1.3.0",
				[ClaimDeploymentId] = tool.DeploymentId,
				[ClaimTargetLinkUri] = target,
				[ClaimResourceLink] = new Dictionary<string, object> { ["id"] = "lesson-" + lesson.LmsLessonId.ToString(CultureInfo.InvariantCulture), ["title"] = lesson.Title ?? string.Empty },
				[ClaimRoles] = new[] { RoleLearner },
				[ClaimContext] = new Dictionary<string, object>
				{
					["id"] = "course-" + (course?.LmsCourseId ?? 0).ToString(CultureInfo.InvariantCulture),
					["title"] = course?.Title ?? string.Empty,
					["type"] = new[] { ContextCourseOffering }
				},
				[ClaimToolPlatform] = new Dictionary<string, object>
				{
					["guid"] = Issuer(departmentId),
					["name"] = "Resgrid",
					["product_family_code"] = "resgrid"
				},
				[ClaimLaunchPresentation] = new Dictionary<string, object> { ["document_target"] = "window", ["locale"] = string.IsNullOrWhiteSpace(locale) ? "en" : locale }
			};

			// Round 10 A8: an in-app launch names where the tool may send the learner back (the app's page that reports the lesson's state).
			if (!string.IsNullOrWhiteSpace(returnUrl))
				((Dictionary<string, object>)claims[ClaimLaunchPresentation])["return_url"] = returnUrl;

			var custom = ParseCustom(tool.CustomParameters, lesson?.LtiCustomParameters);
			if (custom.Count > 0)
				claims[ClaimCustom] = custom;
			if (tool.AllowGradePassback)
				claims[ClaimAgsEndpoint] = new Dictionary<string, object>
				{
					["scope"] = new[] { ScopeLineItemReadOnly, ScopeScore },
					["lineitem"] = LineItemUrl(departmentId, lesson.LmsLessonId)
				};
			if (tool.ShareName && !string.IsNullOrWhiteSpace(name))
			{
				claims["name"] = name;
				if (!string.IsNullOrWhiteSpace(givenName)) claims["given_name"] = givenName;
				if (!string.IsNullOrWhiteSpace(familyName)) claims["family_name"] = familyName;
			}
			if (tool.ShareEmail && !string.IsNullOrWhiteSpace(email))
				claims["email"] = email;
			return claims;
		}

		/// <summary>
		/// What a score means for the lesson (AGS): satisfied once the tool says the activity is Completed and FullyGraded, at or above the
		/// lesson's pass mark (a pass mark of 0 needs no score). The percent is the given score over the maximum.
		/// </summary>
		public static (bool Satisfied, decimal? Percent) Evaluate(LmsLtiScoreState score, int passMark)
		{
			decimal? percent = null;
			if (score?.ScoreGiven.HasValue == true && score.ScoreMaximum.HasValue && score.ScoreMaximum.Value > 0)
				percent = Math.Round(Math.Max(0m, Math.Min(100m, score.ScoreGiven.Value * 100m / score.ScoreMaximum.Value)), 2);
			var finished = string.Equals(score?.ActivityProgress, "Completed", StringComparison.Ordinal) && string.Equals(score?.GradingProgress, "FullyGraded", StringComparison.Ordinal);
			if (!finished)
				return (false, percent);
			if (passMark <= 0)
				return (true, percent);
			return (percent.HasValue && percent.Value >= passMark, percent);
		}

		private static readonly HashSet<string> ActivityProgressValues = new HashSet<string>(StringComparer.Ordinal) { "Initialized", "Started", "InProgress", "Submitted", "Completed" };
		private static readonly HashSet<string> GradingProgressValues = new HashSet<string>(StringComparer.Ordinal) { "FullyGraded", "Pending", "PendingManual", "Failed", "NotReady" };

		/// <summary>Reads an AGS score document; null when it is malformed (the endpoint answers 400).</summary>
		public static (string UserId, LmsLtiScoreState Score) ParseScore(JObject body)
		{
			if (body == null)
				return (null, null);
			var userId = (string)body["userId"];
			var activity = (string)body["activityProgress"];
			var grading = (string)body["gradingProgress"];
			if (string.IsNullOrWhiteSpace(userId) || !ActivityProgressValues.Contains(activity ?? string.Empty) || !GradingProgressValues.Contains(grading ?? string.Empty))
				return (null, null);
			decimal? given = null, maximum = null;
			if (body["scoreGiven"] != null && body["scoreGiven"].Type != JTokenType.Null)
			{
				if (body["scoreGiven"].Type != JTokenType.Float && body["scoreGiven"].Type != JTokenType.Integer)
					return (null, null);
				given = (decimal)body["scoreGiven"];
				if (body["scoreMaximum"] == null || (body["scoreMaximum"].Type != JTokenType.Float && body["scoreMaximum"].Type != JTokenType.Integer))
					return (null, null);
				maximum = (decimal)body["scoreMaximum"];
				if (given < 0 || maximum <= 0)
					return (null, null);
			}
			DateTime? timestamp = null;
			if (body["timestamp"] != null)
			{
				if (!DateTime.TryParse((string)body["timestamp"], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
					return (null, null);
				timestamp = parsed;
			}
			var comment = (string)body["comment"];
			return (userId.Trim(), new LmsLtiScoreState
			{
				ScoreGiven = given,
				ScoreMaximum = maximum,
				ActivityProgress = activity,
				GradingProgress = grading,
				Comment = comment != null && comment.Length > 2000 ? comment.Substring(0, 2000) : comment,
				Timestamp = timestamp
			});
		}
	}
}
