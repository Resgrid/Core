using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Academy;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.Academy
{
	/// <summary>
	/// A2 manifests as code (plan §15): the lesson manifests load and validate, they are the same course set B seeded from code (ids,
	/// SystemKeys, revisions, checks, entitlement keys, English titles, media), a re-run writes nothing, a minor revision keeps completions,
	/// a major one is recorded and worker 88 re-assigns or notifies once, and a moved media base re-links the stored videos.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class AcademyManifestTests
	{
		/// <summary>B's A1 code seed (AcademyCourseManifest before A2): visibility keys, q1/q2 correct options, q3 true or false.</summary>
		private static readonly Dictionary<string, (string Keys, string Q1, string Q2, bool Q3)> A1Seed = new Dictionary<string, (string, string, string, bool)>
		{
			["00-start"] = (null, "b", "a", true), ["01-people"] = (null, "c", "a", true), ["02-security"] = (null, "a", "c", false),
			["03-units"] = (null, "b", "c", true), ["04-apps"] = (null, "a", "b", false), ["05-calls"] = (null, "c", "b", true),
			["06-communication"] = (null, "a", "a", false), ["07-mapping"] = ("module:Mapping", "b", "c", true), ["08-shifts"] = ("module:Shifts", "c", "a", true),
			["09-training"] = (null, "a", "b", false), ["10-knowledge"] = (null, "b", "a", true), ["11-checklists"] = ("module:Checklists", "c", "b", false),
			["12-maintenance"] = ("module:Maintenance", "a", "c", true), ["13-inventory"] = ("module:Inventory", "b", "b", false),
			["14-records"] = ("module:Records,flag:" + FeatureFlagKeys.RecordsSystem, "c", "a", true), ["15-deployments"] = ("module:Deployments", "a", "b", true),
			["16-business"] = ("module:BusinessOperations,addon:BusinessOperations", "b", "c", false), ["17-automation"] = (null, "c", "a", true),
			["18-ptt"] = ("addon:PTT", "a", "c", false), ["19-adp"] = ("addon:ADP", "b", "a", true), ["20-plans"] = (null, "c", "b", false),
			["21-ai"] = ("addon:EnhancedAi", "a", "b", true), ["22-relay"] = (null, "b", "c", false), ["23-self-hosting"] = (null, "c", "a", true),
			["01-setup-wizard"] = (null, "a", "c", true), ["02-department-settings"] = (null, "b", "a", false), ["03-custom-statuses"] = (null, "c", "b", true),
			["06-dispatch-settings"] = (null, "a", "a", false), ["12-chat"] = ("module:Chat", "b", "c", true), ["13-assistant"] = ("addon:EnhancedAi", "c", "b", false),
			["14-admin-assist"] = (null, "a", "c", true),
		};

		/// <summary>B's English lesson titles (AcademyCourses.en.resx before A2 moved them to the manifests).</summary>
		private static readonly Dictionary<string, string> A1Titles = new Dictionary<string, string>
		{
			["00-start"] = "Resgrid: from the first alert to the next shift",
			["01-people"] = "Build a roster dispatch can use",
			["01-setup-wizard"] = "Set up a department with the nine-step wizard",
			["02-department-settings"] = "Configure department-wide behavior",
			["02-security"] = "Configure a department and protect access",
			["03-custom-statuses"] = "Create statuses with useful operational meaning",
			["03-units"] = "Represent apparatus, crews, and GPS devices",
			["04-apps"] = "Put the right Resgrid app on each device",
			["05-calls"] = "Take an incident from intake to closeout",
			["06-communication"] = "Make alerts reach the intended audience",
			["06-dispatch-settings"] = "Control recipient selection and dispatch behavior",
			["07-mapping"] = "Make location information useful and current",
			["08-shifts"] = "Plan coverage and keep dispatch aligned",
			["09-training"] = "Assign learning and track qualifications",
			["10-knowledge"] = "Give members a dependable reference library",
			["11-checklists"] = "Record routine checks and missed work",
			["12-chat"] = "Use chat channels, messages and acknowledgements",
			["12-maintenance"] = "Turn defects into accountable repairs",
			["13-assistant"] = "Use the Assistant and understand AI prerequisites",
			["13-inventory"] = "Know what is available, issued, and expiring",
			["14-admin-assist"] = "Use Admin Assist to review changes and follow up",
			["14-records"] = "Keep incident and prevention records reviewable",
			["15-deployments"] = "Keep deployment activity and cost evidence together",
			["16-business"] = "Connect service activity to costs and billing",
			["17-automation"] = "Automate a repeatable task with visible outcomes",
			["18-ptt"] = "Set up voice channels for team coordination",
			["19-adp"] = "Plan protected-data access and recovery",
			["20-plans"] = "Match subscription capacity to the roster",
			["21-ai"] = "Understand AI availability and human review",
			["22-relay"] = "Relay: bring an audio dispatch feed into Resgrid",
			["23-self-hosting"] = "Self-hosting: configuration and operating responsibilities",
		};

		/// <summary>B's A0 catalog order (24 lessons, then 7 walkthroughs) with topics and minutes.</summary>
		private static readonly (string Id, string Topic, double Minutes, bool Walkthrough)[] A0Catalog =
		{
			("00-start", "Orientation", 3.0, false), ("01-people", "People", 2.6, false), ("02-security", "Security", 3.7, false), ("03-units", "Units", 2.4, false),
			("04-apps", "Apps", 3.0, false), ("05-calls", "Calls", 4.3, false), ("06-communication", "Communication", 3.6, false), ("07-mapping", "Mapping", 3.6, false),
			("08-shifts", "Shifts", 2.5, false), ("09-training", "Training", 2.7, false), ("10-knowledge", "Knowledge", 4.6, false), ("11-checklists", "Checklists", 2.3, false),
			("12-maintenance", "Maintenance", 4.1, false), ("13-inventory", "Inventory", 3.0, false), ("14-records", "Records", 9.4, false), ("15-deployments", "Deployments", 3.2, false),
			("16-business", "Business", 8.9, false), ("17-automation", "Automation", 2.6, false), ("18-ptt", "Push-to-talk", 1.9, false), ("19-adp", "Data protection", 2.7, false),
			("20-plans", "Plans", 1.7, false), ("21-ai", "AI", 2.4, false), ("22-relay", "Relay", 1.2, false), ("23-self-hosting", "Self-hosting", 1.5, false),
			("01-setup-wizard", "Setup wizard", 7.9, true), ("02-department-settings", "Department settings", 6.7, true), ("03-custom-statuses", "Custom statuses", 7.1, true),
			("06-dispatch-settings", "Dispatch settings", 7.7, true), ("12-chat", "Chat", 5.9, true), ("13-assistant", "Assistant", 6.0, true), ("14-admin-assist", "Admin Assist", 6.4, true),
		};

		private string _academyUrl, _mediaUrl;
		private bool _doNotBroadcast;
		private LmsWorld _world;

		[SetUp]
		public void SetUp()
		{
			(_academyUrl, _mediaUrl, _doNotBroadcast) = (SystemBehaviorConfig.AcademyUrl, SystemBehaviorConfig.AcademyMediaUrl, SystemBehaviorConfig.DoNotBroadcast);
			SystemBehaviorConfig.AcademyUrl = "https://academy.resgrid.com/";
			SystemBehaviorConfig.AcademyMediaUrl = "";
			SystemBehaviorConfig.DoNotBroadcast = false;
			LmsWorld.ResetAcademySeedMemo();
			_world = new LmsWorld();
		}

		[TearDown]
		public void TearDown()
		{
			(SystemBehaviorConfig.AcademyUrl, SystemBehaviorConfig.AcademyMediaUrl, SystemBehaviorConfig.DoNotBroadcast) = (_academyUrl, _mediaUrl, _doNotBroadcast);
			LmsWorld.ResetAcademySeedMemo();
		}

		private static AcademyLessonManifest Clone(string lessonId) =>
			JsonConvert.DeserializeObject<AcademyLessonManifest>(JsonConvert.SerializeObject(AcademyManifests.FindLesson(lessonId)));

		#region Manifests

		[Test]
		public void The_embedded_manifests_load_and_pass_validation()
		{
			AcademyManifests.Lessons.Should().HaveCount(31);
			AcademyManifests.Validate(AcademyManifests.Set).Should().BeEmpty();
			AcademyManifests.Tours.Select(t => t.Key).Should().BeEquivalentTo(new[] { "administrator", "dispatcher", "training-officer", "member" });
			AcademyManifests.WhatsNew.Should().NotBeEmpty();
		}

		[Test]
		public void The_manifests_are_the_course_set_B_seeded_from_code()
		{
			AcademyCatalog.Lessons.Select(l => (l.Id, l.Topic, l.Minutes, l.IsWalkthrough)).Should().Equal(A0Catalog.Select(c => (c.Id, c.Topic, c.Minutes, c.Walkthrough)),
				"the A0 catalog keeps its order, topics and lengths");
			AcademyCourseManifest.Courses.Select(c => c.LessonId).Should().Equal(A0Catalog.Select(c => c.Id));

			foreach (var definition in AcademyCourseManifest.Courses)
			{
				var seed = A1Seed[definition.LessonId];
				definition.SystemKey.Should().Be("academy:" + definition.LessonId);
				definition.Revision.Should().Be(AcademyCourseManifest.ManifestVersion, "every lesson starts at the revision B stamped, so nothing is re-seeded");
				definition.VisibilityKeys.Should().Be(seed.Keys ?? string.Empty, definition.LessonId);
				definition.Questions.Select(q => (q.Key, q.Kind, q.Correct)).Should().Equal(new[]
				{
					("q1", LmsQuestionKinds.SingleChoice, seed.Q1), ("q2", LmsQuestionKinds.SingleChoice, seed.Q2), ("q3", LmsQuestionKinds.TrueFalse, seed.Q3 ? "t" : "f")
				}, definition.LessonId);
				AcademyManifests.Text(definition.Manifest.Title, "en").Should().Be(A1Titles[definition.LessonId]);
				AcademyCourseManifest.VideoUrlFor("https://academy.resgrid.com/", definition).Should().Be(AcademyCourseManifest.VideoUrl("https://academy.resgrid.com/", definition.Lesson));
				AcademyCourseManifest.CaptionTracksFor("https://academy.resgrid.com/", definition).Should().ContainSingle()
					.Which.Url.Should().Be(AcademyCourseManifest.CaptionsUrl("https://academy.resgrid.com/", definition.Lesson));
			}
		}

		[Test]
		public void Every_lesson_text_exists_in_every_product_locale()
		{
			foreach (var lesson in AcademyManifests.Lessons)
			{
				var maps = new List<(string Name, Dictionary<string, string> Map)> { (lesson.Id + ".Title", lesson.Title) };
				foreach (var question in lesson.Check)
				{
					maps.Add((lesson.Id + "." + question.Key, question.Text));
					maps.AddRange(question.Options.Select(o => (lesson.Id + "." + question.Key + "." + o.Key, o.Text)));
				}
				foreach (var (name, map) in maps)
					foreach (var locale in Resgrid.Localization.SupportedLocales.GetSupportedCultures())
						map.TryGetValue(locale, out var value).Should().BeTrue(name + " is missing " + locale);
			}
		}

		[Test]
		public void Tours_and_releases_are_translated_and_open_existing_pages()
		{
			var locales = Resgrid.Localization.SupportedLocales.GetSupportedCultures().ToList();
			var assembly = typeof(Resgrid.Web.Areas.User.Controllers.HelpController).Assembly;
			void Page(AcademyManifestPage page, string owner)
			{
				var type = assembly.GetType("Resgrid.Web.Areas.User.Controllers." + page.Controller + "Controller");
				type.Should().NotBeNull(owner);
				type.GetMethods().Any(m => m.Name == page.Action && m.GetCustomAttribute<HttpPostAttribute>() == null).Should().BeTrue(owner + " opens " + page.Url);
			}

			foreach (var tour in AcademyManifests.Tours)
			{
				locales.Where(l => !tour.Title.ContainsKey(l) || !tour.Intro.ContainsKey(l)).Should().BeEmpty(tour.Key);
				foreach (var task in tour.Tasks)
				{
					locales.Where(l => !task.Title.ContainsKey(l) || !task.Hint.ContainsKey(l)).Should().BeEmpty(tour.Key + "." + task.Key);
					locales.Where(l => l != "en" && task.Title[l] == task.Title["en"]).Should().BeEmpty("translated, not copied: " + tour.Key + "." + task.Key);
					Page(task.Page, tour.Key + "." + task.Key);
				}
			}
			foreach (var release in AcademyManifests.WhatsNew)
				foreach (var item in release.Items)
				{
					locales.Where(l => !item.Title.ContainsKey(l) || !item.Body.ContainsKey(l)).Should().BeEmpty(release.Release + "." + item.Key);
					Page(item.Page, release.Release + "." + item.Key);
				}
		}

		[Test]
		public void A_broken_manifest_is_refused()
		{
			var good = JsonConvert.SerializeObject(AcademyManifests.FindLesson("05-calls"));
			var lesson = JsonConvert.DeserializeObject<AcademyLessonManifest>(good);
			lesson.Title.Remove("en");
			lesson.Check[0].Correct = "z";
			lesson.Revision = 2;
			var other = JsonConvert.DeserializeObject<AcademyLessonManifest>(good);
			other.Id = "99-copy";

			var errors = AcademyManifests.Validate(new AcademyManifestSet { Lessons = new List<AcademyLessonManifest> { lesson, other } });

			errors.Should().Contain(e => e.Contains("English title"));
			errors.Should().Contain(e => e.Contains("correct option 'z'"));
			errors.Should().Contain(e => e.Contains("no entry for revision 2"));
			errors.Should().Contain(e => e.Contains("already claimed"), "a page opens one lesson only");

			FluentActions.Invoking(() => AcademyManifests.Parse(new[] { ("Lessons.bad.json", good.Replace("\"Topic\"", "\"Topik\"")) }))
				.Should().Throw<InvalidOperationException>("an unknown field is a typo, not data to drop");
		}

		[Test]
		public void Media_follow_the_configurable_media_base()
		{
			var definition = AcademyCourseManifest.Find("01-setup-wizard");
			SystemBehaviorConfig.AcademyMediaUrl = "https://media.station.local/academy";

			AcademyManifests.MediaBase.Should().Be("https://media.station.local/academy/");
			AcademyCourseManifest.VideoUrlFor(AcademyManifests.MediaBase, definition).Should().Be("https://media.station.local/academy/deep-dive/media/01-setup-wizard.mp4");
			SystemBehaviorConfig.AcademyMediaUrl = "";
			AcademyManifests.MediaBase.Should().Be("https://academy.resgrid.com/");
			AcademyManifests.SeedFingerprint("https://a/").Should().NotBe(AcademyManifests.SeedFingerprint("https://b/"));
		}

		#endregion Manifests

		#region Seeding

		[Test]
		public async Task Loading_the_manifests_over_Bs_seed_writes_nothing_and_keeps_completions()
		{
			// The A1 seed (revision 1 everywhere) as B left it, with a completion on it.
			(await _world.Academy.EnsureSeededAsync()).Should().Be(AcademyCourseManifest.Courses.Count);
			var course = _world.Courses.Rows.Single(c => c.SystemKey == "academy:05-calls");
			_world.Enrollments.Rows.Add(new LmsEnrollment
			{
				LmsEnrollmentId = 9001, DepartmentId = LmsWorld.DepartmentId, LmsCourseId = course.LmsCourseId, LmsCourseVersionId = course.CurrentVersionId.Value,
				UserId = LmsWorld.Member, Source = (int)LmsEnrollmentSources.Assigned, Status = (int)LmsEnrollmentStatuses.Completed, CompletedOn = DateTime.UtcNow
			});
			var counts = (_world.Courses.Rows.Count, _world.Versions.Rows.Count, _world.Lessons.Rows.Count, _world.Banks.Rows.Count, _world.Questions.Rows.Count);
			var titles = _world.Questions.Rows.Select(q => q.Text).ToList();

			// A new process loads the manifest files: same SystemKeys and revisions, so not one row changes.
			LmsWorld.ResetAcademySeedMemo();
			(await _world.Academy.EnsureSeededAsync()).Should().Be(0);
			(_world.Courses.Rows.Count, _world.Versions.Rows.Count, _world.Lessons.Rows.Count, _world.Banks.Rows.Count, _world.Questions.Rows.Count).Should().Be(counts);
			_world.Questions.Rows.Select(q => q.Text).Should().Equal(titles);
			_world.Enrollments.Rows.Single(e => e.LmsEnrollmentId == 9001).Status.Should().Be((int)LmsEnrollmentStatuses.Completed);
			_world.Courses.Rows.Should().OnlyContain(c => c.ManifestVersion == 1);
			_world.AcademyUpdates.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task A_minor_revision_publishes_a_minor_version_and_completions_stand()
		{
			await _world.Academy.EnsureSeededAsync();
			var course = _world.Courses.Rows.Single(c => c.SystemKey == "academy:00-start");
			var first = course.CurrentVersionId.Value;
			_world.Enrollments.Rows.Add(new LmsEnrollment
			{
				LmsEnrollmentId = 9002, DepartmentId = LmsWorld.DepartmentId, LmsCourseId = course.LmsCourseId, LmsCourseVersionId = first,
				UserId = LmsWorld.Member, Source = (int)LmsEnrollmentSources.Assigned, Status = (int)LmsEnrollmentStatuses.Completed, CompletedOn = DateTime.UtcNow
			});

			var manifest = Clone("00-start");
			manifest.Revision = 2;
			manifest.Title["en"] = "Resgrid: from the first alert to the next shift (2026)";
			manifest.History.Add(new AcademyManifestRevision { Revision = 2, Release = "2027.01", Change = AcademyManifestRevision.Minor, Note = "New dashboard footage." });
			(await _world.Academy.SeedCourseAsync(AcademyCourseManifest.FromManifest(manifest), CancellationToken.None)).Should().BeTrue();

			course = _world.Courses.Rows.Single(c => c.SystemKey == "academy:00-start");
			course.ManifestVersion.Should().Be(2);
			var current = _world.Versions.Rows.Single(v => v.LmsCourseVersionId == course.CurrentVersionId);
			(current.Major, current.Minor).Should().Be((1, 1));
			current.MajorChangePolicy.Should().Be((int)LmsMajorChangePolicies.Nothing);
			current.ChangeSummary.Should().Contain("revision 2").And.Contain("New dashboard footage.");
			_world.Versions.Rows.Single(v => v.LmsCourseVersionId == first).Status.Should().Be((int)ProtocolVersionStatuses.Superseded);
			_world.Enrollments.Rows.Single(e => e.LmsEnrollmentId == 9002).Should().Match<LmsEnrollment>(e => e.Status == (int)LmsEnrollmentStatuses.Completed && e.LmsCourseVersionId == first);
			_world.AcademyUpdates.Rows.Should().BeEmpty("a minor change notifies nobody");
			(await _world.Academy.SeedCourseAsync(AcademyCourseManifest.FromManifest(manifest), CancellationToken.None)).Should().BeFalse("the same revision again is a no-op");
		}

		[Test]
		public async Task A_major_revision_is_recorded_and_worker_88_reassigns_completed_learners_once()
		{
			await _world.Academy.EnsureSeededAsync();
			var course = _world.Courses.Rows.Single(c => c.SystemKey == "academy:05-calls");
			var first = course.CurrentVersionId.Value;
			_world.Enrollments.Rows.Add(new LmsEnrollment
			{
				LmsEnrollmentId = 9003, DepartmentId = LmsWorld.DepartmentId, LmsCourseId = course.LmsCourseId, LmsCourseVersionId = first,
				UserId = LmsWorld.Member, Source = (int)LmsEnrollmentSources.RolePath, Status = (int)LmsEnrollmentStatuses.Completed, CompletedOn = DateTime.UtcNow
			});
			// A department where the lesson was only self-enrolled does not require it.
			_world.Enrollments.Rows.Add(new LmsEnrollment
			{
				LmsEnrollmentId = 9004, DepartmentId = 77, LmsCourseId = course.LmsCourseId, LmsCourseVersionId = first,
				UserId = "other-dept-member", Source = (int)LmsEnrollmentSources.SelfEnrolled, Status = (int)LmsEnrollmentStatuses.Completed, CompletedOn = DateTime.UtcNow
			});
			// Round 5F: the department requires the lesson through a T3 requirement and chose re-assign (due in the requirement's 14 days).
			_world.AcademyUpdates.Requirements.Add(new LmsRequirement
			{
				LmsRequirementId = 501, DepartmentId = LmsWorld.DepartmentId, Name = "Calls", Kind = (int)LmsRequirementKinds.Course, TargetRefId = course.LmsCourseId,
				DueDays = 14, AcademyUpdatePolicy = (int)LmsMajorChangePolicies.Reassign
			});
			var members = new Mock<ILmsRequirementMembersRepository>();
			members.Setup(m => m.GetOpenForDepartmentAsync(LmsWorld.DepartmentId)).ReturnsAsync(new List<LmsRequirementMember>
			{
				new LmsRequirementMember { DepartmentId = LmsWorld.DepartmentId, LmsRequirementId = 501, UserId = LmsWorld.Member }
			});

			var manifest = Clone("05-calls");
			manifest.Revision = 2;
			manifest.History.Add(new AcademyManifestRevision { Revision = 2, Release = "2027.01", Change = AcademyManifestRevision.Major, Policy = AcademyManifestRevision.PolicyReassign, ReassignDueDays = 14 });
			(await _world.Academy.SeedCourseAsync(AcademyCourseManifest.FromManifest(manifest), CancellationToken.None)).Should().BeTrue();

			course = _world.Courses.Rows.Single(c => c.SystemKey == "academy:05-calls");
			var current = _world.Versions.Rows.Single(v => v.LmsCourseVersionId == course.CurrentVersionId);
			(current.Major, current.Minor).Should().Be((2, 0));
			current.MajorChangePolicy.Should().Be((int)LmsMajorChangePolicies.Reassign);
			_world.AcademyUpdates.Rows.Should().ContainSingle().Which.Should().Match<AcademyLessonUpdate>(u => u.Revision == 2 && u.Policy == (int)LmsMajorChangePolicies.Reassign && !u.CompletedOn.HasValue);

			var communication = new Mock<ICommunicationService>();
			var settings = new Mock<IDepartmentSettingsService>();
			var updates = new AcademyManifestUpdateService(_world.AcademyUpdates, _world.Courses, _world.Versions, _world.Enrollments, _world.Departments.Object, settings.Object,
				Mock.Of<IUserProfileService>(), new Lazy<ICommunicationService>(() => communication.Object), members.Object);
			var now = DateTime.UtcNow;

			(await updates.ApplyPendingAsync(now)).Should().BeGreaterThan(0);

			var reassigned = _world.Enrollments.Rows.Where(e => e.LmsCourseId == course.LmsCourseId && e.Source == (int)LmsEnrollmentSources.Reassigned).ToList();
			reassigned.Should().ContainSingle().Which.Should().Match<LmsEnrollment>(e => e.UserId == LmsWorld.Member && e.LmsCourseVersionId == current.LmsCourseVersionId &&
				e.DueOn.HasValue && e.DueOn.Value.Date == now.AddDays(14).Date);
			_world.Enrollments.Rows.Single(e => e.LmsEnrollmentId == 9003).Status.Should().Be((int)LmsEnrollmentStatuses.Completed, "the earlier completion stays on the transcript");
			_world.Enrollments.Rows.Should().NotContain(e => e.DepartmentId == 77 && e.Source == (int)LmsEnrollmentSources.Reassigned);
			communication.Verify(x => x.SendNotificationAsync(LmsWorld.Member, LmsWorld.DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(),
				It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Once);
			_world.AcademyUpdates.Rows.Single().Should().Match<AcademyLessonUpdate>(u => u.CompletedOn.HasValue && u.AppliedThroughDepartmentId == LmsWorld.DepartmentId);

			// Nothing is applied twice.
			(await updates.ApplyPendingAsync(now)).Should().Be(0);
			communication.Verify(x => x.SendNotificationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(),
				It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task A_major_update_follows_each_departments_own_choice_on_its_requirements()
		{
			await _world.Academy.EnsureSeededAsync();
			var course = _world.Courses.Rows.Single(c => c.SystemKey == "academy:05-calls");
			var first = course.CurrentVersionId.Value;
			_world.Enrollments.Rows.Add(new LmsEnrollment
			{
				LmsEnrollmentId = 9101, DepartmentId = LmsWorld.DepartmentId, LmsCourseId = course.LmsCourseId, LmsCourseVersionId = first,
				UserId = LmsWorld.Member, Source = (int)LmsEnrollmentSources.Assigned, Status = (int)LmsEnrollmentStatuses.Completed, CompletedOn = DateTime.UtcNow
			});
			_world.Enrollments.Rows.Add(new LmsEnrollment
			{
				LmsEnrollmentId = 9102, DepartmentId = LmsWorld.DepartmentId, LmsCourseId = course.LmsCourseId, LmsCourseVersionId = first,
				UserId = LmsWorld.Member2, Source = (int)LmsEnrollmentSources.SelfEnrolled, Status = (int)LmsEnrollmentStatuses.Completed, CompletedOn = DateTime.UtcNow
			});

			// The manifest asks for re-assign, but this department's requirement chose "notify": nobody is re-assigned, both are told.
			_world.AcademyUpdates.Requirements.Add(new LmsRequirement
			{
				LmsRequirementId = 601, DepartmentId = LmsWorld.DepartmentId, Name = "Calls", Kind = (int)LmsRequirementKinds.Course, TargetRefId = course.LmsCourseId,
				DueDays = 21, AcademyUpdatePolicy = (int)LmsMajorChangePolicies.Notify
			});
			var members = new Mock<ILmsRequirementMembersRepository>();
			members.Setup(m => m.GetOpenForDepartmentAsync(It.IsAny<int>())).ReturnsAsync(new List<LmsRequirementMember>
			{
				new LmsRequirementMember { DepartmentId = LmsWorld.DepartmentId, LmsRequirementId = 601, UserId = LmsWorld.Member },
				new LmsRequirementMember { DepartmentId = LmsWorld.DepartmentId, LmsRequirementId = 602, UserId = LmsWorld.Member }
			});
			// A deleted requirement elsewhere does not make another department require it.
			_world.AcademyUpdates.Requirements.Add(new LmsRequirement
			{
				LmsRequirementId = 700, DepartmentId = 77, Name = "Old", Kind = (int)LmsRequirementKinds.Course, TargetRefId = course.LmsCourseId,
				AcademyUpdatePolicy = (int)LmsMajorChangePolicies.Reassign, DeletedOn = DateTime.UtcNow
			});

			var manifest = Clone("05-calls");
			manifest.Revision = 2;
			manifest.History.Add(new AcademyManifestRevision { Revision = 2, Release = "2027.01", Change = AcademyManifestRevision.Major, Policy = AcademyManifestRevision.PolicyReassign, ReassignDueDays = 14 });
			(await _world.Academy.SeedCourseAsync(AcademyCourseManifest.FromManifest(manifest), CancellationToken.None)).Should().BeTrue();
			course = _world.Courses.Rows.Single(c => c.SystemKey == "academy:05-calls");

			(await _world.AcademyUpdates.GetAssigningDepartmentIdsAsync(course.LmsCourseId, 0, 50)).Should().Equal(LmsWorld.DepartmentId);

			var communication = new Mock<ICommunicationService>();
			var updates = new AcademyManifestUpdateService(_world.AcademyUpdates, _world.Courses, _world.Versions, _world.Enrollments, _world.Departments.Object,
				Mock.Of<IDepartmentSettingsService>(), Mock.Of<IUserProfileService>(), new Lazy<ICommunicationService>(() => communication.Object), members.Object);
			await updates.ApplyPendingAsync(DateTime.UtcNow);

			_world.Enrollments.Rows.Should().NotContain(e => e.LmsCourseId == course.LmsCourseId && e.Source == (int)LmsEnrollmentSources.Reassigned, "the department chose notify");
			communication.Verify(x => x.SendNotificationAsync(It.IsIn(LmsWorld.Member, LmsWorld.Member2), LmsWorld.DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(),
				It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Exactly(2));

			// Next major revision: the department switches its requirement to re-assign; only the requirement's member is re-assigned, with its due days.
			_world.AcademyUpdates.Requirements.Single(r => r.LmsRequirementId == 601).AcademyUpdatePolicy = (int)LmsMajorChangePolicies.Reassign;
			manifest.Revision = 3;
			manifest.History.Add(new AcademyManifestRevision { Revision = 3, Release = "2027.02", Change = AcademyManifestRevision.Major, Policy = AcademyManifestRevision.PolicyNotify });
			(await _world.Academy.SeedCourseAsync(AcademyCourseManifest.FromManifest(manifest), CancellationToken.None)).Should().BeTrue();
			course = _world.Courses.Rows.Single(c => c.SystemKey == "academy:05-calls");
			var now = DateTime.UtcNow;
			await updates.ApplyPendingAsync(now);

			var reassigned = _world.Enrollments.Rows.Where(e => e.LmsCourseId == course.LmsCourseId && e.Source == (int)LmsEnrollmentSources.Reassigned).ToList();
			reassigned.Should().ContainSingle().Which.Should().Match<LmsEnrollment>(e => e.UserId == LmsWorld.Member && e.LmsCourseVersionId == course.CurrentVersionId &&
				e.DueOn.HasValue && e.DueOn.Value.Date == now.AddDays(21).Date);
		}

		[Test]
		public async Task A_moved_media_base_relinks_the_stored_videos_without_a_new_version()
		{
			await _world.Academy.EnsureSeededAsync();
			var versions = _world.Versions.Rows.Count;
			SystemBehaviorConfig.AcademyMediaUrl = "https://media.station.local/academy/";

			(await _world.Academy.EnsureSeededAsync()).Should().Be(0, "re-linking is not a new version");

			_world.Versions.Rows.Count.Should().Be(versions);
			var videos = _world.Lessons.Rows.Where(l => l.Kind == (int)LmsLessonKinds.Video).ToList();
			videos.Should().HaveCount(AcademyCourseManifest.Courses.Count).And.OnlyContain(l => l.VideoUrl.StartsWith("https://media.station.local/academy/"));
			videos.Should().OnlyContain(l => LmsCaptionTrack.Parse(l.CaptionTracksJson).All(t => t.Url.StartsWith("https://media.station.local/academy/")));
		}

		[Test]
		public void The_lesson_texts_come_from_the_manifests_and_the_shared_texts_from_the_resources()
		{
			_world.Academy.Text("05-calls.Title", "de").Should().Be(AcademyManifests.FindLesson("05-calls").Title["de"]);
			_world.Academy.Text("05-calls.q1.a", "fr").Should().Be(AcademyManifests.FindLesson("05-calls").Check[0].Options[0].Text["fr"]);
			_world.Academy.Text("Path.member", "en").Should().Be("Member / Responder");
			_world.Academy.Text("Check.Title", "en").Should().NotBeNullOrWhiteSpace();
		}

		[Test]
		public async Task Worker_88_runs_the_academy_update_step_and_counts_a_failure_without_stopping()
		{
			var result = new Resgrid.Workers.Framework.Logic.LmsSweepRunResult();
			var updates = new Mock<IAcademyManifestUpdateService>();
			updates.Setup(u => u.ApplyPendingAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(3);

			await Resgrid.Workers.Framework.Logic.LmsSweepLogic.ApplyAcademyUpdatesAsync(updates.Object, result, DateTime.UtcNow);
			result.AcademyUpdates.Should().Be(3);

			updates.Setup(u => u.ApplyPendingAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
			await Resgrid.Workers.Framework.Logic.LmsSweepLogic.ApplyAcademyUpdatesAsync(updates.Object, result, DateTime.UtcNow);
			result.AcademyUpdates.Should().Be(0);
			result.Failures.Should().Be(1);
		}

		#endregion Seeding
	}
}
