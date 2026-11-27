using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Model.Lms
{
	/// <summary>
	/// A small xAPI 1.0.3 statement builder for the data exit (round 6D). Round 6C's LRS outbox builds its own statements; this one is
	/// deliberately standalone and writes the same shape an LRS accepts, so the outbox can adopt it later:
	/// <list type="bullet">
	/// <item>actor: an Agent identified by account (homePage = the install's base URL, name = the Resgrid user id), with the member's name;</item>
	/// <item>verbs from the ADL vocabulary (completed, passed, failed, attempted);</item>
	/// <item>object: an Activity whose id is an IRI under the install's base URL, with an ADL activity type;</item>
	/// <item>statement ids are name-based UUIDs (version 5) of a stable seed, so exporting the same record twice yields the same id and an
	/// LRS stores it once.</item>
	/// </list>
	/// Pure: no clock, no I/O.
	/// </summary>
	public static partial class LmsXapiStatements
	{
		public const string Version = "1.0.3";

		public const string VerbCompleted = "http://adlnet.gov/expapi/verbs/completed";
		public const string VerbPassed = "http://adlnet.gov/expapi/verbs/passed";
		public const string VerbFailed = "http://adlnet.gov/expapi/verbs/failed";
		public const string VerbAttempted = "http://adlnet.gov/expapi/verbs/attempted";

		public const string ActivityCourse = "http://adlnet.gov/expapi/activities/course";
		public const string ActivityAssessment = "http://adlnet.gov/expapi/activities/assessment";
		public const string ActivityMeeting = "http://adlnet.gov/expapi/activities/meeting";

		/// <summary>The fixed namespace of Resgrid's context extension IRIs (stable across installs).</summary>
		public const string ExtensionBase = "https://resgrid.com/xapi/extensions/";

		/// <summary>The namespace of the version 5 statement ids (a fixed random UUID).</summary>
		private static readonly Guid StatementNamespace = new Guid("6f1d2c64-8a1e-4b9e-9f0e-3c5b7a2d4e81");

		/// <summary>A deterministic (RFC 4122 version 5, SHA-1) UUID of <paramref name="seed"/>.</summary>
		public static Guid StatementId(string seed)
		{
			var namespaceBytes = StatementNamespace.ToByteArray();
			SwapByteOrder(namespaceBytes);
			var nameBytes = Encoding.UTF8.GetBytes(seed ?? string.Empty);
			byte[] hash;
			using (var sha1 = SHA1.Create())
			{
				var input = new byte[namespaceBytes.Length + nameBytes.Length];
				Buffer.BlockCopy(namespaceBytes, 0, input, 0, namespaceBytes.Length);
				Buffer.BlockCopy(nameBytes, 0, input, namespaceBytes.Length, nameBytes.Length);
				hash = sha1.ComputeHash(input);
			}

			var uuid = new byte[16];
			Array.Copy(hash, uuid, 16);
			uuid[6] = (byte)((uuid[6] & 0x0F) | 0x50);
			uuid[8] = (byte)((uuid[8] & 0x3F) | 0x80);
			SwapByteOrder(uuid);
			return new Guid(uuid);
		}

		private static void SwapByteOrder(byte[] guid)
		{
			void Swap(int a, int b) => (guid[a], guid[b]) = (guid[b], guid[a]);
			Swap(0, 3);
			Swap(1, 2);
			Swap(4, 5);
			Swap(6, 7);
		}

		/// <summary>The install's base URL without a trailing slash (the actor home page and the activity id root).</summary>
		public static string HomePage(string baseUrl) => string.IsNullOrWhiteSpace(baseUrl) ? "https://resgrid.local" : baseUrl.Trim().TrimEnd('/');

		public static JObject Actor(string userId, string name, string baseUrl)
		{
			var actor = new JObject
			{
				["objectType"] = "Agent",
				["account"] = new JObject { ["homePage"] = HomePage(baseUrl), ["name"] = userId ?? string.Empty }
			};
			if (!string.IsNullOrWhiteSpace(name))
				actor["name"] = name.Trim();
			return actor;
		}

		public static JObject Verb(string id)
		{
			var display = id == VerbPassed ? "passed" : id == VerbFailed ? "failed" : id == VerbAttempted ? "attempted" : "completed";
			return new JObject { ["id"] = id, ["display"] = new JObject { ["en-US"] = display } };
		}

		public static JObject Activity(string id, string type, string name, string locale = "en-US")
		{
			var definition = new JObject { ["type"] = type };
			if (!string.IsNullOrWhiteSpace(name))
				definition["name"] = new JObject { [string.IsNullOrWhiteSpace(locale) ? "en-US" : locale] = name.Trim() };
			return new JObject { ["objectType"] = "Activity", ["id"] = id, ["definition"] = definition };
		}

		/// <summary>An activity id under the install: {base}/xapi/activities/department/{id}/{kind}/{escaped key}.</summary>
		public static string ActivityId(string baseUrl, int departmentId, string kind, string key) =>
			HomePage(baseUrl) + "/xapi/activities/department/" + departmentId.ToString(CultureInfo.InvariantCulture) + "/" + kind + "/" + Uri.EscapeDataString(key ?? string.Empty);

		/// <summary>ISO 8601 duration of <paramref name="hours"/> (PT1H30M); null for none.</summary>
		public static string Duration(decimal hours)
		{
			if (hours <= 0)
				return null;
			var totalMinutes = (long)Math.Round(hours * 60m, MidpointRounding.AwayFromZero);
			var h = totalMinutes / 60;
			var m = totalMinutes % 60;
			return "PT" + (h > 0 ? h.ToString(CultureInfo.InvariantCulture) + "H" : string.Empty) + (m > 0 || h == 0 ? m.ToString(CultureInfo.InvariantCulture) + "M" : string.Empty);
		}

		public static string Timestamp(DateTime value) =>
			DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

		private static JObject Statement(string seed, JObject actor, string verb, JObject activity, JObject result, DateTime timestamp, JObject extensions)
		{
			var statement = new JObject
			{
				["id"] = StatementId(seed).ToString("D"),
				["version"] = Version,
				["actor"] = actor,
				["verb"] = Verb(verb),
				["object"] = activity
			};
			if (result != null && result.HasValues)
				statement["result"] = result;
			statement["timestamp"] = Timestamp(timestamp);
			var context = new JObject { ["platform"] = "Resgrid" };
			if (extensions != null && extensions.HasValues)
				context["extensions"] = extensions;
			statement["context"] = context;
			return statement;
		}

		/// <summary>
		/// "completed" for a verified learning record; null for pending, rejected and voided records (they never counted, and an LRS has
		/// no "rejected" verb). <paramref name="activityId"/> is the course or session the record is about.
		/// </summary>
		public static JObject ForLearningRecord(LearningRecord record, string memberName, string baseUrl, string activityId, string activityType, string categoryName)
		{
			if (record == null || record.Status != (int)LearningRecordStatuses.Verified)
				return null;

			var result = new JObject { ["completion"] = true };
			var duration = Duration(record.Hours);
			if (duration != null)
				result["duration"] = duration;

			var extensions = new JObject
			{
				[ExtensionBase + "department-id"] = record.DepartmentId,
				[ExtensionBase + "learning-record-id"] = record.LearningRecordId,
				[ExtensionBase + "source-kind"] = Enum.IsDefined(typeof(LearningRecordSourceKinds), record.SourceKind) ? ((LearningRecordSourceKinds)record.SourceKind).ToString() : record.SourceKind.ToString(CultureInfo.InvariantCulture),
				[ExtensionBase + "hours"] = record.Hours
			};
			if (!string.IsNullOrWhiteSpace(record.CourseCode))
				extensions[ExtensionBase + "course-code"] = record.CourseCode;
			if (!string.IsNullOrWhiteSpace(categoryName))
				extensions[ExtensionBase + "category"] = categoryName;
			if (!string.IsNullOrWhiteSpace(record.CeProvider))
				extensions[ExtensionBase + "ce-provider"] = record.CeProvider;
			if (!string.IsNullOrWhiteSpace(record.CeCourseNumber))
				extensions[ExtensionBase + "ce-course-number"] = record.CeCourseNumber;
			if (record.CeHours.HasValue)
				extensions[ExtensionBase + "ce-hours"] = record.CeHours.Value;

			return Statement("learning-record:" + record.DepartmentId.ToString(CultureInfo.InvariantCulture) + ":" + record.LearningRecordId.ToString(CultureInfo.InvariantCulture),
				Actor(record.UserId, memberName, baseUrl), VerbCompleted, Activity(activityId, activityType ?? ActivityCourse, record.Title), result, record.CompletedOn, extensions);
		}

		/// <summary>
		/// "passed" or "failed" for a scored or graded attempt, "attempted" for one waiting for a grader or expired; null while in progress.
		/// The score is scaled 0–1 from the attempt's percent; answers never go into a statement.
		/// </summary>
		public static JObject ForAttempt(LmsAssessmentAttempt attempt, string memberName, string baseUrl, string lessonTitle)
		{
			if (attempt == null || attempt.Status == (int)LmsAttemptStatuses.InProgress)
				return null;

			var verb = attempt.Passed == true ? VerbPassed : attempt.Passed == false ? VerbFailed : VerbAttempted;
			var result = new JObject
			{
				["score"] = new JObject
				{
					["scaled"] = Math.Round(Math.Clamp(attempt.Percent, 0m, 100m) / 100m, 4),
					["raw"] = attempt.Score,
					["min"] = 0,
					["max"] = attempt.MaxScore
				},
				["completion"] = attempt.Status != (int)LmsAttemptStatuses.Expired
			};
			if (attempt.Passed.HasValue)
				result["success"] = attempt.Passed.Value;
			if (attempt.SubmittedOn.HasValue && attempt.SubmittedOn.Value > attempt.StartedOn)
				result["duration"] = Duration((decimal)(attempt.SubmittedOn.Value - attempt.StartedOn).TotalHours) ?? "PT0M";

			var extensions = new JObject
			{
				[ExtensionBase + "department-id"] = attempt.DepartmentId,
				[ExtensionBase + "attempt-id"] = attempt.LmsAssessmentAttemptId,
				[ExtensionBase + "attempt-number"] = attempt.AttemptNumber,
				[ExtensionBase + "course-version-id"] = attempt.LmsCourseVersionId,
				[ExtensionBase + "pass-mark"] = attempt.PassMark
			};

			var activityId = ActivityId(baseUrl, attempt.DepartmentId, "assessment", attempt.LmsLessonId.ToString(CultureInfo.InvariantCulture));
			return Statement("assessment-attempt:" + attempt.DepartmentId.ToString(CultureInfo.InvariantCulture) + ":" + attempt.LmsAssessmentAttemptId.ToString(CultureInfo.InvariantCulture),
				Actor(attempt.UserId, memberName, baseUrl), verb, Activity(activityId, ActivityAssessment, lessonTitle), result, attempt.SubmittedOn ?? attempt.StartedOn, extensions);
		}

		/// <summary>One statement as a JSON line (no indentation, no trailing newline).</summary>
		public static string ToJsonLine(JObject statement) => statement == null ? null : statement.ToString(Formatting.None);
	}
}
