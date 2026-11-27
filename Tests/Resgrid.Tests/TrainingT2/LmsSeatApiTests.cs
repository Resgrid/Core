using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Models.v4.Lms;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using LmsController = Resgrid.Web.Services.Controllers.v4.LmsController;

namespace Resgrid.Tests.TrainingT2
{
	/// <summary>
	/// Round 10 A7 (owner decision 2026-11-26): the v4 learner endpoints that did not ask for a seat (Enroll, GetCourse, SaveProgress,
	/// CompleteLesson, StartAttempt, SubmitAttempt, CrewScan, the offline manifest) now refuse a member without one as "NotLicensed", through
	/// the LMS services, and GetCourse turns read-only (titles, kinds and the member's progress, no content). The A4 endpoints keep their
	/// refusal but ask the services' lapse-aware seat, so a member who held a seat keeps the lapse window (rule 4) on bearer clients too.
	/// Run over the real learner, assessment, crew and media services (LmsWorld).
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class LmsSeatApiTests
	{
		private const int D = LmsWorld.DepartmentId;

		private LmsWorld _world;
		private Mock<ILmsGateService> _gate;
		private Mock<ILmsAccessService> _access;
		private string _previousPassphrase, _previousApiBase;
		private int _previousSignedMinutes;

		[SetUp]
		public void SetUp()
		{
			_world = new LmsWorld();
			LmsWorld.ResetAcademySeedMemo();
			_gate = new Mock<ILmsGateService>();
			_gate.Setup(g => g.GetGateAsync(It.IsAny<int>())).ReturnsAsync(() => _world.Gate);
			_gate.Setup(g => g.GetActorAsync(D, It.IsAny<string>())).ReturnsAsync((int d, string u) => _world.Actor(u));
			_access = new Mock<ILmsAccessService>();
			_access.Setup(a => a.IsTrainingModuleOnAsync(It.IsAny<int>())).ReturnsAsync(true);
			_access.Setup(a => a.CanBeTrainedAsync(D, It.IsAny<string>())).ReturnsAsync((int d, string u) => _world.Gate.AddonActive == true && _world.SeatedMembers.Contains(u));

			_previousPassphrase = SystemBehaviorConfig.ExternalLinkUrlParamPassphrase;
			_previousApiBase = SystemBehaviorConfig.ResgridApiBaseUrl;
			_previousSignedMinutes = LmsConfig.SignedUrlMinutes;
			SystemBehaviorConfig.ExternalLinkUrlParamPassphrase = "round-10-a7-test-passphrase";
			SystemBehaviorConfig.ResgridApiBaseUrl = "https://api.example.test";
			LmsConfig.SignedUrlMinutes = 60;
			_world.Store.Setup(s => s.GetSignedUrl(It.IsAny<string>(), It.IsAny<DateTime>())).Returns((string key, DateTime until) => "https://store.example.test/" + key + "?signed=1");
		}

		[TearDown]
		public void TearDown()
		{
			SystemBehaviorConfig.ExternalLinkUrlParamPassphrase = _previousPassphrase;
			SystemBehaviorConfig.ResgridApiBaseUrl = _previousApiBase;
			LmsConfig.SignedUrlMinutes = _previousSignedMinutes;
			ApiClaims._httpContextAccessor = null;
			LmsWorld.ResetAcademySeedMemo();
		}

		private LmsController Controller(string userId = LmsWorld.Member)
		{
			var http = new DefaultHttpContext();
			// StartAttempt and CrewScan record the caller's address.
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, userId), new Claim(ClaimTypes.PrimaryGroupSid, D.ToString()) }, "test"));
			ApiClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			return new LmsController(_gate.Object, _world.Learner, _world.Assessment, _world.MediaService, _world.Crew, _world.Academy, _access.Object, Mock.Of<IAiKnowledgeService>(),
				Mock.Of<ILmsAppLaunchService>())
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
		}

		private static T Value<T>(ActionResult<T> result) where T : class => (T)((OkObjectResult)result.Result).Value;

		private async Task<LmsEnrollment> AssignAsync(string userId, int courseId, DateTime? due = null)
		{
			(await _world.Learner.AssignAsync(_world.AdminActor, courseId, new[] { new ContentAudienceRule(ContentAudienceKinds.User, userId) }, due)).Error.Should().Be(LmsErrors.None);
			return _world.Enrollments.Rows.Single(e => e.LmsCourseId == courseId && e.UserId == userId);
		}

		[Test]
		public void The_api_code_is_the_lms_error_name()
		{
			LmsController.NotLicensedError.Should().Be("NotLicensed").And.Be(LmsErrors.NotLicensed.ToString(), "Omni reads one code from every learner endpoint");
		}

		[Test]
		public async Task Enroll_refuses_a_member_without_a_seat()
		{
			var (course, _, _) = await _world.PublishedCourseAsync("Ladders", LmsWorld.Reading("One"));
			_world.SeatedMembers.Remove(LmsWorld.Member);

			var refused = Value(await Controller().Enroll(course.LmsCourseId, CancellationToken.None));
			refused.Error.Should().Be(LmsController.NotLicensedError);
			refused.Status.Should().Be("failure");
			_world.Enrollments.Rows.Should().BeEmpty();

			var enrolled = Value(await Controller(LmsWorld.Member2).Enroll(course.LmsCourseId, CancellationToken.None));
			enrolled.Error.Should().BeNull();
			enrolled.EnrollmentId.Should().NotBeNull();
		}

		[Test]
		public async Task GetCourse_is_read_only_without_a_seat()
		{
			var (course, _, lessons) = await _world.PublishedCourseAsync("Pumps", LmsWorld.Reading("One"), LmsWorld.Reading("Two"));
			var enrollment = await AssignAsync(LmsWorld.Member, course.LmsCourseId);
			Value(await Controller().CompleteLesson(new LmsCompleteLessonInput { EnrollmentId = enrollment.LmsEnrollmentId, LessonId = lessons[0].LmsLessonId }, CancellationToken.None))
				.Error.Should().BeNull();
			var seated = Value(await Controller().GetCourse(enrollment.LmsEnrollmentId));
			seated.Error.Should().BeNull();
			seated.Status.Should().Be("success");
			seated.Lessons[1].ContentHtml.Should().Contain("Two");

			_world.SeatedMembers.Remove(LmsWorld.Member);
			var readOnly = Value(await Controller().GetCourse(enrollment.LmsEnrollmentId));

			readOnly.Status.Should().Be("failure");
			readOnly.Error.Should().Be(LmsController.NotLicensedError);
			readOnly.Course.CanRun.Should().BeFalse();
			readOnly.Lessons.Select(l => l.Title).Should().Equal("One", "Two");
			readOnly.Lessons[0].Progress.Should().Be(LmsLessonProgressStatuses.Completed.ToString(), "the member keeps seeing their own progress");
			readOnly.Lessons.Should().OnlyContain(l => l.ContentHtml == null && l.DocumentUrl == null && l.VideoUrl == null && l.TranscriptText == null &&
				l.FreeResponsePrompt == null && l.FileRequestPrompt == null && !l.AvailableOffline && l.SurveyQuestions.Count == 0 && l.Captions.Count == 0);
			readOnly.CurrentLessonId.Should().BeNull();
		}

		[Test]
		public async Task Progress_completion_attempts_and_crew_scans_refuse_a_member_without_a_seat()
		{
			var bank = await _world.BankAsync(LmsWorld.Single("Nozzle?", "a", "a", "b"));
			var (course, _, lessons) = await _world.PublishedCourseAsync("Hose", LmsWorld.Reading("Read"), LmsWorld.Quiz(bank.LmsQuestionBankId, 100));
			var enrollment = await AssignAsync(LmsWorld.Member, course.LmsCourseId);
			Value(await Controller().CompleteLesson(new LmsCompleteLessonInput { EnrollmentId = enrollment.LmsEnrollmentId, LessonId = lessons[0].LmsLessonId }, CancellationToken.None))
				.Error.Should().BeNull();
			var open = Value(await Controller().StartAttempt(new LmsStartAttemptInput { EnrollmentId = enrollment.LmsEnrollmentId, LessonId = lessons[1].LmsLessonId }, CancellationToken.None));
			open.Error.Should().BeNull();
			var viewing = (LmsCrewViewing)(await _world.Crew.StartAsync(_world.AdminActor, course.LmsCourseId, lessons[0].LmsLessonId)).Value;

			_world.SeatedMembers.Remove(LmsWorld.Member);

			Value(await Controller().SaveProgress(new Resgrid.Web.Services.Models.v4.Lms.LmsProgressInput { EnrollmentId = enrollment.LmsEnrollmentId, LessonId = lessons[0].LmsLessonId, Seconds = 30 },
				CancellationToken.None)).Error.Should().Be(LmsController.NotLicensedError);
			Value(await Controller().CompleteLesson(new LmsCompleteLessonInput { EnrollmentId = enrollment.LmsEnrollmentId, LessonId = lessons[0].LmsLessonId }, CancellationToken.None))
				.Error.Should().Be(LmsController.NotLicensedError);
			var start = Value(await Controller().StartAttempt(new LmsStartAttemptInput { EnrollmentId = enrollment.LmsEnrollmentId, LessonId = lessons[1].LmsLessonId }, CancellationToken.None));
			start.Error.Should().Be(LmsController.NotLicensedError);
			start.Questions.Should().BeEmpty();
			var submit = Value(await Controller().SubmitAttempt(new LmsSubmitAttemptInput
			{
				AttemptId = open.AttemptId,
				Answers = _world.Answers(open.AttemptId, true).Select(a => new LmsAnswerInput { QuestionId = a.QuestionId, Keys = a.Keys }).ToList()
			}, CancellationToken.None));
			submit.Error.Should().Be(LmsController.NotLicensedError);
			_world.Attempts.Rows.Single(a => a.LmsAssessmentAttemptId == open.AttemptId).Status.Should().Be((int)LmsAttemptStatuses.InProgress);
			Value(await Controller().CrewScan(new LmsCrewScanInput { ViewingId = viewing.LmsCrewViewingId, Code = _world.Crew.CurrentCode(viewing, DateTime.UtcNow) }, CancellationToken.None))
				.Error.Should().Be(LmsController.NotLicensedError);
		}

		[Test]
		public async Task The_offline_manifest_is_empty_without_a_seat()
		{
			var asset = new LmsMediaAsset { DepartmentId = D, Status = (int)LmsMediaStatuses.Ready, HlsPrefix = "lms/31/9/hls/", Mp4Key = "lms/31/9/video.mp4", DurationSeconds = 60 };
			await _world.Media.InsertAsync(asset, CancellationToken.None);
			var video = new LmsLesson
			{
				Kind = (int)LmsLessonKinds.Video, Title = "Offline", IsRequired = true, VideoSource = (int)LmsVideoSources.Hosted, LmsMediaAssetId = asset.LmsMediaAssetId,
				AvailableOffline = true, OfflineMaxBytes = 10_000_000
			};
			var (course, _, _) = await _world.PublishedCourseAsync("Offline course", video);
			await AssignAsync(LmsWorld.Member, course.LmsCourseId);
			_world.Store.Setup(s => s.GetSizeAsync(asset.Mp4Key, It.IsAny<CancellationToken>())).ReturnsAsync(5_000_000L);

			Value(await Controller().GetOfflineManifest()).Data.Should().ContainSingle();

			_world.SeatedMembers.Remove(LmsWorld.Member);
			Value(await Controller().GetOfflineManifest()).Data.Should().BeEmpty("no hosted video leaves for a member without a seat");
		}

		[Test]
		public async Task During_a_lapse_a_member_who_held_a_seat_keeps_the_hosted_video_of_assigned_work()
		{
			var asset = new LmsMediaAsset { DepartmentId = D, Status = (int)LmsMediaStatuses.Ready, HlsPrefix = "lms/31/8/hls/", Mp4Key = "lms/31/8/video.mp4", DurationSeconds = 60 };
			await _world.Media.InsertAsync(asset, CancellationToken.None);
			var video = new LmsLesson { Kind = (int)LmsLessonKinds.Video, Title = "Lapse video", IsRequired = true, VideoSource = (int)LmsVideoSources.Hosted, LmsMediaAssetId = asset.LmsMediaAssetId };
			var (course, _, lessons) = await _world.PublishedCourseAsync("Lapsing video", video);
			var enrollment = await AssignAsync(LmsWorld.Member, course.LmsCourseId, DateTime.UtcNow.AddDays(20));
			enrollment.AssignedOn = DateTime.UtcNow.AddDays(-10);
			_world.Gate = new LmsDepartmentGate { DepartmentId = D, ModuleOn = true, FlagOn = true, AddonActive = false, LastEntitledOn = DateTime.UtcNow.AddDays(-5) };
			(await _access.Object.CanBeTrainedAsync(D, LmsWorld.Member)).Should().BeFalse("nobody is licensed for new LMS work during a lapse");

			var playlist = Value(await Controller().GetLessonPlaylist(enrollment.LmsEnrollmentId, lessons[0].LmsLessonId));

			playlist.Error.Should().BeNull("lapse rules 4 and 6 as built: the seat a member held stays through the lapse");
			playlist.Data.Mp4Url.Should().Be("https://store.example.test/lms/31/8/video.mp4?signed=1");

			_world.SeatedMembers.Remove(LmsWorld.Member);
			Value(await Controller().GetLessonPlaylist(enrollment.LmsEnrollmentId, lessons[0].LmsLessonId)).Error.Should().Be(LmsController.NotLicensedError);
		}

		[Test]
		public async Task The_members_own_finished_attempt_stays_readable_without_a_seat()
		{
			var bank = await _world.BankAsync(LmsWorld.Single("Nozzle?", "a", "a", "b"));
			var (course, _, lessons) = await _world.PublishedCourseAsync("Hose", LmsWorld.Quiz(bank.LmsQuestionBankId, 100));
			var enrollment = await AssignAsync(LmsWorld.Member, course.LmsCourseId);
			var open = Value(await Controller().StartAttempt(new LmsStartAttemptInput { EnrollmentId = enrollment.LmsEnrollmentId, LessonId = lessons[0].LmsLessonId }, CancellationToken.None));
			var done = Value(await Controller().SubmitAttempt(new LmsSubmitAttemptInput
			{
				AttemptId = open.AttemptId,
				Answers = _world.Answers(open.AttemptId, true).Select(a => new LmsAnswerInput { QuestionId = a.QuestionId, Keys = a.Keys }).ToList()
			}, CancellationToken.None));
			done.Passed.Should().BeTrue();

			_world.SeatedMembers.Remove(LmsWorld.Member);
			var result = Value(await Controller().GetAttemptResult(open.AttemptId));

			result.Error.Should().BeNull();
			result.Passed.Should().BeTrue();
			Value(await Controller().GetMyCourses()).Data.Should().ContainSingle(c => c.EnrollmentId == enrollment.LmsEnrollmentId && c.Status == "Completed",
				"completed records stay listed without a seat");
		}
	
		[Test]
		public async Task GetCourse_is_read_only_with_the_lapse_code_once_the_lapse_window_ends()
		{
			var survey = new LmsLesson
			{
				Kind = (int)LmsLessonKinds.Survey, Title = "Feedback", IsRequired = false,
				SurveyJson = Newtonsoft.Json.JsonConvert.SerializeObject(new[] { new LmsSurveyQuestion { Key = "q1", Kind = (int)LmsSurveyQuestionKinds.Rating, Text = "Rate" } })
			};
			var (course, _, lessons) = await _world.PublishedCourseAsync("Lapsing", LmsWorld.Reading("One"), survey);
			var enrollment = await AssignAsync(LmsWorld.Member, course.LmsCourseId, DateTime.UtcNow.AddDays(20));
			Value(await Controller().CompleteLesson(new LmsCompleteLessonInput { EnrollmentId = enrollment.LmsEnrollmentId, LessonId = lessons[0].LmsLessonId }, CancellationToken.None))
				.Error.Should().BeNull();
			enrollment.AssignedOn = DateTime.UtcNow.AddDays(-30);
			enrollment.DueOn = DateTime.UtcNow.AddDays(-1);
			_world.Gate = new LmsDepartmentGate { DepartmentId = D, ModuleOn = true, FlagOn = true, AddonActive = false, LastEntitledOn = DateTime.UtcNow.AddDays(-10) };

			var readOnly = Value(await Controller().GetCourse(enrollment.LmsEnrollmentId));

			readOnly.Status.Should().Be("failure");
			readOnly.Error.Should().Be(LmsErrors.LapseWindowEnded.ToString());
			readOnly.Lessons.Select(l => l.Title).Should().Equal("One", "Feedback");
			readOnly.Lessons[0].Progress.Should().Be(LmsLessonProgressStatuses.Completed.ToString(), "the member's own progress stays readable");
			readOnly.Lessons.Should().OnlyContain(l => l.ContentHtml == null && l.DocumentUrl == null && l.VideoUrl == null && l.TranscriptText == null &&
				l.FreeResponsePrompt == null && l.FileRequestPrompt == null && !l.AvailableOffline && l.SurveyQuestions.Count == 0 && l.Captions.Count == 0);
		}
}
}
