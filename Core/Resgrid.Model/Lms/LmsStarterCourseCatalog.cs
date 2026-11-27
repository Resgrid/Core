using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model.Lms
{
	/// <summary>One lesson of a starter course: Resgrid's own plain-language text (html, sanitized on clone).</summary>
	public sealed class LmsStarterLesson
	{
		[JsonProperty("title")]
		public string Title { get; set; }

		[JsonProperty("html")]
		public string Html { get; set; }
	}

	public sealed class LmsStarterModule
	{
		[JsonProperty("title")]
		public string Title { get; set; }

		[JsonProperty("lessons")]
		public List<LmsStarterLesson> Lessons { get; set; } = new List<LmsStarterLesson>();
	}

	/// <summary>One question of a starter course's closing check.</summary>
	public sealed class LmsStarterQuestion
	{
		[JsonProperty("text")]
		public string Text { get; set; }

		/// <summary>"single" or "truefalse".</summary>
		[JsonProperty("kind")]
		public string Kind { get; set; }

		[JsonProperty("options")]
		public List<string> Options { get; set; } = new List<string>();

		/// <summary>The index of the correct option.</summary>
		[JsonProperty("correct")]
		public int Correct { get; set; }

		[JsonProperty("explanation")]
		public string Explanation { get; set; }

		[JsonIgnore]
		public bool IsTrueFalse => string.Equals(Kind, "truefalse", StringComparison.OrdinalIgnoreCase);
	}

	public sealed class LmsStarterCheck
	{
		[JsonProperty("module")]
		public string Module { get; set; }

		[JsonProperty("title")]
		public string Title { get; set; }

		[JsonProperty("passMark")]
		public int PassMark { get; set; } = 80;

		[JsonProperty("questions")]
		public List<LmsStarterQuestion> Questions { get; set; } = new List<LmsStarterQuestion>();
	}

	/// <summary>
	/// A Resgrid starter course (T5 item 4, §13a.4 point 2 "Resgrid-authored starters (free)"): structure and original Resgrid text only, no
	/// standard's text and no accreditation claim. Cloned into a department as a draft course the department edits, submits and publishes
	/// through its normal approval; the clone keeps the attribution (source, licence, revision). A partner entry (<see cref="IsPartnerSlot"/>)
	/// documents where third-party, CE-bearing content plugs in (LTI 1.3 or SCORM/cmi5, built with the standards work) and has no content.
	/// </summary>
	public sealed class LmsStarterCourseManifest
	{
		/// <summary>Stable key, e.g. "resgrid-onboarding"; the catalog card's title and summary are localized by it.</summary>
		[JsonProperty("key")]
		public string Key { get; set; }

		/// <summary>Raised whenever the starter's content changes; recorded on each clone as the source version.</summary>
		[JsonProperty("revision")]
		public int Revision { get; set; }

		/// <summary>"course" (cloneable) or "partner" (a documented LTI/SCORM slot, nothing to clone).</summary>
		[JsonProperty("kind")]
		public string Kind { get; set; } = "course";

		/// <summary>The English title applied to the department's course (the catalog shows the localized one by key).</summary>
		[JsonProperty("title")]
		public string Title { get; set; }

		[JsonProperty("code")]
		public string Code { get; set; }

		[JsonProperty("summary")]
		public string Summary { get; set; }

		[JsonProperty("tags")]
		public string Tags { get; set; }

		[JsonProperty("hours")]
		public decimal Hours { get; set; }

		/// <summary>The licence key ("resgrid-starter": free for any Resgrid department to use and adapt; no accreditation).</summary>
		[JsonProperty("license")]
		public string License { get; set; }

		/// <summary>Partner slots: "lti13", "scorm" or "cmi5", the standard the partner's content will launch over.</summary>
		[JsonProperty("launch")]
		public string Launch { get; set; }

		[JsonProperty("overview")]
		public string OverviewHtml { get; set; }

		[JsonProperty("modules")]
		public List<LmsStarterModule> Modules { get; set; } = new List<LmsStarterModule>();

		[JsonProperty("check")]
		public LmsStarterCheck Check { get; set; }

		[JsonIgnore]
		public bool IsPartnerSlot => string.Equals(Kind, "partner", StringComparison.OrdinalIgnoreCase);

		[JsonIgnore]
		public string SourceKey => LmsStarterCourseCatalog.SourceKeyPrefix + Key;

		[JsonIgnore]
		public int LessonCount => (Modules ?? new List<LmsStarterModule>()).Sum(m => m.Lessons?.Count ?? 0) + (Check?.Questions?.Count > 0 ? 1 : 0);
	}

	/// <summary>
	/// The Resgrid starter course library, read from the embedded manifests in Lms/Starters (one JSON file per entry). Every entry is checked
	/// when it loads (<see cref="Problems"/>); an invalid entry is left out of <see cref="All"/>, so a broken file never reaches a department.
	/// </summary>
	public static class LmsStarterCourseCatalog
	{
		public const string ResourcePrefix = "Resgrid.Model.Lms.Starters.";
		public const string SourceKeyPrefix = "starter:";
		public const string ResgridLicense = "resgrid-starter";
		public const string SourceName = "Resgrid";

		private static readonly Lazy<(IReadOnlyList<LmsStarterCourseManifest> Entries, IReadOnlyList<string> Problems)> Loaded =
			new Lazy<(IReadOnlyList<LmsStarterCourseManifest>, IReadOnlyList<string>)>(Load);

		/// <summary>Every valid entry, courses first, in key order.</summary>
		public static IReadOnlyList<LmsStarterCourseManifest> All => Loaded.Value.Entries;

		/// <summary>The cloneable starter courses.</summary>
		public static IEnumerable<LmsStarterCourseManifest> Courses => All.Where(e => !e.IsPartnerSlot);

		/// <summary>The documented partner content slots (LTI 1.3 / SCORM / cmi5).</summary>
		public static IEnumerable<LmsStarterCourseManifest> PartnerSlots => All.Where(e => e.IsPartnerSlot);

		/// <summary>Why an embedded file was left out ("file: problem"); empty when every file is valid.</summary>
		public static IReadOnlyList<string> Problems => Loaded.Value.Problems;

		public static LmsStarterCourseManifest Find(string key) =>
			string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(e => string.Equals(e.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

		/// <summary>
		/// The text fields of a starter (round 9C): titles, summary, overview, lesson html, check questions, options and explanations. Keys,
		/// codes, tags, kinds and the licence stay as written.
		/// </summary>
		public static readonly ISet<string> TextProperties = SeedContentLocalization.Properties("title", "summary", "overview", "html", "text", "options", "explanation", "module");

		/// <summary>A starter in a language (a translated copy; the starter itself in English or when the language ships no variant).</summary>
		public static LmsStarterCourseManifest Localized(LmsStarterCourseManifest manifest, string culture) =>
			SeedContentLocalization.Localize(manifest, SeedContentLocalization.StarterCourses, culture, TextProperties, manifest?.Key + "|" + manifest?.Revision);

		/// <summary>Every entry in a language, in catalog order.</summary>
		public static IReadOnlyList<LmsStarterCourseManifest> AllFor(string culture) => All.Select(e => Localized(e, culture)).ToList();

		/// <summary>Every English text of a starter (what the translations cover).</summary>
		public static IReadOnlyList<string> TextOf(LmsStarterCourseManifest manifest) => SeedContentLocalization.CollectObject(manifest, TextProperties);

		/// <summary>The problems of one manifest (empty when it is valid).</summary>
		public static List<string> Validate(LmsStarterCourseManifest manifest)
		{
			var problems = new List<string>();
			if (manifest == null)
			{
				problems.Add("empty");
				return problems;
			}
			if (string.IsNullOrWhiteSpace(manifest.Key) || manifest.Key.Any(c => !(char.IsLetterOrDigit(c) && c < 128) && c != '-') || manifest.Key.Length > 80)
				problems.Add("key");
			if (manifest.Revision < 1)
				problems.Add("revision");
			if (string.IsNullOrWhiteSpace(manifest.Title) || manifest.Title.Length > LmsCourse.TitleMaxLength)
				problems.Add("title");
			if (manifest.IsPartnerSlot)
			{
				if (!new[] { "lti13", "scorm", "cmi5" }.Contains(manifest.Launch ?? string.Empty, StringComparer.OrdinalIgnoreCase))
					problems.Add("launch");
				if ((manifest.Modules?.Count ?? 0) > 0 || manifest.Check != null)
					problems.Add("partner-content");
				return problems;
			}

			if (!string.Equals(manifest.Kind, "course", StringComparison.OrdinalIgnoreCase))
				problems.Add("kind");
			if (!string.Equals(manifest.License, ResgridLicense, StringComparison.Ordinal))
				problems.Add("license");
			if (manifest.Hours <= 0 || manifest.Hours > 40)
				problems.Add("hours");
			if (string.IsNullOrWhiteSpace(manifest.Code) || manifest.Code.Length > 50)
				problems.Add("code");
			var modules = manifest.Modules ?? new List<LmsStarterModule>();
			if (modules.Count == 0)
				problems.Add("modules");
			foreach (var module in modules)
			{
				if (string.IsNullOrWhiteSpace(module?.Title))
					problems.Add("module-title");
				if (module?.Lessons == null || module.Lessons.Count == 0)
					problems.Add("module-empty:" + module?.Title);
				foreach (var lesson in module?.Lessons ?? new List<LmsStarterLesson>())
				{
					if (string.IsNullOrWhiteSpace(lesson?.Title) || lesson.Title.Length > LmsLesson.TitleMaxLength)
						problems.Add("lesson-title:" + module?.Title);
					if (string.IsNullOrWhiteSpace(lesson?.Html) || lesson.Html.IndexOf("<script", StringComparison.OrdinalIgnoreCase) >= 0)
						problems.Add("lesson-html:" + lesson?.Title);
				}
			}

			if (manifest.Check != null)
			{
				if (string.IsNullOrWhiteSpace(manifest.Check.Module) || string.IsNullOrWhiteSpace(manifest.Check.Title))
					problems.Add("check-title");
				if (manifest.Check.PassMark < 1 || manifest.Check.PassMark > 100)
					problems.Add("check-pass-mark");
				var questions = manifest.Check.Questions ?? new List<LmsStarterQuestion>();
				if (questions.Count == 0)
					problems.Add("check-empty");
				foreach (var question in questions)
				{
					var count = question?.Options?.Count ?? 0;
					var valid = question != null && !string.IsNullOrWhiteSpace(question.Text) && question.Correct >= 0 && question.Correct < count &&
						question.Options.All(o => !string.IsNullOrWhiteSpace(o)) && question.Options.Distinct(StringComparer.OrdinalIgnoreCase).Count() == count &&
						(question.IsTrueFalse ? count == 2 : string.Equals(question.Kind, "single", StringComparison.OrdinalIgnoreCase) && count >= 2 && count <= 6);
					if (!valid)
						problems.Add("question:" + question?.Text);
				}
			}

			return problems;
		}

		/// <summary>Reads every embedded manifest; one that does not parse or validate is reported and left out.</summary>
		private static (IReadOnlyList<LmsStarterCourseManifest>, IReadOnlyList<string>) Load()
		{
			var entries = new List<LmsStarterCourseManifest>();
			var problems = new List<string>();
			var assembly = typeof(LmsStarterCourseCatalog).Assembly;
			foreach (var name in assembly.GetManifestResourceNames()
				.Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
				.OrderBy(n => n, StringComparer.Ordinal))
			{
				LmsStarterCourseManifest manifest;
				try
				{
					using var stream = assembly.GetManifestResourceStream(name);
					using var reader = new StreamReader(stream);
					manifest = JsonConvert.DeserializeObject<LmsStarterCourseManifest>(reader.ReadToEnd());
				}
				catch (JsonException ex)
				{
					problems.Add(name + ": " + ex.Message);
					continue;
				}

				var found = Validate(manifest);
				if (entries.Any(e => string.Equals(e.Key, manifest?.Key, StringComparison.OrdinalIgnoreCase)))
					found.Add("duplicate-key");
				if (found.Count > 0)
				{
					problems.AddRange(found.Select(p => name + ": " + p));
					continue;
				}
				entries.Add(manifest);
			}

			return (entries.OrderBy(e => e.IsPartnerSlot).ThenBy(e => e.Key, StringComparer.Ordinal).ToList(), problems);
		}
	}
}
