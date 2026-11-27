using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Lms;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using LmsController = Resgrid.Web.Services.Controllers.v4.LmsController;

namespace Resgrid.Tests.TrainingT2
{
	/// <summary>
	/// Round 10 (Omni Responder): the v4 LMS endpoints the app could not build without. The member's access answer, the self-enrollable
	/// catalog, hosted video for bearer clients (signed, short-lived, enrollment-bound playlist and caption links that run the player's rules
	/// again as the enrollment's learner), the member's own uploaded lesson files, surveys, and the lesson fields (protocol version, files,
	/// survey questions, captions). Department LMS items need the member's LMS seat; the Trainings module switch refuses everything.
	/// Run over the real learner and media services (LmsWorld).
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class LmsOmniApiTests
	{
		private const int D = LmsWorld.DepartmentId;

		private LmsWorld _world;
		private Mock<ILmsGateService> _gate;
		private Mock<ILmsAccessService> _access;
		private Mock<IAiKnowledgeService> _knowledge;
		private bool _moduleOn;
		private HashSet<string> _seated;
		private string _user;
		private string _previousPassphrase, _previousApiBase;
		private int _previousSignedMinutes;

		[SetUp]
		public void SetUp()
		{
			_world = new LmsWorld();
			LmsWorld.ResetAcademySeedMemo();
			_moduleOn = true;
			_seated = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { LmsWorld.Member, LmsWorld.Member2 };
			// Round 10 A7: the learner services ask the gate for the seat (lapse-aware); the access mock answers GetMyAccess / GetCatalog.
			_world.SeatedMembers = _seated;
			_user = LmsWorld.Member;

			_gate = new Mock<ILmsGateService>();
			_gate.Setup(g => g.GetGateAsync(It.IsAny<int>())).ReturnsAsync(() => _world.Gate);
			_gate.Setup(g => g.GetActorAsync(D, It.IsAny<string>())).ReturnsAsync((int d, string u) => _world.Actor(u));
			_access = new Mock<ILmsAccessService>();
			_access.Setup(a => a.IsTrainingModuleOnAsync(It.IsAny<int>())).ReturnsAsync(() => _moduleOn);
			_access.Setup(a => a.CanBeTrainedAsync(D, It.IsAny<string>())).ReturnsAsync((int d, string u) => _world.Gate.FlagOn && _seated.Contains(u));
			_knowledge = new Mock<IAiKnowledgeService>();

			_previousPassphrase = SystemBehaviorConfig.ExternalLinkUrlParamPassphrase;
			_previousApiBase = SystemBehaviorConfig.ResgridApiBaseUrl;
			_previousSignedMinutes = LmsConfig.SignedUrlMinutes;
			SystemBehaviorConfig.ExternalLinkUrlParamPassphrase = "round-10-test-passphrase";
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

		/// <summary>The controller as <paramref name="userId"/> calls it with a bearer token (null: an anonymous signed-link request).</summary>
		private LmsController Controller(string userId = LmsWorld.Member)
		{
			var http = new DefaultHttpContext();
			if (userId != null)
				http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, userId), new Claim(ClaimTypes.PrimaryGroupSid, D.ToString()) }, "test"));
			ApiClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			return new LmsController(_gate.Object, _world.Learner, _world.Assessment, _world.MediaService, _world.Crew, _world.Academy, _access.Object, _knowledge.Object,
				Mock.Of<ILmsAppLaunchService>())
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
		}

		private static T Value<T>(ActionResult<T> result) where T : class => (T)((OkObjectResult)result.Result).Value;

		private async Task<LmsEnrollment> EnrollAsync(string userId, int courseId)
		{
			var result = await _world.Learner.EnrollSelfAsync(_world.Actor(userId), courseId);
			result.Succeeded.Should().BeTrue(result.Error.ToString());
			return result.Enrollment;
		}

		private static Dictionary<string, string> Query(string url)
		{
			var query = new Uri(url).Query.TrimStart('?');
			return query.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2))
				.ToDictionary(p => p[0], p => Uri.UnescapeDataString(p.Length > 1 ? p[1] : string.Empty), StringComparer.Ordinal);
		}

		private static string Reading => "<p>Read</p>";

		#region Policies and shapes

		[TestCase(nameof(LmsController.GetMyAccess))]
		[TestCase(nameof(LmsController.GetCatalog))]
		[TestCase(nameof(LmsController.GetLessonPlaylist))]
		[TestCase(nameof(LmsController.GetLessonCaptions))]
		[TestCase(nameof(LmsController.GetLessonFile))]
		[TestCase(nameof(LmsController.SubmitSurvey))]
		public void Every_new_bearer_endpoint_needs_training_view(string action)
		{
			var method = typeof(LmsController).GetMethod(action)!;
			method.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).Should().ContainSingle().Which.Should().Be(ResgridResources.Training_View);
			method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
		}

		[Test]
		public void Only_the_two_signed_media_routes_are_anonymous()
		{
			typeof(LmsController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
				.Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null).Select(m => m.Name)
				.Should().BeEquivalentTo(nameof(LmsController.HostedPlaylist), nameof(LmsController.HostedCaptions));
			typeof(LmsController).GetMethod(nameof(LmsController.HostedPlaylist))!.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().EndWith(".m3u8");
		}

		#endregion Policies and shapes

		#region GetMyAccess

		[Test]
		public async Task GetMyAccess_answers_while_the_module_is_off_with_everything_false_and_asks_nothing_else()
		{
			_moduleOn = false;

			var data = Value(await Controller().GetMyAccess()).Data;

			data.TrainingModuleOn.Should().BeFalse();
			data.LmsFlagOn.Should().BeFalse();
			data.AddonActive.Should().BeNull();
			data.CanBeTrained.Should().BeFalse();
			data.EnhancedAiActive.Should().BeFalse();
			_gate.Verify(g => g.GetGateAsync(It.IsAny<int>()), Times.Never);
			_access.Verify(a => a.CanBeTrainedAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task GetMyAccess_reports_the_gate_the_seat_and_enhanced_ai()
		{
			_knowledge.Setup(k => k.IsEntitledAsync(D)).ReturnsAsync(true);

			var data = Value(await Controller().GetMyAccess()).Data;
			data.TrainingModuleOn.Should().BeTrue();
			data.LmsFlagOn.Should().BeTrue();
			data.AddonActive.Should().BeTrue();
			data.IsLapsed.Should().BeFalse();
			data.CanBeTrained.Should().BeTrue();
			data.EnhancedAiActive.Should().BeTrue();

			_seated.Remove(LmsWorld.Member);
			Value(await Controller().GetMyAccess()).Data.CanBeTrained.Should().BeFalse("no LMS seat");

			_world.Gate.AddonActive = false;
			_world.Gate.LastEntitledOn = DateTime.UtcNow.AddDays(-3);
			var lapsed = Value(await Controller().GetMyAccess()).Data;
			lapsed.AddonActive.Should().BeFalse();
			lapsed.IsLapsed.Should().BeTrue();
		}

		[Test]
		public async Task GetMyAccess_with_the_flag_off_has_no_addon_answer_and_no_seat()
		{
			_world.Gate.FlagOn = false;
			_world.Gate.AddonActive = null;
			_knowledge.Setup(k => k.IsEntitledAsync(D)).ThrowsAsync(new InvalidOperationException("billing down"));

			var data = Value(await Controller().GetMyAccess()).Data;

			data.TrainingModuleOn.Should().BeTrue();
			data.LmsFlagOn.Should().BeFalse();
			data.AddonActive.Should().BeNull();
			data.CanBeTrained.Should().BeFalse();
			data.EnhancedAiActive.Should().BeFalse("an entitlement that cannot be read is not active");
			_access.Verify(a => a.CanBeTrainedAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		#endregion GetMyAccess

		#region GetCatalog

		[Test]
		public async Task GetCatalog_lists_self_enrollable_courses_with_the_version_and_the_open_enrollment()
		{
			var (course, version, _) = await _world.PublishedCourseAsync("Ladders", LmsWorld.Reading("One"));
			var stored = _world.Courses.Rows.Single(c => c.LmsCourseId == course.LmsCourseId);
			stored.Summary = "Raising and climbing";

			var entry = Value(await Controller().GetCatalog()).Data.Should().ContainSingle().Subject;
			entry.CourseId.Should().Be(course.LmsCourseId);
			entry.CourseVersionId.Should().Be(version.LmsCourseVersionId);
			entry.Title.Should().Be("Ladders");
			entry.Summary.Should().Be("Raising and climbing");
			entry.Hours.Should().Be(2m);
			entry.VersionLabel.Should().Be("1.0");
			entry.CanEnroll.Should().BeTrue();
			entry.OpenEnrollmentId.Should().BeNull();

			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);
			var after = Value(await Controller().GetCatalog()).Data.Single();
			after.CanEnroll.Should().BeFalse("one open enrollment per course");
			after.OpenEnrollmentId.Should().Be(enrollment.LmsEnrollmentId);
		}

		[Test]
		public async Task GetCatalog_is_empty_without_a_seat_or_with_the_lms_dark_and_404_with_the_module_off()
		{
			await _world.PublishedCourseAsync("Ladders", LmsWorld.Reading("One"));

			_seated.Clear();
			Value(await Controller().GetCatalog()).Data.Should().BeEmpty("a member without an LMS seat keeps the free baseline only");

			_seated.Add(LmsWorld.Member);
			_world.Gate.FlagOn = false;
			Value(await Controller().GetCatalog()).Data.Should().BeEmpty();

			_moduleOn = false;
			(await Controller().GetCatalog()).Result.Should().BeOfType<NotFoundResult>();
		}

		#endregion GetCatalog

		#region Lesson fields

		[Test]
		public async Task GetCourse_lessons_carry_the_protocol_version_survey_questions_captions_and_the_members_own_files()
		{
			var upload = new LmsLesson { Kind = (int)LmsLessonKinds.Blocks, Title = "Upload", ContentHtml = Reading, IsRequired = true, FileRequestPrompt = "Your card" };
			var protocol = new LmsLesson { Kind = (int)LmsLessonKinds.ProtocolVersion, Title = "SOP", IsRequired = false, ProtocolVersionId = 55 };
			var survey = new LmsLesson
			{
				Kind = (int)LmsLessonKinds.Survey, Title = "Feedback", IsRequired = false,
				SurveyJson = JsonConvert.SerializeObject(new[]
				{
					new LmsSurveyQuestion { Key = "q1", Kind = (int)LmsSurveyQuestionKinds.Rating, Text = "Rate it" },
					new LmsSurveyQuestion { Key = "q2", Kind = (int)LmsSurveyQuestionKinds.Choice, Text = "Pace", Options = new List<string> { "Slow", "Right", "Fast" } }
				})
			};
			var video = new LmsLesson
			{
				Kind = (int)LmsLessonKinds.Video, Title = "Video", IsRequired = false, VideoSource = (int)LmsVideoSources.Hosted,
				CaptionTracksJson = JsonConvert.SerializeObject(new[]
				{
					new LmsCaptionTrack { Locale = "en", Label = "English", ObjectKey = "lms/31/1/captions/en.vtt" },
					new LmsCaptionTrack { Locale = "fr", Label = "Français", Url = "https://academy.example.test/fr.vtt" },
					new LmsCaptionTrack { Locale = "de", Label = "Deutsch", Url = "http://insecure.example.test/de.vtt" }
				})
			};
			var (course, _, lessons) = await _world.PublishedCourseAsync("Fields", upload, protocol, survey, video);
			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);

			var completed = Value(await Controller().CompleteLesson(new LmsCompleteLessonInput
			{
				EnrollmentId = enrollment.LmsEnrollmentId, LessonId = lessons[0].LmsLessonId, FileName = "card.pdf", FileContentType = "application/pdf",
				FileData = Convert.ToBase64String(Encoding.UTF8.GetBytes("%PDF-1.4 card"))
			}, CancellationToken.None));
			completed.Error.Should().BeNull();
			// Another member's upload on the same lesson never shows.
			await _world.Files.InsertAsync(new LmsLessonFile { DepartmentId = D, LmsLessonId = lessons[0].LmsLessonId, UserId = LmsWorld.Member2, FileName = "theirs.pdf", ContentType = "application/pdf", SizeBytes = 3, Data = new byte[3] }, CancellationToken.None);

			var result = Value(await Controller().GetCourse(enrollment.LmsEnrollmentId));
			var file = result.Lessons[0].Files.Should().ContainSingle().Subject;
			file.FileName.Should().Be("card.pdf");
			file.ContentType.Should().Be("application/pdf");
			file.Size.Should().Be(Encoding.UTF8.GetBytes("%PDF-1.4 card").Length);
			file.LessonFileId.Should().BeGreaterThan(0);

			result.Lessons[1].ProtocolVersionId.Should().Be(55);
			result.Lessons.Where((l, i) => i != 1).Should().OnlyContain(l => l.ProtocolVersionId == null);

			var questions = result.Lessons[2].SurveyQuestions;
			questions.Select(q => q.Key).Should().Equal("q1", "q2");
			questions[0].Kind.Should().Be("Rating");
			questions[1].Kind.Should().Be("Choice");
			questions[1].Options.Should().Equal("Slow", "Right", "Fast");
			result.Lessons[0].SurveyQuestions.Should().BeEmpty();

			var captions = result.Lessons[3].Captions;
			captions.Select(c => c.Locale).Should().Equal("en", "fr", "de");
			captions[0].Url.Should().BeNull("an uploaded track is read through the API, never by its store key");
			captions[1].Url.Should().Be("https://academy.example.test/fr.vtt");
			captions[2].Url.Should().BeNull("only https links pass through");
			JsonConvert.SerializeObject(result).Should().NotContain("lms/31/1/captions");
		}

		#endregion Lesson fields

		#region Hosted video

		private async Task<(LmsEnrollment Enrollment, LmsLesson Video, LmsMediaAsset Asset)> HostedVideoAsync(bool lockedBehindReading = false, int status = (int)LmsMediaStatuses.Ready)
		{
			var asset = new LmsMediaAsset { DepartmentId = D, Status = status, HlsPrefix = "lms/31/7/hls/", Mp4Key = "lms/31/7/video.mp4", DurationSeconds = 95 };
			await _world.Media.InsertAsync(asset, CancellationToken.None);
			var video = new LmsLesson
			{
				Kind = (int)LmsLessonKinds.Video, Title = "Hosted", IsRequired = true, VideoSource = (int)LmsVideoSources.Hosted, LmsMediaAssetId = asset.LmsMediaAssetId,
				CaptionTracksJson = JsonConvert.SerializeObject(new[]
				{
					new LmsCaptionTrack { Locale = "en", Label = "English", ObjectKey = "lms/31/7/captions/en.vtt" },
					new LmsCaptionTrack { Locale = "fr", Label = "Français", Url = "https://academy.example.test/fr.vtt" }
				})
			};
			var lessons = lockedBehindReading ? new[] { LmsWorld.Reading("First"), video } : new[] { video };
			var (course, _, stored) = await _world.PublishedCourseAsync("Video course", lessons);
			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);
			_world.Store.Setup(s => s.ReadAllAsync("lms/31/7/hls/index.m3u8", It.IsAny<CancellationToken>()))
				.ReturnsAsync(Encoding.UTF8.GetBytes("#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXTINF:6.0,\nseg0.ts\n#EXTINF:6.0,\n../escape.ts\n#EXT-X-ENDLIST\n"));
			_world.Store.Setup(s => s.ReadAllAsync("lms/31/7/captions/en.vtt", It.IsAny<CancellationToken>()))
				.ReturnsAsync(Encoding.UTF8.GetBytes("WEBVTT\n\n00:00.000 --> 00:02.000\nHello crew"));
			return (enrollment, stored.Single(l => l.Kind == (int)LmsLessonKinds.Video), asset);
		}

		[Test]
		public async Task GetLessonPlaylist_hands_out_signed_enrollment_bound_links_and_the_mp4()
		{
			var (enrollment, video, _) = await HostedVideoAsync();

			var result = Value(await Controller().GetLessonPlaylist(enrollment.LmsEnrollmentId, video.LmsLessonId));

			result.Error.Should().BeNull();
			result.Status.Should().Be("success");
			var data = result.Data;
			data.EnrollmentId.Should().Be(enrollment.LmsEnrollmentId);
			data.LessonId.Should().Be(video.LmsLessonId);
			data.DurationSeconds.Should().Be(95);
			data.Mp4Url.Should().Be("https://store.example.test/lms/31/7/video.mp4?signed=1");
			data.UrlExpiresOn.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(60), TimeSpan.FromMinutes(1));
			data.PlaylistUrl.Should().StartWith("https://api.example.test/api/v4/Lms/HostedPlaylist.m3u8?");
			data.PlaylistUrl.Should().NotContain(LmsWorld.Member, "no user id travels in the link");
			var query = Query(data.PlaylistUrl);
			query["d"].Should().Be(D.ToString());
			query["en"].Should().Be(enrollment.LmsEnrollmentId.ToString());
			query["ls"].Should().Be(video.LmsLessonId.ToString());

			data.Captions.Select(c => c.Locale).Should().Equal("en", "fr");
			data.Captions[0].Url.Should().StartWith("https://api.example.test/api/v4/Lms/HostedCaptions.vtt?").And.Contain("loc=en");
			data.Captions[1].Url.Should().Be("https://academy.example.test/fr.vtt");
		}

		[Test]
		public async Task GetLessonPlaylist_refuses_another_members_enrollment_a_non_video_lesson_and_the_module_off()
		{
			var (enrollment, video, _) = await HostedVideoAsync();

			(await Controller(LmsWorld.Member2).GetLessonPlaylist(enrollment.LmsEnrollmentId, video.LmsLessonId)).Result.Should().BeOfType<NotFoundResult>();
			(await Controller().GetLessonPlaylist(enrollment.LmsEnrollmentId, video.LmsLessonId + 999)).Result.Should().BeOfType<NotFoundResult>();

			var (course, _, lessons) = await _world.PublishedCourseAsync("Reading only", LmsWorld.Reading("Only"));
			var reading = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);
			(await Controller().GetLessonPlaylist(reading.LmsEnrollmentId, lessons[0].LmsLessonId)).Result.Should().BeOfType<NotFoundResult>();

			_moduleOn = false;
			(await Controller().GetLessonPlaylist(enrollment.LmsEnrollmentId, video.LmsLessonId)).Result.Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task GetLessonPlaylist_says_why_it_will_not_play()
		{
			var (locked, lockedVideo, _) = await HostedVideoAsync(lockedBehindReading: true);
			Value(await Controller().GetLessonPlaylist(locked.LmsEnrollmentId, lockedVideo.LmsLessonId)).Error.Should().Be("LessonLocked");

			_world = new LmsWorld();
			_world.SeatedMembers = _seated;
			_world.Store.Setup(s => s.GetSignedUrl(It.IsAny<string>(), It.IsAny<DateTime>())).Returns((string key, DateTime until) => "https://store.example.test/" + key);
			var (queued, queuedVideo, _) = await HostedVideoAsync(status: (int)LmsMediaStatuses.Queued);
			var notReady = Value(await Controller().GetLessonPlaylist(queued.LmsEnrollmentId, queuedVideo.LmsLessonId));
			notReady.Error.Should().Be("MediaUnavailable");
			notReady.Status.Should().Be("failure");
			notReady.Data.Should().BeNull();

			_seated.Remove(LmsWorld.Member);
			Value(await Controller().GetLessonPlaylist(queued.LmsEnrollmentId, queuedVideo.LmsLessonId)).Error.Should().Be(LmsController.NotLicensedError);

			_seated.Add(LmsWorld.Member);
			_world.Gate.AddonActive = false;
			_world.Gate.LastEntitledOn = DateTime.UtcNow.AddDays(-200);
			Value(await Controller().GetLessonPlaylist(queued.LmsEnrollmentId, queuedVideo.LmsLessonId)).Error.Should().Be("LapseWindowEnded");
		}

		[Test]
		public async Task GetLessonPlaylist_issues_no_link_without_the_passphrase()
		{
			var (enrollment, video, _) = await HostedVideoAsync();
			SystemBehaviorConfig.ExternalLinkUrlParamPassphrase = null;

			Value(await Controller().GetLessonPlaylist(enrollment.LmsEnrollmentId, video.LmsLessonId)).Error.Should().Be("MediaUnavailable");
		}

		private async Task<ActionResult> PlaylistFromAsync(string url)
		{
			var q = Query(url);
			return await Controller(null).HostedPlaylist(q["d"], q["en"], q["ls"], q["e"], q["sig"]);
		}

		[Test]
		public async Task HostedPlaylist_serves_the_playlist_with_signed_segments_without_a_bearer_token()
		{
			var (enrollment, video, _) = await HostedVideoAsync();
			var url = Value(await Controller().GetLessonPlaylist(enrollment.LmsEnrollmentId, video.LmsLessonId)).Data.PlaylistUrl;

			var content = (await PlaylistFromAsync(url)).Should().BeOfType<ContentResult>().Subject;

			content.ContentType.Should().Be("application/vnd.apple.mpegurl");
			content.Content.Should().Contain("#EXTM3U").And.Contain("https://store.example.test/lms/31/7/hls/seg0.ts?signed=1");
			content.Content.Should().NotContain("escape.ts", "a segment line that leaves the prefix is dropped");
		}

		[Test]
		public async Task HostedPlaylist_refuses_a_forged_expired_withdrawn_or_unlicensed_link()
		{
			var (enrollment, video, _) = await HostedVideoAsync();
			var url = Value(await Controller().GetLessonPlaylist(enrollment.LmsEnrollmentId, video.LmsLessonId)).Data.PlaylistUrl;
			var q = Query(url);

			(await Controller(null).HostedPlaylist(q["d"], q["en"], (video.LmsLessonId + 1).ToString(), q["e"], q["sig"])).Should().BeOfType<NotFoundResult>("another lesson");
			(await Controller(null).HostedPlaylist((D + 1).ToString(), q["en"], q["ls"], q["e"], q["sig"])).Should().BeOfType<NotFoundResult>("another department");
			(await Controller(null).HostedPlaylist(q["d"], q["en"], q["ls"], (long.Parse(q["e"]) + 30).ToString(), q["sig"])).Should().BeOfType<NotFoundResult>("a moved expiry");
			(await Controller(null).HostedPlaylist(q["d"], q["en"], q["ls"], q["e"], null)).Should().BeOfType<NotFoundResult>();

			var expired = LmsMediaLinks.Build(LmsMediaLinks.PlaylistKind, D, enrollment.LmsEnrollmentId, video.LmsLessonId, null, DateTime.UtcNow.AddMinutes(-1));
			(await PlaylistFromAsync(expired)).Should().BeOfType<NotFoundResult>("expired");

			_seated.Remove(LmsWorld.Member);
			(await PlaylistFromAsync(url)).Should().BeOfType<NotFoundResult>("the learner lost the LMS seat");
			_seated.Add(LmsWorld.Member);

			_moduleOn = false;
			(await PlaylistFromAsync(url)).Should().BeOfType<NotFoundResult>("the Trainings module is off");
			_moduleOn = true;

			var stored = _world.Enrollments.Rows.Single(e => e.LmsEnrollmentId == enrollment.LmsEnrollmentId);
			stored.Status = (int)LmsEnrollmentStatuses.Withdrawn;
			stored.IsOpen = false;
			(await PlaylistFromAsync(url)).Should().BeOfType<NotFoundResult>("the enrollment was withdrawn");
		}

		[Test]
		public async Task HostedCaptions_serves_the_signed_track_and_only_that_locale()
		{
			var (enrollment, video, _) = await HostedVideoAsync();
			var url = Value(await Controller().GetLessonPlaylist(enrollment.LmsEnrollmentId, video.LmsLessonId)).Data.Captions[0].Url;
			var q = Query(url);

			var content = (await Controller(null).HostedCaptions(q["d"], q["en"], q["ls"], q["loc"], q["e"], q["sig"])).Should().BeOfType<ContentResult>().Subject;
			content.ContentType.Should().Be("text/vtt");
			content.Content.Should().StartWith("WEBVTT").And.Contain("Hello crew");

			(await Controller(null).HostedCaptions(q["d"], q["en"], q["ls"], "fr", q["e"], q["sig"])).Should().BeOfType<NotFoundResult>("the signature names the locale");
			var playlistQuery = Query(Value(await Controller().GetLessonPlaylist(enrollment.LmsEnrollmentId, video.LmsLessonId)).Data.PlaylistUrl);
			(await Controller(null).HostedCaptions(playlistQuery["d"], playlistQuery["en"], playlistQuery["ls"], "en", playlistQuery["e"], playlistQuery["sig"]))
				.Should().BeOfType<NotFoundResult>("a playlist link is not a caption link");
		}

		[Test]
		public async Task GetLessonCaptions_reads_an_uploaded_track_of_the_members_own_enrollment()
		{
			var (enrollment, video, _) = await HostedVideoAsync();

			var content = (await Controller().GetLessonCaptions(enrollment.LmsEnrollmentId, video.LmsLessonId, "EN")).Should().BeOfType<ContentResult>().Subject;
			content.ContentType.Should().Be("text/vtt");
			content.Content.Should().Contain("Hello crew");

			(await Controller().GetLessonCaptions(enrollment.LmsEnrollmentId, video.LmsLessonId, "fr")).Should().BeOfType<NotFoundResult>("an Academy track is a public URL, not an upload");
			(await Controller(LmsWorld.Member2).GetLessonCaptions(enrollment.LmsEnrollmentId, video.LmsLessonId, "en")).Should().BeOfType<NotFoundResult>();
			_seated.Remove(LmsWorld.Member);
			(await Controller().GetLessonCaptions(enrollment.LmsEnrollmentId, video.LmsLessonId, "en")).Should().BeOfType<NotFoundResult>();
		}

		#endregion Hosted video

		#region Lesson files

		private async Task<(LmsEnrollment Enrollment, LmsLesson Lesson)> FileRequestAsync()
		{
			var upload = new LmsLesson { Kind = (int)LmsLessonKinds.Blocks, Title = "Upload", ContentHtml = Reading, IsRequired = true, FileRequestPrompt = "Your card" };
			var (course, _, lessons) = await _world.PublishedCourseAsync("Files", upload);
			return (await EnrollAsync(LmsWorld.Member, course.LmsCourseId), lessons[0]);
		}

		[Test]
		public async Task CompleteLesson_takes_a_requested_file_as_base64_and_checks_it()
		{
			var (enrollment, lesson) = await FileRequestAsync();
			LmsCompleteLessonInput Input(string data, string type = "application/pdf") => new LmsCompleteLessonInput
			{
				EnrollmentId = enrollment.LmsEnrollmentId, LessonId = lesson.LmsLessonId, FileName = "card.pdf", FileContentType = type, FileData = data
			};

			Value(await Controller().CompleteLesson(Input(null), CancellationToken.None)).Error.Should().Be("ResponseRequired");
			Value(await Controller().CompleteLesson(Input("not base64!"), CancellationToken.None)).Error.Should().Be("InvalidInput");
			Value(await Controller().CompleteLesson(Input(Convert.ToBase64String(new byte[LmsLessonFile.MaxBytes + 1])), CancellationToken.None)).Error.Should().Be("FileTooLarge");
			Value(await Controller().CompleteLesson(Input(Convert.ToBase64String(new byte[] { 1, 2 }), "application/x-msdownload"), CancellationToken.None)).Error.Should().Be("UnsupportedFile");

			var done = Value(await Controller().CompleteLesson(Input(Convert.ToBase64String(new byte[] { 37, 80, 68, 70 })), CancellationToken.None));
			done.Error.Should().BeNull();
			done.CourseCompleted.Should().BeTrue();
			_world.Files.Rows.Should().ContainSingle(f => f.UserId == LmsWorld.Member && f.FileName == "card.pdf");
		}

		[Test]
		public async Task GetLessonFile_returns_only_the_members_own_upload_on_their_own_enrollment()
		{
			var (enrollment, lesson) = await FileRequestAsync();
			Value(await Controller().CompleteLesson(new LmsCompleteLessonInput
			{
				EnrollmentId = enrollment.LmsEnrollmentId, LessonId = lesson.LmsLessonId, FileName = "card.pdf", FileContentType = "application/pdf",
				FileData = Convert.ToBase64String(new byte[] { 37, 80, 68, 70 })
			}, CancellationToken.None)).Error.Should().BeNull();
			var fileId = Value(await Controller().GetCourse(enrollment.LmsEnrollmentId)).Lessons[0].Files.Single().LessonFileId;

			var file = (await Controller().GetLessonFile(fileId)).Should().BeOfType<FileContentResult>().Subject;
			file.FileContents.Should().Equal(37, 80, 68, 70);
			file.ContentType.Should().Be("application/pdf");
			file.FileDownloadName.Should().Be("card.pdf");

			(await Controller(LmsWorld.Member2).GetLessonFile(fileId)).Should().BeOfType<NotFoundResult>("another member's upload");
			(await Controller(LmsWorld.Grader).GetLessonFile(fileId)).Should().BeOfType<NotFoundResult>("graders read uploads on the web");

			// A file of theirs that no lesson of their enrollments holds is not served.
			var stray = await _world.Files.InsertAsync(new LmsLessonFile { DepartmentId = D, LmsLessonId = lesson.LmsLessonId, UserId = LmsWorld.Member, FileName = "stray.pdf", ContentType = "application/pdf", SizeBytes = 1, Data = new byte[1] }, CancellationToken.None);
			(await Controller().GetLessonFile(stray.LmsLessonFileId)).Should().BeOfType<NotFoundResult>();

			_seated.Remove(LmsWorld.Member);
			(await Controller().GetLessonFile(fileId)).Should().BeOfType<NotFoundResult>("no LMS seat");
			_seated.Add(LmsWorld.Member);

			var stored = _world.Enrollments.Rows.Single(e => e.LmsEnrollmentId == enrollment.LmsEnrollmentId);
			stored.Status = (int)LmsEnrollmentStatuses.Withdrawn;
			(await Controller().GetLessonFile(fileId)).Should().BeOfType<NotFoundResult>("a withdrawn enrollment");

			_moduleOn = false;
			(await Controller().GetLessonFile(fileId)).Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task The_members_file_list_is_metadata_only()
		{
			var (enrollment, lesson) = await FileRequestAsync();
			await Controller().CompleteLesson(new LmsCompleteLessonInput
			{
				EnrollmentId = enrollment.LmsEnrollmentId, LessonId = lesson.LmsLessonId, FileName = "card.pdf", FileContentType = "application/pdf",
				FileData = Convert.ToBase64String(new byte[] { 37, 80, 68, 70 })
			}, CancellationToken.None);

			var files = await _world.Learner.GetMyLessonFilesAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId);
			files.Should().ContainSingle().Which.Data.Should().BeNull();
			(await _world.Learner.GetMyLessonFilesAsync(D, LmsWorld.Member2, enrollment.LmsEnrollmentId)).Should().BeEmpty("another member's enrollment");
			(await _world.Learner.GetMyLessonFilesAsync(D + 1, LmsWorld.Member, enrollment.LmsEnrollmentId)).Should().BeEmpty("another department");
		}

		#endregion Lesson files

		#region Surveys

		private async Task<(LmsEnrollment Enrollment, LmsLesson Survey)> SurveyAsync()
		{
			var survey = new LmsLesson
			{
				Kind = (int)LmsLessonKinds.Survey, Title = "Feedback", IsRequired = true,
				SurveyJson = JsonConvert.SerializeObject(new[]
				{
					new LmsSurveyQuestion { Key = "q1", Kind = (int)LmsSurveyQuestionKinds.Rating, Text = "Rate it" },
					new LmsSurveyQuestion { Key = "q2", Kind = (int)LmsSurveyQuestionKinds.Choice, Text = "Pace", Options = new List<string> { "Slow", "Right", "Fast" } },
					new LmsSurveyQuestion { Key = "q3", Kind = (int)LmsSurveyQuestionKinds.Text, Text = "Anything else?" }
				})
			};
			var (course, _, lessons) = await _world.PublishedCourseAsync("Evaluated", survey);
			return (await EnrollAsync(LmsWorld.Member, course.LmsCourseId), lessons[0]);
		}

		[Test]
		public async Task SubmitSurvey_records_anonymous_answers_and_completes_the_lesson()
		{
			var (enrollment, survey) = await SurveyAsync();
			LmsSubmitSurveyInput Input(Dictionary<string, string> answers) => new LmsSubmitSurveyInput { EnrollmentId = enrollment.LmsEnrollmentId, LessonId = survey.LmsLessonId, Answers = answers };

			Value(await Controller().SubmitSurvey(Input(new Dictionary<string, string> { ["q1"] = "6", ["q2"] = "1" }), CancellationToken.None)).Error.Should().Be("InvalidInput");
			Value(await Controller().SubmitSurvey(Input(new Dictionary<string, string> { ["q1"] = "4", ["q2"] = "3" }), CancellationToken.None)).Error.Should().Be("InvalidInput", "options are 0-based");

			var done = Value(await Controller().SubmitSurvey(Input(new Dictionary<string, string> { ["q1"] = "4", ["q2"] = "1", ["q3"] = "More ladders" }), CancellationToken.None));
			done.Error.Should().BeNull();
			done.Status.Should().Be("success");
			done.CourseCompleted.Should().BeTrue();
			var response = _world.Surveys.Rows.Should().ContainSingle().Subject;
			response.AnswersJson.Should().Contain("More ladders").And.NotContain(LmsWorld.Member);

			Value(await Controller().SubmitSurvey(Input(new Dictionary<string, string> { ["q1"] = "5", ["q2"] = "0" }), CancellationToken.None)).Error.Should().Be("AlreadySubmitted");
		}

		[Test]
		public async Task SubmitSurvey_refuses_another_members_enrollment_a_member_without_a_seat_and_the_module_off()
		{
			var (enrollment, survey) = await SurveyAsync();
			var input = new LmsSubmitSurveyInput { EnrollmentId = enrollment.LmsEnrollmentId, LessonId = survey.LmsLessonId, Answers = new Dictionary<string, string> { ["q1"] = "4", ["q2"] = "1" } };

			(await Controller(LmsWorld.Member2).SubmitSurvey(input, CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();
			(await Controller().SubmitSurvey(null, CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();

			_seated.Remove(LmsWorld.Member);
			var refused = Value(await Controller().SubmitSurvey(input, CancellationToken.None));
			refused.Error.Should().Be(LmsController.NotLicensedError);
			refused.Status.Should().Be("failure");
			_world.Surveys.Rows.Should().BeEmpty();

			_seated.Add(LmsWorld.Member);
			_moduleOn = false;
			(await Controller().SubmitSurvey(input, CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();
		}

		#endregion Surveys
	}
}
