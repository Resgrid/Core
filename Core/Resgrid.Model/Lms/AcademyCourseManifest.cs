using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Academy;

namespace Resgrid.Model.Lms
{
	/// <summary>One question of an Academy lesson check: its kind and key; the text is localized (Academy area, 10 locales).</summary>
	public sealed class AcademyQuestionDefinition
	{
		public AcademyQuestionDefinition(string key, LmsQuestionKinds kind, string correct)
		{
			Key = key;
			Kind = kind;
			Correct = correct;
		}

		/// <summary>"q1", "q2"…; the resource key is "{lessonId}.{Key}" and options "{lessonId}.{Key}.{a|b|c}".</summary>
		public string Key { get; }

		public LmsQuestionKinds Kind { get; }

		/// <summary>The correct option key ("a", "b", "c"), or "t" / "f" for true/false.</summary>
		public string Correct { get; }

		/// <summary>The option keys in order: a, b, c for choice questions, t and f for true/false.</summary>
		public IReadOnlyList<string> OptionKeys => Kind == LmsQuestionKinds.TrueFalse ? new[] { "t", "f" } : new[] { "a", "b", "c" };
	}

	/// <summary>One Academy lesson as a system course (A1): the A0 catalog entry plus its check, entitlement and paths.</summary>
	public sealed class AcademyCourseDefinition
	{
		public AcademyCourseDefinition(AcademyLesson lesson, string visibilityKeys, params AcademyQuestionDefinition[] questions)
		{
			Lesson = lesson;
			VisibilityKeys = visibilityKeys ?? string.Empty;
			Questions = questions;
		}

		public AcademyLesson Lesson { get; }

		public string LessonId => Lesson.Id;

		/// <summary>"academy:{lessonId}": the system course's and its bank's SystemKey.</summary>
		public string SystemKey => AcademyCourseManifest.SystemKeyFor(Lesson.Id);

		/// <summary>Comma separated "module:X", "flag:X", "addon:X" keys; the lesson is hidden without every one of them.</summary>
		public string VisibilityKeys { get; }

		public IReadOnlyList<AcademyQuestionDefinition> Questions { get; }

		/// <summary>0.1 to 0.25 hours: the video's length rounded up to the next 0.05 h, within those bounds (§15 A1).</summary>
		public decimal Hours => AcademyCourseManifest.HoursFor(Lesson.Minutes);

		/// <summary>The lesson manifest the definition was read from (A2): texts per locale, media, history.</summary>
		public AcademyLessonManifest Manifest { get; init; }

		/// <summary>The manifest's revision, stored as the system course's ManifestVersion (1 for every lesson B seeded in code).</summary>
		public int Revision => Manifest?.Revision ?? AcademyCourseManifest.ManifestVersion;
	}

	/// <summary>A role path (A1, R-S2): an ordered list of Academy lessons assigned together; each lesson is tracked on its own.</summary>
	public sealed class AcademyRolePath
	{
		public AcademyRolePath(string key, params string[] lessonIds)
		{
			Key = key;
			LessonIds = lessonIds;
		}

		/// <summary>Stable key ("member", "dispatcher"…); names and audiences are localized ("Path.{key}", "Path.{key}.Audience").</summary>
		public string Key { get; }

		public IReadOnlyList<string> LessonIds { get; }
	}

	/// <summary>
	/// The Resgrid Academy as a system-published course set (A1, plan §15): one course per Academy lesson and walkthrough, each with
	/// the Academy video and caption tracks (academy.resgrid.com media or a bundled media pack, never uploaded), a three-question check in
	/// 10 locales and 0.1–0.25 hours in a "Systems training" category with no CE. Since A2 the lessons are versioned manifest files
	/// (<see cref="AcademyManifests"/>); ILmsAcademyService.EnsureSeededAsync (first use and worker 88) seeds them idempotently through
	/// each course's unique SystemKey, and a lesson whose revision rises gets a new minor version (completions stand, R-L12) or, for a
	/// major change, a new major version whose notify / re-assign policy worker 88 applies. The role paths are plain ordered lists until
	/// T3 makes them learning paths.
	/// </summary>
	public static class AcademyCourseManifest
	{
		/// <summary>
		/// The lesson manifest schema the seed reads, and the revision B's code seed stamped on every course. Each course now stores its own
		/// lesson's revision (<see cref="AcademyCourseDefinition.Revision"/>); bump a lesson's revision in its manifest, never this.
		/// </summary>
		public const int ManifestVersion = 1;

