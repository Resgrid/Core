using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Resgrid.Model.Academy
{
	/// <summary>One revision of a lesson manifest: what changed and, for a major change, what departments that assigned it are asked to do.</summary>
	public sealed class AcademyManifestRevision
	{
		public const string Initial = "initial";
		public const string Minor = "minor";
		public const string Major = "major";
		public const string PolicyNotify = "notify";
		public const string PolicyReassign = "reassign";

		public int Revision { get; set; }

		/// <summary>The release the revision shipped with ("2026.12"): the per-release docs/Academy pass (decision 13).</summary>
		public string Release { get; set; }

		/// <summary><see cref="Initial"/>, <see cref="Minor"/> (completions stand) or <see cref="Major"/> (completed learners are notified or re-assigned).</summary>
		public string Change { get; set; }

		public string Note { get; set; }

		/// <summary>A major change only: <see cref="PolicyNotify"/> (the default) or <see cref="PolicyReassign"/>.</summary>
		public string Policy { get; set; }

		/// <summary>A major change with the re-assign policy: days until the new assignment is due (1–730, default 30).</summary>
		public int? ReassignDueDays { get; set; }

		[JsonIgnore]
		public bool IsMajor => string.Equals(Change, Major, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>The Admin Assist modules (areas) and capabilities a lesson teaches: the coverage guard's input (§15 A2).</summary>
	public sealed class AcademyAdminAssistLinks
	{
		public List<string> Areas { get; set; } = new List<string>();
		public List<string> Capabilities { get; set; } = new List<string>();
	}

	public sealed class AcademyManifestCaption
	{
		public string Locale { get; set; }
		public string Label { get; set; }

		/// <summary>Relative to the media base (<see cref="AcademyManifests.MediaBase"/>), or absolute.</summary>
		public string Url { get; set; }
	}

	/// <summary>The lesson's media: the video and its caption tracks per locale, relative to the configurable media base (an air-gapped media pack).</summary>
	public sealed class AcademyManifestMedia
	{
		public string Video { get; set; }
		public List<AcademyManifestCaption> Captions { get; set; } = new List<AcademyManifestCaption>();
	}

	public sealed class AcademyManifestOption
	{
		public string Key { get; set; }
		public Dictionary<string, string> Text { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>One question of a lesson check, with its text and options per locale.</summary>
	public sealed class AcademyManifestQuestion
	{
		public const string Single = "single";
		public const string TrueFalse = "truefalse";

		public string Key { get; set; }

		/// <summary><see cref="Single"/> or <see cref="TrueFalse"/>.</summary>
		public string Kind { get; set; }

		/// <summary>The correct option key ("a", "b", "c"), or "t" / "f" for true/false.</summary>
		public string Correct { get; set; }

		public Dictionary<string, string> Text { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Choice questions only; true/false options are the fixed True/False resources.</summary>
		public List<AcademyManifestOption> Options { get; set; } = new List<AcademyManifestOption>();

		[JsonIgnore]
		public bool IsTrueFalse => string.Equals(Kind, TrueFalse, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// One Academy lesson or walkthrough as a versioned manifest file (A2, plan §15): id, revision and version, titles per locale, audience,
	/// module/feature keys, Admin Assist links, media and caption tracks, and the check per locale. The file is the source of truth; the A0
	/// catalog, the A1 system course seed and the knowledge corpus read it.
	/// </summary>
	public sealed class AcademyLessonManifest
	{
		public int Schema { get; set; }
		public string Id { get; set; }

		/// <summary>Monotonic; stored as the system course's ManifestVersion. A rise re-seeds the course as a new minor or major version.</summary>
		public int Revision { get; set; }

		/// <summary>The lesson's own label ("1.0"), for people; <see cref="Revision"/> drives the seed.</summary>
		public string Version { get; set; }

		public List<AcademyManifestRevision> History { get; set; } = new List<AcademyManifestRevision>();
		public string Topic { get; set; }
		public double Minutes { get; set; }
		public bool Walkthrough { get; set; }

		/// <summary>The role audiences the lesson is written for (informational; role paths are their own definitions).</summary>
		public List<string> Audience { get; set; } = new List<string>();

		/// <summary>"module:X", "flag:X", "addon:X": the lesson is hidden without every one of them (visibility follows entitlement).</summary>
		public List<string> Visibility { get; set; } = new List<string>();

		/// <summary>User-area controllers whose Help menu "Learn" link opens this lesson (A0).</summary>
		public List<string> Pages { get; set; } = new List<string>();

		public AcademyAdminAssistLinks AdminAssist { get; set; } = new AcademyAdminAssistLinks();
		public AcademyManifestMedia Media { get; set; } = new AcademyManifestMedia();
		public Dictionary<string, string> Title { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public List<AcademyManifestQuestion> Check { get; set; } = new List<AcademyManifestQuestion>();

		[JsonIgnore]
		public string VisibilityKeys => string.Join(",", (Visibility ?? new List<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()));

		/// <summary>Whether any revision after <paramref name="storedRevision"/> (up to the current one) is a major change.</summary>
		public bool HasMajorChangeSince(int storedRevision) => (History ?? new List<AcademyManifestRevision>()).Any(h => h.Revision > storedRevision && h.Revision <= Revision && h.IsMajor);

		/// <summary>The latest major revision after <paramref name="storedRevision"/>, whose policy applies.</summary>
		public AcademyManifestRevision LatestMajorSince(int storedRevision) =>
			(History ?? new List<AcademyManifestRevision>()).Where(h => h.Revision > storedRevision && h.Revision <= Revision && h.IsMajor).OrderByDescending(h => h.Revision).FirstOrDefault();

		[JsonIgnore]
		public AcademyManifestRevision Current => (History ?? new List<AcademyManifestRevision>()).FirstOrDefault(h => h.Revision == Revision);
	}

	/// <summary>A user-area page a guidance item links to.</summary>
	public sealed class AcademyManifestPage
	{
		public string Controller { get; set; }
		public string Action { get; set; }

		[JsonIgnore]
		public string Url => string.IsNullOrWhiteSpace(Controller) ? null
			: "/User/" + Uri.EscapeDataString(Controller) + "/" + Uri.EscapeDataString(string.IsNullOrWhiteSpace(Action) ? "Index" : Action);
	}

	/// <summary>One item of a release's "What's new" micro-lesson.</summary>
	public sealed class AcademyWhatsNewItem
	{
		public string Key { get; set; }
		public Dictionary<string, string> Title { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, string> Body { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>The Academy lesson that teaches it (optional).</summary>
		public string LessonId { get; set; }

		public AcademyManifestPage Page { get; set; }

		/// <summary>Same keys as a lesson's visibility: the item is hidden from departments without them.</summary>
		public List<string> Visibility { get; set; } = new List<string>();
	}

	/// <summary>A release's "What's new" micro-lesson (A3), shown once per member until dismissed.</summary>
	public sealed class AcademyWhatsNewManifest
	{
		public int Schema { get; set; }
		public string Release { get; set; }
		public DateTime PublishedOn { get; set; }
		public double Minutes { get; set; }
		public Dictionary<string, string> Title { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, string> Summary { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public List<AcademyWhatsNewItem> Items { get; set; } = new List<AcademyWhatsNewItem>();

		/// <summary>The member's remembered state key ("whatsnew:2026.12").</summary>
		[JsonIgnore]
		public string GuidanceKey => AcademyManifests.WhatsNewKeyPrefix + Release;
	}

	/// <summary>One first-run task of a guided tour, built on a walkthrough or lesson.</summary>
	public sealed class AcademyTourTask
	{
		public string Key { get; set; }
		public string LessonId { get; set; }
		public AcademyManifestPage Page { get; set; }
		public Dictionary<string, string> Title { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, string> Hint { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public List<string> Visibility { get; set; } = new List<string>();
	}

	/// <summary>A guided tour for one role (A3): first-run tasks, dismissible, remembered per member.</summary>
	public sealed class AcademyTourManifest
	{
		public const string Administrator = "administrator";
		public const string Dispatcher = "dispatcher";
		public const string TrainingOfficer = "training-officer";
		public const string Member = "member";

		public int Schema { get; set; }
		public string Key { get; set; }

		/// <summary>A rise shows the tour again to members who finished an earlier revision (new tasks).</summary>
		public int Revision { get; set; }

		public Dictionary<string, string> Title { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, string> Intro { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public List<AcademyTourTask> Tasks { get; set; } = new List<AcademyTourTask>();

		[JsonIgnore]
		public string GuidanceKey => AcademyManifests.TourKeyPrefix + Key;
	}

	public sealed class AcademyCoverageExemption
	{
		public string Id { get; set; }
		public string Reason { get; set; }
	}

	/// <summary>Admin Assist modules and capabilities that deliberately have no lesson (the coverage guard's explicit exemptions).</summary>
	public sealed class AcademyCoverageExemptions
	{
		public List<AcademyCoverageExemption> Areas { get; set; } = new List<AcademyCoverageExemption>();
		public List<AcademyCoverageExemption> Capabilities { get; set; } = new List<AcademyCoverageExemption>();
	}

	/// <summary>Every manifest of a release, loaded together and validated.</summary>
	public sealed class AcademyManifestSet
	{
		public List<AcademyLessonManifest> Lessons { get; set; } = new List<AcademyLessonManifest>();
		public List<AcademyWhatsNewManifest> WhatsNew { get; set; } = new List<AcademyWhatsNewManifest>();
		public List<AcademyTourManifest> Tours { get; set; } = new List<AcademyTourManifest>();
		public AcademyCoverageExemptions Exemptions { get; set; } = new AcademyCoverageExemptions();
	}

	/// <summary>
	/// The Academy manifests as code (A2, protocols-training-knowledge-plan §15): JSON files under <c>Academy/Manifests</c> in this project,
	/// embedded so they ship with every release and self-hosted install (Lessons/*.json, WhatsNew/*.json, Tours/*.json,
	/// coverage-exemptions.json). Loaded once and validated; a malformed manifest fails loudly at first use, never silently. The media base
	/// is configurable (<see cref="Config.SystemBehaviorConfig.AcademyMediaUrl"/>) so an air-gapped install plays a bundled media pack.
	/// </summary>
	public static class AcademyManifests
	{
		public const string ResourcePrefix = "Resgrid.Model.Academy.Manifests.";
		public const string TourKeyPrefix = "tour:";
		public const string WhatsNewKeyPrefix = "whatsnew:";
		public const string DefaultLocale = "en";

		/// <summary>The 10 product locales every title, question and option carries.</summary>
		public static readonly IReadOnlyList<string> Locales = new[] { "en", "ar", "de", "el", "es", "fr", "it", "pl", "sv", "uk" };

		private static readonly Lazy<AcademyManifestSet> Embedded = new Lazy<AcademyManifestSet>(LoadEmbedded);

		public static AcademyManifestSet Set => Embedded.Value;

		/// <summary>Lessons in catalog order: the numbered lessons, then the walkthroughs.</summary>
		public static IReadOnlyList<AcademyLessonManifest> Lessons => Set.Lessons;

		/// <summary>Newest release first.</summary>
		public static IReadOnlyList<AcademyWhatsNewManifest> WhatsNew => Set.WhatsNew;

		public static IReadOnlyList<AcademyTourManifest> Tours => Set.Tours;

		public static AcademyCoverageExemptions Exemptions => Set.Exemptions;

		public static AcademyLessonManifest FindLesson(string lessonId) =>
			string.IsNullOrWhiteSpace(lessonId) ? null : Lessons.FirstOrDefault(l => string.Equals(l.Id, lessonId.Trim(), StringComparison.OrdinalIgnoreCase));

		public static AcademyTourManifest FindTour(string key) =>
			string.IsNullOrWhiteSpace(key) ? null : Tours.FirstOrDefault(t => string.Equals(t.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

		public static AcademyWhatsNewManifest FindRelease(string release) =>
			string.IsNullOrWhiteSpace(release) ? null : WhatsNew.FirstOrDefault(w => string.Equals(w.Release, release.Trim(), StringComparison.OrdinalIgnoreCase));

		/// <summary>The text in <paramref name="locale"/> (or its two-letter language), else English, else any.</summary>
		public static string Text(IReadOnlyDictionary<string, string> map, string locale)
		{
			if (map == null || map.Count == 0)
				return null;
			if (!string.IsNullOrWhiteSpace(locale))
			{
				if (map.TryGetValue(locale, out var exact) && !string.IsNullOrWhiteSpace(exact))
					return exact;
				var language = locale.Split('-', '_')[0];
				if (map.TryGetValue(language, out var lang) && !string.IsNullOrWhiteSpace(lang))
					return lang;
			}
			if (map.TryGetValue(DefaultLocale, out var english) && !string.IsNullOrWhiteSpace(english))
				return english;
			return map.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
		}

		public static string Text(Dictionary<string, string> map, string locale) => Text((IReadOnlyDictionary<string, string>)map, locale);

		/// <summary>The media base: <c>AcademyMediaUrl</c> when set (a bundled media pack), else the Academy itself; null when both are blank.</summary>
		public static string MediaBase => AcademyCatalog.HomeUrl(string.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.AcademyMediaUrl)
			? Config.SystemBehaviorConfig.AcademyUrl : Config.SystemBehaviorConfig.AcademyMediaUrl);

		/// <summary>A manifest media path against <paramref name="mediaBase"/>; an absolute https URL is kept as written.</summary>
		public static string MediaUrl(string mediaBase, string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return null;
			if (Uri.TryCreate(path, UriKind.Absolute, out var absolute) && (absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeHttp))
				return path;
			var root = AcademyCatalog.HomeUrl(mediaBase);
			return root == null ? null : root + path.TrimStart('/');
		}

		/// <summary>
		/// A stable, non-zero number for the lesson set and the media base: the seed memo. It changes when any lesson's revision changes, a
		/// lesson is added or removed, or the media base moves (the seed then re-links stored media).
		/// </summary>
		public static int SeedFingerprint(string mediaBase)
		{
			var text = new StringBuilder(mediaBase ?? string.Empty);
			foreach (var lesson in Lessons)
				text.Append('|').Append(lesson.Id).Append(':').Append(lesson.Revision.ToString(CultureInfo.InvariantCulture));
			var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
			return (BitConverter.ToInt32(hash, 0) & 0x7FFFFFFF) | 1;
		}

		private static AcademyManifestSet LoadEmbedded()
		{
			var assembly = typeof(AcademyManifests).Assembly;
			var files = new List<(string Name, string Json)>();
			foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.Ordinal))
			{
				using var stream = assembly.GetManifestResourceStream(name);
				if (stream == null)
					continue;
				using var reader = new StreamReader(stream, Encoding.UTF8);
				files.Add((name.Substring(ResourcePrefix.Length), reader.ReadToEnd()));
			}

			var set = Parse(files);
			var errors = Validate(set);
			if (errors.Count > 0)
				throw new InvalidOperationException("The embedded Academy manifests are invalid: " + string.Join("; ", errors));
			return set;
		}

		/// <summary>
		/// Reads manifest files by their name under <c>Academy/Manifests</c> ("Lessons.00-start.json", "WhatsNew.2026.12.json",
		/// "Tours.member.json", "coverage-exemptions.json"). Lessons are ordered numbered lessons first, then walkthroughs, each by id.
		/// </summary>
		public static AcademyManifestSet Parse(IEnumerable<(string Name, string Json)> files)
		{
			var settings = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error, DateTimeZoneHandling = DateTimeZoneHandling.Utc };
			var set = new AcademyManifestSet();
			foreach (var (name, json) in files ?? Enumerable.Empty<(string, string)>())
			{
				var folder = (name ?? string.Empty).Split('.')[0];
				try
				{
					switch (folder.ToLowerInvariant())
					{
						case "lessons":
							set.Lessons.Add(JsonConvert.DeserializeObject<AcademyLessonManifest>(json, settings));
							break;
						case "whatsnew":
							set.WhatsNew.Add(JsonConvert.DeserializeObject<AcademyWhatsNewManifest>(json, settings));
							break;
						case "tours":
							set.Tours.Add(JsonConvert.DeserializeObject<AcademyTourManifest>(json, settings));
							break;
						case "coverage-exemptions":
							set.Exemptions = JsonConvert.DeserializeObject<AcademyCoverageExemptions>(json, settings) ?? new AcademyCoverageExemptions();
							break;
					}
				}
				catch (JsonException ex)
				{
					throw new InvalidOperationException("Academy manifest " + name + " is not valid JSON for its kind: " + ex.Message, ex);
				}
			}

			set.Lessons = set.Lessons.Where(l => l != null).OrderBy(l => l.Walkthrough).ThenBy(l => l.Id, StringComparer.Ordinal).ToList();
			set.WhatsNew = set.WhatsNew.Where(w => w != null).OrderByDescending(w => w.PublishedOn).ThenByDescending(w => w.Release, StringComparer.Ordinal).ToList();
			set.Tours = set.Tours.Where(t => t != null).ToList();
			return set;
		}

		/// <summary>
		/// The structural rules a release's manifests must meet (a broken one fails at load): unique ids, an English title, a current revision
		/// recorded in an ascending history, a check of at least three questions with a correct option that exists, captions with a locale,
		/// pages claimed by one lesson only, and tours and "What's new" items that name existing lessons. Translation completeness and Admin
		/// Assist coverage are test concerns (the coverage guard only warns).
		/// </summary>
		public static List<string> Validate(AcademyManifestSet set)
		{
			var errors = new List<string>();
			if (set == null)
			{
				errors.Add("no manifests");
				return errors;
			}

			var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var pages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var lesson in set.Lessons)
			{
				var id = lesson.Id ?? "(no id)";
				if (string.IsNullOrWhiteSpace(lesson.Id) || lesson.Id.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '-')))
					errors.Add(id + ": the id must be letters, digits and dashes");
				if (!ids.Add(id))
					errors.Add(id + ": duplicate lesson id");
				if (lesson.Schema != 1)
					errors.Add(id + ": unknown schema " + lesson.Schema);
				if (lesson.Revision < 1)
					errors.Add(id + ": the revision starts at 1");
				var history = lesson.History ?? new List<AcademyManifestRevision>();
				if (!history.Any(h => h.Revision == lesson.Revision))
					errors.Add(id + ": the history has no entry for revision " + lesson.Revision);
				if (history.Select(h => h.Revision).Zip(history.Select(h => h.Revision).Skip(1), (a, b) => b > a).Any(ascending => !ascending))
					errors.Add(id + ": the history must be in ascending revision order");
				foreach (var entry in history)
				{
					var change = entry.Change ?? string.Empty;
					if (!new[] { AcademyManifestRevision.Initial, AcademyManifestRevision.Minor, AcademyManifestRevision.Major }.Contains(change, StringComparer.OrdinalIgnoreCase))
						errors.Add(id + ": revision " + entry.Revision + " has an unknown change kind '" + change + "'");
					if (entry.IsMajor && !string.IsNullOrWhiteSpace(entry.Policy) &&
						!new[] { AcademyManifestRevision.PolicyNotify, AcademyManifestRevision.PolicyReassign }.Contains(entry.Policy, StringComparer.OrdinalIgnoreCase))
						errors.Add(id + ": revision " + entry.Revision + " has an unknown major-change policy '" + entry.Policy + "'");
					if (entry.ReassignDueDays.HasValue && (entry.ReassignDueDays < 1 || entry.ReassignDueDays > 730))
						errors.Add(id + ": revision " + entry.Revision + " re-assign days must be 1 to 730");
				}
				if (string.IsNullOrWhiteSpace(Text(lesson.Title, DefaultLocale)) || lesson.Title == null || !lesson.Title.ContainsKey(DefaultLocale))
					errors.Add(id + ": an English title is required");
				if (lesson.Minutes <= 0 || lesson.Minutes > 60)
					errors.Add(id + ": minutes must be above 0 and at most 60");
				if (string.IsNullOrWhiteSpace(lesson.Media?.Video))
					errors.Add(id + ": a video is required");
				if ((lesson.Media?.Captions ?? new List<AcademyManifestCaption>()).Any(c => string.IsNullOrWhiteSpace(c.Locale) || string.IsNullOrWhiteSpace(c.Url)))
					errors.Add(id + ": every caption track needs a locale and a url");
				foreach (var key in lesson.Visibility ?? new List<string>())
				{
					var colon = (key ?? string.Empty).IndexOf(':');
					var kind = colon > 0 ? key.Substring(0, colon) : string.Empty;
					if (!new[] { "module", "flag", "addon" }.Contains(kind, StringComparer.OrdinalIgnoreCase) || colon == key.Length - 1)
						errors.Add(id + ": unknown visibility key '" + key + "'");
				}
				foreach (var page in lesson.Pages ?? new List<string>())
				{
					if (pages.TryGetValue(page, out var other))
						errors.Add(id + ": page " + page + " is already claimed by " + other);
					else
						pages[page] = id;
				}

				var check = lesson.Check ?? new List<AcademyManifestQuestion>();
				if (check.Count < 3)
					errors.Add(id + ": the check needs at least three questions");
				if (check.Select(q => q.Key ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).Count() != check.Count)
					errors.Add(id + ": question keys must be unique");
				foreach (var question in check)
				{
					var qid = id + "." + question.Key;
					if (string.IsNullOrWhiteSpace(question.Key) || question.Key.Contains('.'))
						errors.Add(qid + ": a question key is required and has no dots");
					if (question.Text == null || !question.Text.ContainsKey(DefaultLocale) || string.IsNullOrWhiteSpace(question.Text[DefaultLocale]))
						errors.Add(qid + ": English question text is required");
					if (question.IsTrueFalse)
					{
						if (question.Correct != "t" && question.Correct != "f")
							errors.Add(qid + ": a true/false answer is t or f");
						if (question.Options != null && question.Options.Count > 0)
							errors.Add(qid + ": a true/false question has no options of its own");
					}
					else if (string.Equals(question.Kind, AcademyManifestQuestion.Single, StringComparison.OrdinalIgnoreCase))
					{
						var options = question.Options ?? new List<AcademyManifestOption>();
						if (options.Count < 2 || options.Select(o => o.Key ?? string.Empty).Distinct(StringComparer.Ordinal).Count() != options.Count)
							errors.Add(qid + ": a choice question needs two or more options with unique keys");
						if (!options.Any(o => string.Equals(o.Key, question.Correct, StringComparison.Ordinal)))
							errors.Add(qid + ": the correct option '" + question.Correct + "' is not an option");
						if (options.Any(o => o.Text == null || !o.Text.ContainsKey(DefaultLocale) || string.IsNullOrWhiteSpace(o.Text[DefaultLocale])))
							errors.Add(qid + ": every option needs English text");
					}
					else
					{
						errors.Add(qid + ": unknown question kind '" + question.Kind + "'");
					}
				}
			}

			var tourKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var tour in set.Tours)
			{
				var key = tour.Key ?? "(no key)";
				if (string.IsNullOrWhiteSpace(tour.Key) || !tourKeys.Add(tour.Key))
					errors.Add("tour " + key + ": a unique key is required");
				if (tour.Revision < 1)
					errors.Add("tour " + key + ": the revision starts at 1");
				if (string.IsNullOrWhiteSpace(Text(tour.Title, DefaultLocale)))
					errors.Add("tour " + key + ": an English title is required");
				var tasks = tour.Tasks ?? new List<AcademyTourTask>();
				if (tasks.Count == 0 || tasks.Select(t => t.Key ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).Count() != tasks.Count)
					errors.Add("tour " + key + ": tasks need unique keys");
				foreach (var task in tasks)
				{
					if (!string.IsNullOrWhiteSpace(task.LessonId) && !ids.Contains(task.LessonId))
						errors.Add("tour " + key + "." + task.Key + ": unknown lesson " + task.LessonId);
					if (string.IsNullOrWhiteSpace(task.Page?.Controller))
						errors.Add("tour " + key + "." + task.Key + ": a page is required");
					if (string.IsNullOrWhiteSpace(Text(task.Title, DefaultLocale)))
						errors.Add("tour " + key + "." + task.Key + ": an English title is required");
					if ((task.Key ?? string.Empty).Contains(','))
						errors.Add("tour " + key + "." + task.Key + ": a task key has no commas");
				}
			}

			var releases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var release in set.WhatsNew)
			{
				var key = release.Release ?? "(no release)";
				if (string.IsNullOrWhiteSpace(release.Release) || !releases.Add(release.Release))
					errors.Add("what's new " + key + ": a unique release is required");
				if (release.PublishedOn == default)
					errors.Add("what's new " + key + ": a published date is required");
				if (string.IsNullOrWhiteSpace(Text(release.Title, DefaultLocale)))
					errors.Add("what's new " + key + ": an English title is required");
				foreach (var item in release.Items ?? new List<AcademyWhatsNewItem>())
					if (!string.IsNullOrWhiteSpace(item.LessonId) && !ids.Contains(item.LessonId))
						errors.Add("what's new " + key + "." + item.Key + ": unknown lesson " + item.LessonId);
			}

			return errors;
		}
	}
}
