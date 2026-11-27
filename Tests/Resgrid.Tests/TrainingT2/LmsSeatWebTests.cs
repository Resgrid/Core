using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Courses;
using CoursesController = Resgrid.Web.Areas.User.Controllers.CoursesController;
using WebClaims = Resgrid.Web.Helpers.ClaimsAuthorizationHelper;

namespace Resgrid.Tests.TrainingT2
{
	/// <summary>
	/// Round 10 A7 (owner decision 2026-11-26): the web Trainings → Courses pages map the services' NotLicensed refusal to the member-facing
	/// "you need an LMS seat, ask your administrator" message (Courses resx Error.NotLicensed) on enrolment, My courses (the catalog and the
	/// locked card), the player, an open attempt and a submitted one. Run over the real LMS services (LmsWorld); the localizer echoes keys.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class LmsSeatWebTests
	{
		private const int D = LmsWorld.DepartmentId;
		private static readonly string[] Locales = { "en", "de", "es", "fr", "it", "pl", "sv", "uk", "el", "ar" };

		private LmsWorld _world;
		private Mock<ILmsGateService> _gate;
		private DefaultHttpContext _http;
		private IHttpContextAccessor _previousAccessor;

		[SetUp]
		public void SetUp()
		{
			_world = new LmsWorld();
			LmsWorld.ResetAcademySeedMemo();
			_gate = new Mock<ILmsGateService>();
			_gate.Setup(g => g.GetGateAsync(It.IsAny<int>())).ReturnsAsync(() => _world.Gate);
			_gate.Setup(g => g.GetActorAsync(D, It.IsAny<string>())).ReturnsAsync((int d, string u) => _world.Actor(u));
			_previousAccessor = WebClaims._httpContextAccessor;
		}

		[TearDown]
		public void TearDown()
		{
			WebClaims._httpContextAccessor = _previousAccessor;
			LmsWorld.ResetAcademySeedMemo();
		}

		private CoursesController Controller(string userId = LmsWorld.Member)
		{
			_http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, userId), new Claim(ClaimTypes.PrimaryGroupSid, D.ToString()) }, "Test"))
			};
			_http.Request.ContentType = "application/x-www-form-urlencoded";
			_http.Request.Form = new FormCollection(new Dictionary<string, StringValues>());
			WebClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };

			var localizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Courses.Courses>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			var trainings = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings>>();
			trainings.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			var access = new Mock<ILmsAccessService>();
			access.Setup(a => a.IsTrainingModuleOnAsync(D)).ReturnsAsync(true);

			return new CoursesController(_gate.Object, _world.CourseService, _world.BankService, _world.Learner, _world.Assessment, _world.MediaService, _world.Crew, _world.Academy,
				access.Object, Mock.Of<ILmsTrainingService>(), _world.Departments.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(),
				Mock.Of<ILearningRecordService>(), Mock.Of<ICertificationService>(), Mock.Of<IProtocolLibraryService>(), Mock.Of<IProtocolsService>(),
				Mock.Of<Resgrid.Model.Services.IAuthorizationService>(), localizer.Object, Mock.Of<ILmsCompletionRecordingService>(), Mock.Of<ITrainingCompletionRecordingService>(),
				trainings.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = _http },
				TempData = new TempDataDictionary(_http, Mock.Of<ITempDataProvider>())
			};
		}

		private async Task<LmsEnrollment> AssignAsync(string userId, int courseId)
		{
			(await _world.Learner.AssignAsync(_world.AdminActor, courseId, new[] { new ContentAudienceRule(ContentAudienceKinds.User, userId) }, null)).Error.Should().Be(LmsErrors.None);
			return _world.Enrollments.Rows.Single(e => e.LmsCourseId == courseId && e.UserId == userId);
		}

		[Test]
		public async Task Enrolling_without_a_seat_goes_back_to_my_courses_with_the_ask_your_administrator_message()
		{
			var (course, _, _) = await _world.PublishedCourseAsync("Ladders", LmsWorld.Reading("One"));
			_world.SeatedMembers.Remove(LmsWorld.Member);
			var controller = Controller();

			var result = await controller.Enroll(course.LmsCourseId, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("My");
			controller.TempData["CourseError"].Should().Be("Error.NotLicensed");
			_world.Enrollments.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task My_courses_locks_the_card_and_the_catalog_offer_with_the_reason()
		{
			var (assigned, _, _) = await _world.PublishedCourseAsync("Assigned", LmsWorld.Reading("One"));
			await AssignAsync(LmsWorld.Member, assigned.LmsCourseId);
			_world.SeatedMembers.Remove(LmsWorld.Member);

			var model = (MyCoursesModel)(await Controller().My()).Should().BeOfType<ViewResult>().Subject.Model;

			model.Courses.Should().ContainSingle().Which.BlockedBy.Should().Be(LmsErrors.NotLicensed);
			model.Courses.Single().CanRun.Should().BeFalse();
			model.Catalog.Should().ContainSingle().Which.BlockedBy.Should().Be(LmsErrors.NotLicensed, "the self-enrollable course says why there is no Enrol button");
			CourseLabels.Error(LmsErrors.NotLicensed).Should().Be("Error.NotLicensed");
		}

		[Test]
		public async Task The_player_shows_the_reason_and_runs_nothing()
		{
			var (course, _, lessons) = await _world.PublishedCourseAsync("Pumps", LmsWorld.Reading("One"));
			var enrollment = await AssignAsync(LmsWorld.Member, course.LmsCourseId);
			_world.SeatedMembers.Remove(LmsWorld.Member);
			var controller = Controller();

			var model = (PlayerModel)(await controller.Play(enrollment.LmsEnrollmentId)).Should().BeOfType<ViewResult>().Subject.Model;
			model.Player.Error.Should().Be(LmsErrors.NotLicensed);
			model.Error.Should().Be("Error.NotLicensed");
			model.Player.Lesson.Should().BeNull();
			model.HlsUrl.Should().BeNull();

			var progress = (JsonResult)await controller.Progress(enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, 15, null, null, CancellationToken.None);
			progress.Value.ToString().Should().Contain("NotLicensed");
			_world.Progress.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task An_open_attempt_and_its_submission_go_back_to_the_player_without_a_seat()
		{
			var bank = await _world.BankAsync(LmsWorld.Single("Nozzle?", "a", "a", "b"));
			var (course, _, lessons) = await _world.PublishedCourseAsync("Hose", LmsWorld.Quiz(bank.LmsQuestionBankId, 100));
			var enrollment = await AssignAsync(LmsWorld.Member, course.LmsCourseId);
			var open = await _world.Assessment.StartAttemptAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, false, null, null, null);
			open.Error.Should().Be(LmsErrors.None);
			_world.SeatedMembers.Remove(LmsWorld.Member);

			var controller = Controller();
			var shown = (await controller.Attempt(open.Attempt.LmsAssessmentAttemptId)).Should().BeOfType<RedirectToActionResult>().Subject;
			shown.ActionName.Should().Be("Play");
			shown.RouteValues["id"].Should().Be(enrollment.LmsEnrollmentId);
			controller.TempData["CourseError"].Should().Be("Error.NotLicensed");

			controller = Controller();
			var submitted = (await controller.SubmitAttempt(open.Attempt.LmsAssessmentAttemptId, CancellationToken.None)).Should().BeOfType<RedirectToActionResult>().Subject;
			submitted.ActionName.Should().Be("Play");
			controller.TempData["CourseError"].Should().Be("Error.NotLicensed");
			_world.Attempts.Rows.Single().Status.Should().Be((int)LmsAttemptStatuses.InProgress);
		}

		private static string LocalizationRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Core", "Resgrid.Localization")))
				directory = directory.Parent;
			directory.Should().NotBeNull("the repository root holds Core/Resgrid.Localization");
			return Path.Combine(directory.FullName, "Core", "Resgrid.Localization");
		}

		[TestCase("Areas/User/Courses/Courses", "Error.NotLicensed")]
		[TestCase("Areas/User/LmsClasses/LmsClasses", "lms_class_no_seat")]
		[TestCase("Areas/User/LmsStandards/LmsStandards", "Std.Error.NotLicensed")]
		[TestCase("Areas/User/Courses/Courses", "AssignedNotLicensedMessage")]
		[TestCase("Areas/User/LmsPrograms/LmsPrograms", "AssignedNotLicensedFormat")]
		public void The_seat_refusal_is_translated_in_every_locale(string area, string key)
		{
			var root = LocalizationRoot();
			string english = null;
			foreach (var locale in Locales)
			{
				var path = Path.Combine(root, (area + "." + locale + ".resx").Replace('/', Path.DirectorySeparatorChar));
				var data = XDocument.Load(path).Root.Elements("data").SingleOrDefault(e => (string)e.Attribute("name") == key);
				data.Should().NotBeNull(key + " in " + locale);
				var value = (string)data.Element("value");
				value.Should().NotBeNullOrWhiteSpace();
				if (locale == "en")
				{
					english = value;
					value.Should().Contain("LMS seat");
					continue;
				}
				value.Should().NotBe(english, locale + " is translated");
				((string)data.Element("comment")).Should().Contain("machine-translated", locale);
			}
		}
	
		[Test]
		public async Task The_assignment_summary_says_how_many_were_not_enrolled_for_want_of_a_seat()
		{
			var (course, _, _) = await _world.PublishedCourseAsync("Ladders", LmsWorld.Reading("One"));
			_gate.Setup(g => g.GetActorAsync(D, LmsWorld.Admin)).ReturnsAsync(() => _world.AdminActor);
			_world.SeatedMembers.Remove(LmsWorld.Member2);
			var controller = Controller(LmsWorld.Admin);

			var result = await controller.Assign(course.LmsCourseId, new CourseWhoInput { Groups = new List<int> { 3 } }, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Course");
			controller.TempData["CourseMessage"].Should().Be("AssignedMessage AssignedNotLicensedMessage", "the summary names the members left out for want of a seat");
			_world.Enrollments.Rows.Should().ContainSingle(e => e.UserId == LmsWorld.Member);

			_world.SeatedMembers.Add(LmsWorld.Member2);
			controller = Controller(LmsWorld.Admin);
			await controller.Assign(course.LmsCourseId, new CourseWhoInput { Members = new List<string> { LmsWorld.Member2 } }, CancellationToken.None);
			controller.TempData["CourseMessage"].Should().Be("AssignedMessage", "nothing to add when everyone was seated");
		}
}
}