		/// <summary>The transcript category every Academy completion is credited to (created per department on first completion).</summary>
		public const string CategoryCode = "RG-ACADEMY";

		public const string SystemKeyPrefix = "academy:";

		public static string SystemKeyFor(string lessonId) => SystemKeyPrefix + lessonId;

		public static string LessonIdFromSystemKey(string systemKey) =>
			systemKey != null && systemKey.StartsWith(SystemKeyPrefix, StringComparison.OrdinalIgnoreCase) ? systemKey.Substring(SystemKeyPrefix.Length) : null;

		public static decimal HoursFor(double minutes)
		{
			var hours = (decimal)Math.Ceiling(minutes / 60.0 / 0.05) * 0.05m;
			return Math.Max(0.10m, Math.Min(0.25m, hours));
		}

		/// <summary>Every Academy lesson and walkthrough, in catalog order, read from the lesson manifests (A2).</summary>
		public static readonly IReadOnlyList<AcademyCourseDefinition> Courses = AcademyManifests.Lessons.Select(FromManifest).ToList();

		/// <summary>A lesson manifest as a system course definition: its catalog entry, visibility keys and check.</summary>
		public static AcademyCourseDefinition FromManifest(AcademyLessonManifest manifest)
		{
			var lesson = AcademyCatalog.Find(manifest.Id) ?? new AcademyLesson(manifest.Id, manifest.Topic, manifest.Minutes, manifest.Walkthrough);
			var questions = (manifest.Check ?? new List<AcademyManifestQuestion>()).Select(q => new AcademyQuestionDefinition(q.Key,
				q.IsTrueFalse ? LmsQuestionKinds.TrueFalse : LmsQuestionKinds.SingleChoice, q.Correct)).ToArray();
			return new AcademyCourseDefinition(lesson, manifest.VisibilityKeys, questions) { Manifest = manifest };
		}

		/// <summary>The eight role paths of §15 A1, in the order the Academy page lists them.</summary>
		public static readonly IReadOnlyList<AcademyRolePath> RolePaths = new List<AcademyRolePath>
		{
			new AcademyRolePath("member", "00-start", "04-apps", "06-communication", "08-shifts", "09-training", "10-knowledge", "11-checklists", "12-chat"),
			new AcademyRolePath("dispatcher", "00-start", "03-units", "05-calls", "07-mapping", "06-communication", "03-custom-statuses", "06-dispatch-settings", "22-relay"),
			new AcademyRolePath("officer", "00-start", "05-calls", "06-communication", "07-mapping", "10-knowledge", "15-deployments", "21-ai", "13-assistant"),
			new AcademyRolePath("administrator", "00-start", "01-people", "02-security", "03-units", "01-setup-wizard", "02-department-settings", "03-custom-statuses",
				"17-automation", "19-adp", "20-plans", "14-admin-assist", "23-self-hosting"),
			new AcademyRolePath("training-officer", "00-start", "09-training", "10-knowledge", "11-checklists", "01-people"),
			new AcademyRolePath("records", "00-start", "14-records", "05-calls", "19-adp"),
			new AcademyRolePath("logistics", "00-start", "11-checklists", "12-maintenance", "13-inventory", "03-units"),
			new AcademyRolePath("business", "00-start", "16-business", "15-deployments", "20-plans"),
		};

		public static AcademyCourseDefinition Find(string lessonId) =>
			string.IsNullOrWhiteSpace(lessonId) ? null : Courses.FirstOrDefault(c => string.Equals(c.LessonId, lessonId.Trim(), StringComparison.OrdinalIgnoreCase));

