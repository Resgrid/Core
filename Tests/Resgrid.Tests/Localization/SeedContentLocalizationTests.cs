using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Certifications;
using Resgrid.Model.Lms;

namespace Resgrid.Tests.Localization
{
	/// <summary>
	/// Round 9C: Resgrid's seeded content (protocol template library, starter courses, lesson-plan, skill-sheet and task-book starters,
	/// training category templates, competency framework catalog, certification type templates) ships a machine-translated variant in every
	/// supported language, chosen by the department's or user's language with English as the fallback. These tests keep every English seed
	/// string translated in every language (with its placeholders, HTML tags and bracketed instructions intact), keep identifiers and
	/// structure identical across languages, and check the sector and region templates load.
	/// <para>
	/// To list what a language is missing after changing seed text, run with the environment variable RESGRID_SEED_EXPORT set to a folder:
	/// the test writes seed-source.json (every English string per catalog) there.
	/// </para>
	/// </summary>
	[TestFixture]
	public class SeedContentLocalizationTests
	{
		/// <summary>Every English text per catalog, as the translations must cover it.</summary>
		public static Dictionary<string, List<string>> SourceStrings()
		{
			static List<string> Distinct(IEnumerable<string> values) => values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).ToList();
			return new Dictionary<string, List<string>>(StringComparer.Ordinal)
			{
				[SeedContentLocalization.ProtocolTemplates] = Distinct(ProtocolTemplateLibrary.Sets.SelectMany(ProtocolTemplateLibrary.TextOf)),
				[SeedContentLocalization.StarterCourses] = Distinct(LmsStarterCourseCatalog.All.SelectMany(LmsStarterCourseCatalog.TextOf)),
				[SeedContentLocalization.LessonPlans] = Distinct(LmsLessonPlanStarterCatalog.All.SelectMany(LmsLessonPlanStarterCatalog.TextOf)),
				[SeedContentLocalization.SkillTemplates] = Distinct(LmsSkillTemplateCatalog.AllText()),
				[SeedContentLocalization.CategoryTemplates] = Distinct(TrainingCategoryTemplateCatalog.All.SelectMany(TrainingCategoryTemplateCatalog.TextOf)),
				[SeedContentLocalization.CompetencyFrameworks] = Distinct(LmsCompetencyFrameworkCatalog.All.SelectMany(LmsCompetencyFrameworkCatalog.TextOf)),
				[SeedContentLocalization.CertificationTemplates] = Distinct(CertificationTypeTemplateCatalog.All.SelectMany(CertificationTypeTemplateCatalog.TextOf))
			};
		}

		[Test]
		public void Export_the_English_seed_strings_when_asked()
		{
			var folder = Environment.GetEnvironmentVariable("RESGRID_SEED_EXPORT");
			var source = SourceStrings();
			source.Keys.Should().BeEquivalentTo(SeedContentLocalization.Catalogs);
			source.Values.Should().OnlyContain(list => list.Count > 0);
			if (string.IsNullOrWhiteSpace(folder))
				return;
			Directory.CreateDirectory(folder);
			System.IO.File.WriteAllText(Path.Combine(folder, "seed-source.json"), JsonConvert.SerializeObject(source, Formatting.Indented));
		}

