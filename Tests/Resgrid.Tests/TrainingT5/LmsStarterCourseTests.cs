using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Services;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>
	/// T5 item 4, the Resgrid starter course library: every embedded starter loads and validates, carries Resgrid's own text (no standard
	/// cited by number) under the Resgrid starter licence; a clone is a DRAFT department course that passes the course validator and keeps its
	/// attribution (source, licence, revision) on the origin row and in the overview; partner slots are documentation only; the LMS gate applies.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class LmsStarterCourseTests
	{
		private CourseStudioWorld _world;

		[SetUp]
		public void SetUp() => _world = new CourseStudioWorld();

		[TearDown]
		public void TearDown() => _world.Dispose();

		[Test]
		public void Every_embedded_starter_loads_and_validates()
		{
			LmsStarterCourseCatalog.Problems.Should().BeEmpty();
			LmsStarterCourseCatalog.Courses.Select(c => c.Key).Should().Contain(new[]
			{
				"resgrid-onboarding", "incident-safety-basics", "hazmat-awareness-outline", "driver-evo-basics"
			});
			LmsStarterCourseCatalog.PartnerSlots.Select(p => p.Launch).Should().BeEquivalentTo(new[] { "lti13", "scorm" });
			foreach (var starter in LmsStarterCourseCatalog.All)
				LmsStarterCourseCatalog.Validate(starter).Should().BeEmpty(starter.Key);
		}

		[Test]
		public void Starters_are_Resgrid_text_with_no_standard_quoted_and_no_accreditation_claimed()
		{
			foreach (var starter in LmsStarterCourseCatalog.Courses)
			{
				starter.License.Should().Be(LmsStarterCourseCatalog.ResgridLicense);
				var text = string.Join("\n", new[] { starter.Title, starter.Summary, starter.OverviewHtml }
					.Concat(starter.Modules.SelectMany(m => m.Lessons.Select(l => l.Title + "\n" + l.Html)))
					.Concat(starter.Check.Questions.Select(q => q.Text + "\n" + string.Join("\n", q.Options) + "\n" + q.Explanation)));
				// Standards are licensed text: a starter neither cites one by number nor claims to satisfy one.
				foreach (var marker in new[] { "NFPA", "29 CFR", "OSHA 1910", "NIOSH", "accredited", "CAPCE", "certifies" })
					text.Should().NotContainEquivalentOf(marker, starter.Key);
			}
		}

		[Test]
		public void A_validator_finds_an_empty_module_a_blank_lesson_and_an_empty_bank()
		{
			var outline = new LmsCourseOutline
			{
				Course = new LmsCourse { Title = "T" },
				Version = new LmsCourseVersion { Title = "T", Hours = 1 },
				Modules =
				{
					new LmsOutlineModule { Module = new LmsCourseModule { Title = "" } },
					new LmsOutlineModule
					{
						Module = new LmsCourseModule { Title = "M" },
						Lessons =
						{
							new LmsLesson { Kind = (int)LmsLessonKinds.Blocks, Title = "Blank", ContentHtml = "<p> </p>" },
							new LmsLesson { Kind = (int)LmsLessonKinds.Assessment, Title = "Check", LmsQuestionBankId = 9 }
						}
					}
				}
			};

			var problems = LmsCourseValidator.Validate(outline, new System.Collections.Generic.Dictionary<int, int> { [9] = 0 }, StringHelpers.HtmlToPlainText);

			problems.Should().BeEquivalentTo(new[]
			{
				LmsCourseValidator.ModuleTitle + ":1", LmsCourseValidator.EmptyModule + ":1", LmsCourseValidator.LessonContent + ":2.1", LmsCourseValidator.AssessmentBank + ":2.2"
			});
		}

		[Test]
		public async Task A_clone_is_a_draft_that_keeps_its_attribution_and_passes_the_course_validator()
		{
			var starter = LmsStarterCourseCatalog.Find("incident-safety-basics");

			var result = await _world.Starters.CloneAsync(_world.Lms.AdminActor, starter.Key, "en");

			result.Outcome.Should().Be(LmsStudioOutcomes.Created, result.Outcome);
			result.Lessons.Should().Be(starter.LessonCount);
			result.Questions.Should().Be(starter.Check.Questions.Count);

			var course = _world.Lms.Courses.Rows.Single();
			course.Status.Should().Be((int)ProtocolStatuses.Draft);
			course.Code.Should().Be(starter.Code);
			var version = _world.Lms.Versions.Rows.Single();
			version.Status.Should().Be((int)ProtocolVersionStatuses.Draft);
			version.Hours.Should().Be(starter.Hours);

			var origin = _world.Origins.Rows.Single();
			origin.OriginKind.Should().Be((int)LmsCourseOriginKinds.Starter);
			origin.SourceKey.Should().Be("starter:incident-safety-basics");
			origin.SourceName.Should().Be("Resgrid");
			origin.SourceVersion.Should().Be(starter.Revision.ToString());
			origin.License.Should().Be(LmsStarterCourseCatalog.ResgridLicense);
			origin.Attribution.Should().Contain(starter.Title);
			version.ContentHtml.Should().Contain(starter.Title, "the attribution travels with the content in the overview");

			var outline = await _world.Lms.CourseService.GetOutlineAsync(CourseStudioWorld.DepartmentId, version.LmsCourseVersionId);
			outline.Modules.Select(m => m.Module.Title).Should().Equal(starter.Modules.Select(m => m.Title).Append(starter.Check.Module));
			LmsCourseValidator.Validate(outline, _world.LiveQuestionsByBank(), StringHelpers.HtmlToPlainText).Should().BeEmpty();
			_world.Lms.Questions.Rows.Should().Contain(q => q.Kind == (int)LmsQuestionKinds.TrueFalse);
			_world.Events.Verify(e => e.SendMessage(It.Is<Resgrid.Model.Events.AuditEvent>(a => a.Type == AuditLogTypes.LmsStarterCourseCloned)), Times.Once);

			// The department's normal approval takes it from there.
			var published = await _world.PublishAsync(version.LmsCourseVersionId);
			published.Status.Should().Be((int)ProtocolVersionStatuses.Published);
		}

		[Test]
		public async Task Every_starter_clones_into_a_valid_draft()
		{
			foreach (var starter in LmsStarterCourseCatalog.Courses)
			{
				var result = await _world.Starters.CloneAsync(_world.Lms.AdminActor, starter.Key, "en");
				result.Outcome.Should().Be(LmsStudioOutcomes.Created, starter.Key);
				var outline = await _world.Lms.CourseService.GetOutlineAsync(CourseStudioWorld.DepartmentId, result.LmsCourseVersionId);
				LmsCourseValidator.Validate(outline, _world.LiveQuestionsByBank(), StringHelpers.HtmlToPlainText).Should().BeEmpty(starter.Key);
			}
			_world.Origins.Rows.Should().HaveCount(LmsStarterCourseCatalog.Courses.Count());
		}

		[Test]
		public async Task Partner_slots_are_never_cloned_and_the_LMS_gate_applies()
		{
			(await _world.Starters.CloneAsync(_world.Lms.AdminActor, "partner-ce-lti", "en")).Outcome.Should().Be(LmsStudioOutcomes.NotFound);
			(await _world.Starters.CloneAsync(_world.Lms.AdminActor, "no-such-starter", "en")).Outcome.Should().Be(LmsStudioOutcomes.NotFound);
			(await _world.Starters.CloneAsync(_world.Lms.Actor(LmsWorld.Member), "resgrid-onboarding", "en")).Outcome.Should().Be(LmsStudioOutcomes.NotAllowed);

			_world.Lms.Gate.AddonActive = false;
			(await _world.Starters.CloneAsync(_world.Lms.AdminActor, "resgrid-onboarding", "en")).Outcome.Should().Be(LmsStudioOutcomes.NotEntitled);

			_world.Lms.Courses.Rows.Should().BeEmpty();
			_world.Origins.Rows.Should().BeEmpty();
		}
	}
}