		public static AcademyRolePath FindPath(string key) =>
			string.IsNullOrWhiteSpace(key) ? null : RolePaths.FirstOrDefault(p => string.Equals(p.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

		/// <summary>The lesson's video against the media base: its manifest path, else B's layout ({academy}/media/{id}.mp4).</summary>
		public static string VideoUrlFor(string mediaBase, AcademyCourseDefinition definition) =>
			definition?.Manifest?.Media?.Video != null ? AcademyManifests.MediaUrl(mediaBase, definition.Manifest.Media.Video) : VideoUrl(mediaBase, definition?.Lesson);

		/// <summary>The lesson's caption tracks per locale against the media base (the English track at least).</summary>
		public static List<LmsCaptionTrack> CaptionTracksFor(string mediaBase, AcademyCourseDefinition definition)
		{
			var tracks = (definition?.Manifest?.Media?.Captions ?? new List<AcademyManifestCaption>())
				.Select(c => new LmsCaptionTrack { Locale = c.Locale, Label = c.Label, Url = AcademyManifests.MediaUrl(mediaBase, c.Url) })
				.Where(t => !string.IsNullOrWhiteSpace(t.Url)).ToList();
			if (tracks.Count == 0 && definition?.Lesson != null)
				tracks.Add(new LmsCaptionTrack { Locale = "en", Label = "English", Url = CaptionsUrl(mediaBase, definition.Lesson) });
			return tracks;
		}

		/// <summary>The Academy video of a lesson: {academy}/media/{id}.mp4, or {academy}/deep-dive/media/{id}.mp4 for a walkthrough.</summary>
		public static string VideoUrl(string academyUrl, AcademyLesson lesson)
		{
			var root = AcademyCatalog.HomeUrl(academyUrl);
			if (root == null || lesson == null)
				return null;
			var id = Uri.EscapeDataString(lesson.Id);
			return lesson.IsWalkthrough ? root + "deep-dive/media/" + id + ".mp4" : root + "media/" + id + ".mp4";
		}

		/// <summary>The English caption track: {academy}/media/{id}.vtt, or the walkthrough page's captions.vtt.</summary>
		public static string CaptionsUrl(string academyUrl, AcademyLesson lesson)
		{
			var root = AcademyCatalog.HomeUrl(academyUrl);
			if (root == null || lesson == null)
				return null;
			var id = Uri.EscapeDataString(lesson.Id);
			return lesson.IsWalkthrough ? root + "deep-dive/lessons/" + id + "/captions.vtt" : root + "media/" + id + ".vtt";
		}

		/// <summary>The paths a lesson belongs to.</summary>
		public static IEnumerable<AcademyRolePath> PathsFor(string lessonId) =>
			RolePaths.Where(p => p.LessonIds.Contains(lessonId, StringComparer.OrdinalIgnoreCase));
	}

	/// <summary>What a department has, for Academy visibility (R-S2 "visibility follows entitlement").</summary>
	public sealed class AcademyEntitlements
	{
		/// <summary>Module names that are on ("Records", "Shifts"… the DepartmentModuleSettings switch without "Disabled").</summary>
		public HashSet<string> Modules { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public HashSet<string> Flags { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Active add-ons by <see cref="PlanAddonTypes"/> name.</summary>
		public HashSet<string> Addons { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Whether every key in <paramref name="visibilityKeys"/> is held. An unknown key kind hides the lesson (fail closed).</summary>
		public bool Allows(string visibilityKeys)
		{
			foreach (var raw in (visibilityKeys ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
			{
				var key = raw.Trim();
				var colon = key.IndexOf(':');
				if (colon <= 0)
					return false;
				var kind = key.Substring(0, colon);
				var value = key.Substring(colon + 1);
				var held = kind.Equals("module", StringComparison.OrdinalIgnoreCase) ? Modules.Contains(value)
					: kind.Equals("flag", StringComparison.OrdinalIgnoreCase) ? Flags.Contains(value)
					: kind.Equals("addon", StringComparison.OrdinalIgnoreCase) && Addons.Contains(value);
				if (!held)
					return false;
			}
			return true;
		}

		/// <summary>The flag and add-on names the keys mention (what a caller needs to evaluate).</summary>
		public static (HashSet<string> Flags, HashSet<string> Addons) Mentioned(IEnumerable<string> visibilityKeys)
		{
			var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var addons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var key in (visibilityKeys ?? Enumerable.Empty<string>()).SelectMany(k => (k ?? string.Empty).Split(',')).Select(k => k.Trim()))
			{
				if (key.StartsWith("flag:", StringComparison.OrdinalIgnoreCase))
					flags.Add(key.Substring(5));
				else if (key.StartsWith("addon:", StringComparison.OrdinalIgnoreCase))
					addons.Add(key.Substring(6));
			}
			return (flags, addons);
		}
	}
}