		private static readonly Regex Placeholder = new Regex(@"\{\d+\}", RegexOptions.Compiled);
		private static readonly Regex Tag = new Regex(@"</?([a-z0-9]+)\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		private static IEnumerable<string> Tags(string text) => Tag.Matches(text ?? string.Empty).Select(m => (m.Value.StartsWith("</") ? "/" : string.Empty) + m.Groups[1].Value.ToLowerInvariant());

		[TestCaseSource(typeof(SeedContentLocalization), nameof(SeedContentLocalization.Languages))]
		public void Every_seed_string_is_translated_with_placeholders_and_tags_intact(string language)
		{
			var missing = new List<string>();
			var broken = new List<string>();
			foreach (var catalog in SourceStrings())
			{
				var map = SeedContentLocalization.MapFor(catalog.Key, language) ?? new Dictionary<string, string>();
				foreach (var english in catalog.Value)
				{
					if (!map.TryGetValue(english, out var translated) || string.IsNullOrWhiteSpace(translated))
					{
						missing.Add(catalog.Key + ": " + english);
						continue;
					}
					if (!Placeholder.Matches(english).Select(m => m.Value).OrderBy(v => v).SequenceEqual(Placeholder.Matches(translated).Select(m => m.Value).OrderBy(v => v)))
						broken.Add(catalog.Key + " placeholders: " + english);
					if (!Tags(english).SequenceEqual(Tags(translated)))
						broken.Add(catalog.Key + " tags: " + english);
					if (english.Count(c => c == '[') != translated.Count(c => c == '[') || english.Count(c => c == ']') != translated.Count(c => c == ']'))
						broken.Add(catalog.Key + " brackets: " + english);
				}
			}

			missing.Should().BeEmpty(language + " must translate every seed string (run with RESGRID_SEED_EXPORT to list the English source)");
			broken.Should().BeEmpty(language + " translations must keep {0} placeholders, HTML tags and [bracketed] instructions");
		}

		[TestCaseSource(typeof(SeedContentLocalization), nameof(SeedContentLocalization.Languages))]
		public void Translation_maps_hold_no_stale_entries(string language)
		{
			var source = SourceStrings();
			foreach (var catalog in SeedContentLocalization.Catalogs)
			{
				var map = SeedContentLocalization.MapFor(catalog, language);
				if (map == null)
					continue;
				var known = new HashSet<string>(source[catalog], StringComparer.Ordinal);
				map.Keys.Where(k => !known.Contains(k)).Should().BeEmpty(language + "/" + catalog + " holds translations of text no seed carries any more");
			}
		}

		[Test]
		public void A_language_picks_its_variant_and_anything_else_falls_back_to_English()
		{
			var english = ProtocolTemplateLibrary.Get("sar-starter");
			ProtocolTemplateLibrary.Get("sar-starter", "en-US").Should().BeSameAs(english);
			ProtocolTemplateLibrary.Get("sar-starter", "pt-BR").Should().BeSameAs(english, "Portuguese ships no variant");
			ProtocolTemplateLibrary.Get("sar-starter", null).Should().BeSameAs(english);

			var german = ProtocolTemplateLibrary.Get("sar-starter", "de-DE");
			german.Should().NotBeSameAs(english);
			german.Set.Title.Should().Be(SeedContentLocalization.Translate(SeedContentLocalization.ProtocolTemplates, english.Set.Title, "de"));
			german.Set.Title.Should().NotBe(english.Set.Title);
			english.Set.Title.Should().Be("Search and rescue starter set", "the shared English instance is never changed");

			SeedContentLocalization.LanguageFor("fr-CA").Should().Be("fr");
			SeedContentLocalization.LanguageFor("en").Should().BeNull();
			SeedContentLocalization.FirstLanguage(null, "", "sv-SE", "de").Should().Be("sv");
			SeedContentLocalization.FirstLanguage("en", "de").Should().BeNull("a reader who chose English is not handed the department's language");
			SeedContentLocalization.Translate(SeedContentLocalization.StarterCourses, "No such text", "de").Should().Be("No such text");
		}

		[TestCaseSource(typeof(SeedContentLocalization), nameof(SeedContentLocalization.Languages))]
		public void Translated_variants_keep_every_identifier_and_the_structure(string language)
		{
			foreach (var set in ProtocolTemplateLibrary.Sets)
			{
				var localized = ProtocolTemplateLibrary.Localized(set, language);
				localized.Set.Key.Should().Be(set.Set.Key);
				localized.Set.License.Should().Be(set.Set.License);
				localized.Protocols.Select(p => p.Code).Should().Equal(set.Protocols.Select(p => p.Code));
				localized.Protocols.Select(p => p.Kind).Should().Equal(set.Protocols.Select(p => p.Kind));
				localized.Protocols.Select(p => p.Tags).Should().Equal(set.Protocols.Select(p => p.Tags), "search tags stay as written");
				for (var i = 0; i < set.Protocols.Count; i++)
				{
					localized.Protocols[i].Content.Blocks.Select(b => b.Type + ":" + b.Key).Should().Equal(set.Protocols[i].Content.Blocks.Select(b => b.Type + ":" + b.Key));
					ProtocolBlocks.Normalize(localized.Protocols[i].Content).Blocks.Should().HaveCount(set.Protocols[i].Content.Blocks.Count, set.Set.Key + "/" + set.Protocols[i].Code);
					ProtocolRules.Validate(new DispatchProtocol { Name = localized.Protocols[i].Name, Code = localized.Protocols[i].Code, Description = localized.Protocols[i].Description })
						.Should().BeEmpty(language + " " + set.Protocols[i].Code);
				}
			}

			foreach (var starter in LmsStarterCourseCatalog.All)
			{
				var localized = LmsStarterCourseCatalog.Localized(starter, language);
				LmsStarterCourseCatalog.Validate(localized).Should().BeEmpty(language + " " + starter.Key);
				localized.Key.Should().Be(starter.Key);
				localized.Code.Should().Be(starter.Code);
				localized.Tags.Should().Be(starter.Tags);
				localized.LessonCount.Should().Be(starter.LessonCount);
				localized.Check?.Questions.Select(q => q.Correct).Should().Equal(starter.Check?.Questions.Select(q => q.Correct));
			}

			foreach (var plan in LmsLessonPlanStarterCatalog.All)
			{
				var localized = LmsLessonPlanStarterCatalog.Localized(plan, language);
				localized.Key.Should().Be(plan.Key);
				localized.Content.Objectives.Should().HaveCount(plan.Content.Objectives.Count);
				localized.Content.PreBriefChecklist.Should().HaveCount(plan.Content.PreBriefChecklist.Count);
				localized.Content.StudentsPerInstructor.Should().Be(plan.Content.StudentsPerInstructor);
			}

			foreach (var sheet in LmsSkillTemplateCatalog.Sheets)
			{
				var localized = LmsSkillTemplateCatalog.Localized(sheet, language);
				localized.Code.Should().Be(sheet.Code);
				localized.Steps.Select(s => s.Key + ":" + s.Type + ":" + s.Critical).Should().Equal(sheet.Steps.Select(s => s.Key + ":" + s.Type + ":" + s.Critical));
				LmsSkillSheetEngine.Validate(new LmsSkillSheetContent { Steps = localized.Steps }).Should().BeEmpty(language + " " + sheet.Key);
			}

			foreach (var book in LmsSkillTemplateCatalog.TaskBooks)
			{
				var localized = LmsSkillTemplateCatalog.Localized(book, language);
				localized.PositionCode.Should().Be(book.PositionCode);
				localized.Tasks.Select(t => t.Number).Should().Equal(book.Tasks.Select(t => t.Number));
				if (book.Family == LmsTaskBookFamilies.Wildland)
					localized.Tasks.Should().OnlyContain(t => t.Title.Contains(book.PositionCode) && t.Title.Contains(t.Number), "numbered tasks keep the code and number");
			}

			foreach (var template in TrainingCategoryTemplateCatalog.All)
				TrainingCategoryTemplateCatalog.Localized(template, language).Items.Select(i => i.Code + ":" + i.TargetHours + ":" + i.TargetPeriod)
					.Should().Equal(template.Items.Select(i => i.Code + ":" + i.TargetHours + ":" + i.TargetPeriod));

			foreach (var framework in LmsCompetencyFrameworkCatalog.All)
			{
				var localized = LmsCompetencyFrameworkCatalog.Localized(framework, language);
				localized.Key.Should().Be(framework.Key);
				localized.Publisher.Should().Be(framework.Publisher);
				localized.Items.Select(i => i.Key + ":" + i.Code).Should().Equal(framework.Items.Select(i => i.Key + ":" + i.Code));
			}

			foreach (var template in CertificationTypeTemplateCatalog.All)
			{
				var localized = CertificationTypeTemplateCatalog.Localized(template, language);
				localized.Code.Should().Be(template.Code);
				localized.IssuingAuthority.Should().Be(template.IssuingAuthority);
				localized.Category.Should().Be(template.Category);
			}
		}

		[Test]
		public void Sector_and_region_templates_exist_and_load()
		{
			ProtocolTemplateLibrary.Sets.Select(s => s.Set.Key).Should().Contain(new[] { "law-enforcement-policy-starter", "security-post-orders-starter", "workplace-policies-starter" });
			ProtocolTemplateLibrary.Get("law-enforcement-policy-starter").Protocols.Should().Contain(p => p.Code == "LE-101" && p.RequiresAcknowledgment);
			ProtocolTemplateLibrary.Get("workplace-policies-starter").Protocols.Should().Contain(p => p.Code == "WP-101" && p.RequiresAcknowledgment);

			LmsStarterCourseCatalog.Problems.Should().BeEmpty();
			LmsStarterCourseCatalog.Courses.Select(c => c.Key).Should().Contain(new[]
			{
				"workplace-onboarding", "confined-space-awareness", "lockout-tagout-awareness", "hazwoper-refresher-outline", "security-officer-orientation", "annual-policy-review"
			});

			TrainingCategoryTemplateCatalog.All.Select(t => t.Key).Should().Contain(new[]
			{
				TrainingCategoryTemplateCatalog.WorkplaceCompliance, TrainingCategoryTemplateCatalog.IndustrialSafety, TrainingCategoryTemplateCatalog.SarTeam,
				TrainingCategoryTemplateCatalog.LawEnforcementInService, TrainingCategoryTemplateCatalog.SecurityLicensing, TrainingCategoryTemplateCatalog.CpdHours,
				TrainingCategoryTemplateCatalog.CanadaWorkplace, TrainingCategoryTemplateCatalog.EuWorkplace
			});
			TrainingCategoryTemplateCatalog.All.SelectMany(t => t.Items).Select(i => i.Code).Should().OnlyHaveUniqueItems();
			// Region and sector templates are structure only: the organization sets the hours its regulator or licensing body requires.
			TrainingCategoryTemplateCatalog.All.Where(t => t.Kind == TrainingCategoryKinds.Custom).SelectMany(t => t.Items).Should().OnlyContain(i => i.TargetHours == null);

			LmsLessonPlanStarterCatalog.All.Select(s => s.Key).Should().Contain(new[] { "evacuation-drill", "confined-space-rescue-drill", "sar-field-exercise", "scenario-de-escalation" });
			LmsSkillTemplateCatalog.Sheets.Select(s => s.Sector).Should().Contain(new[] { "SAR", "Industrial", "Workplace", "Security" });

			CertificationTypeTemplateCatalog.All.Select(t => t.Code).Should().OnlyHaveUniqueItems();
			CertificationTypeTemplateCatalog.GetByCategory(CertificationCategories.LawEnforcement).Should().NotBeEmpty();
			CertificationTypeTemplateCatalog.Search("canada").Should().HaveCountGreaterThan(3);
			CertificationTypeTemplateCatalog.Search("eu").Should().HaveCountGreaterThan(3);
		}

		[Test]
		public void Suggestions_follow_the_operating_profile_and_never_hide_a_template()
		{
			SeedSectorRecommendations.All.Values.SelectMany(v => v).Should().OnlyContain(code => DepartmentOperatingProfile.ArchetypeCodes.Contains(code));
			foreach (var entry in SeedSectorRecommendations.All.Keys)
			{
				var parts = entry.Split(new[] { ':' }, 2);
				var exists = parts[0] switch
				{
					SeedContentLocalization.ProtocolTemplates => ProtocolTemplateLibrary.Get(parts[1]) != null,
					SeedContentLocalization.StarterCourses => LmsStarterCourseCatalog.Find(parts[1]) != null,
					SeedContentLocalization.CategoryTemplates => TrainingCategoryTemplateCatalog.Find(parts[1]) != null,
					_ => false
				};
				exists.Should().BeTrue(entry + " names a template that ships");
			}

			var lawEnforcement = new DepartmentOperatingProfile { Archetypes = new List<string> { "law-enforcement" } };
			var ordered = SeedSectorRecommendations.SuggestedFirst(TrainingCategoryTemplateCatalog.All, SeedContentLocalization.CategoryTemplates, t => t.Key, lawEnforcement);
			ordered.Should().HaveCount(TrainingCategoryTemplateCatalog.All.Count, "a suggestion orders the library; it hides nothing");
			ordered[0].Key.Should().Be(TrainingCategoryTemplateCatalog.LawEnforcementInService);

			var none = SeedSectorRecommendations.SuggestedFirst(TrainingCategoryTemplateCatalog.All, SeedContentLocalization.CategoryTemplates, t => t.Key, new DepartmentOperatingProfile());
			none.Select(t => t.Key).Should().Equal(TrainingCategoryTemplateCatalog.All.Select(t => t.Key), "no profile, no reordering");
			SeedSectorRecommendations.Suggested(SeedContentLocalization.StarterCourses, LmsStarterCourseCatalog.All.Select(s => s.Key), null).Should().BeEmpty();
			SeedSectorRecommendations.IsSuggested(SeedContentLocalization.ProtocolTemplates, "security-post-orders-starter",
				new DepartmentOperatingProfile { Archetypes = new List<string> { "security" } }).Should().BeTrue();
		}
	}
}
