using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.KnowledgeK3
{
	/// <summary>
	/// K3: LMS courses in the knowledge corpus. Only published lesson content is chunked (blocks per block with lesson-scoped b-{key} anchors,
	/// transcripts, document titles, embedded protocol versions unless "no AI"); answer keys, explanations, attempts, enrollments' progress,
	/// learner files and survey answers never reach a chunk; the source's own read rule (enrolled, or in the catalog) behind the LMS gate;
	/// the "Ask this course" version source; and the reindex the course service queues on publish, retire and catalog changes.
	/// </summary>
	[TestFixture]
	public class LmsCourseKnowledgeSourceTests
	{
		private const int Dept = LmsWorld.DepartmentId;
		private const string Secret = "SECRET-LEARNER-OR-KEY";

		private LmsWorld _world;
		private Mock<ILmsAccessService> _access;
		private Mock<ILmsGateService> _gate;
		private Mock<IProtocolVersionsRepository> _protocolVersions;
		private Mock<IKnowledgeIndexRepository> _index;
		private LmsCourseKnowledgeSource _source;
		private readonly List<DispatchProtocol> _protocols = new List<DispatchProtocol>();

		[SetUp]
		public void SetUp()
		{
			_world = new LmsWorld();
			_access = new Mock<ILmsAccessService>();
			_access.Setup(a => a.IsFlagEnabledAsync(Dept)).ReturnsAsync(true);
			_gate = new Mock<ILmsGateService>();
			_gate.Setup(g => g.GetActorAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string user) => _world.Actor(user));
			_protocolVersions = new Mock<IProtocolVersionsRepository>();
			_protocolVersions.Setup(v => v.GetByIdsAsync(Dept, It.IsAny<IEnumerable<int>>(), true)).ReturnsAsync(new List<ProtocolVersion>
			{
				new ProtocolVersion { ProtocolVersionId = 700, DispatchProtocolId = 70, DepartmentId = Dept, Status = (int)ProtocolVersionStatuses.Published, Major = 2, Minor = 0,
					ContentHtml = "<h2>Rehab</h2><p>Rotate crews every two cylinders.</p>" },
				new ProtocolVersion { ProtocolVersionId = 710, DispatchProtocolId = 71, DepartmentId = Dept, Status = (int)ProtocolVersionStatuses.Published, Major = 1, Minor = 0,
					ContentHtml = "<p>LICENSED-TEXT never indexed.</p>" }
			});
			_protocols.Clear();
			_protocols.Add(new DispatchProtocol { DispatchProtocolId = 70, DepartmentId = Dept, Name = "Rehab", Status = (int)ProtocolStatuses.Published });
			_protocols.Add(new DispatchProtocol { DispatchProtocolId = 71, DepartmentId = Dept, Name = "Vendor", Status = (int)ProtocolStatuses.Published, IsLicensedContent = true });
			_index = new Mock<IKnowledgeIndexRepository>();
			_index.Setup(i => i.GetProtocolHeadersAsync(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(() => _protocols.ToList());
			_index.Setup(i => i.GetItemChunksAsync(Dept, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeChunk>());
			_source = new LmsCourseKnowledgeSource(_access.Object, _gate.Object, _world.Courses, _world.Versions, _world.Modules, _world.Lessons, _world.Audiences,
				_world.Enrollments, _protocolVersions.Object, _index.Object);
		}

		private static string Blocks() => ProtocolBlocks.Normalize(new ProtocolContentDocument
		{
			Schema = ProtocolContentDocument.BlockSchema,
			Blocks = new List<ProtocolContentBlock>
			{
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Heading, Key = "h1", Text = "Hydrant flow" },
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Paragraph, Key = "p1", Html = "<p>Open the hydrant slowly.</p>" },
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Steps, Key = "s1", Items = new List<ProtocolStepItem> { new ProtocolStepItem { Key = "a", Html = "Flush" }, new ProtocolStepItem { Key = "b", Html = "Connect" } } }
			}
		}).ToJson();

		/// <summary>A published course with every lesson kind, a bank whose key and explanation carry the secret, and learner data carrying it too.</summary>
		private async Task<(LmsCourse Course, LmsCourseVersion Version, List<LmsLesson> Lessons)> CourseAsync(bool selfEnrollable = true)
		{
			var bank = await _world.BankAsync(new LmsQuestion
			{
				Kind = (int)LmsQuestionKinds.SingleChoice, Text = "Question " + Secret, Explanation = "Explanation " + Secret, Points = 1,
				Options = new List<LmsQuestionOption> { new LmsQuestionOption { Key = "a", Text = "Key " + Secret, IsCorrect = true }, new LmsQuestionOption { Key = "b", Text = "Wrong" } }
			});
			var course = await _world.PublishedCourseAsync("Hydrants",
				new LmsLesson { Kind = (int)LmsLessonKinds.Blocks, Title = "Flow", ContentJson = Blocks() },
				new LmsLesson { Kind = (int)LmsLessonKinds.Blocks, Title = "Html lesson", ContentHtml = "<h2>Hose lays</h2><p>Lay from the hydrant to the engine.</p>", FreeResponsePrompt = "Describe" },
				new LmsLesson { Kind = (int)LmsLessonKinds.Video, Title = "Video", VideoUrl = "https://example.test/v", TranscriptText = "The transcript says open slowly." },
				new LmsLesson { Kind = (int)LmsLessonKinds.Document, Title = "Reading", DocumentUrl = "https://example.test/doc.pdf", DocumentName = "Hydrant maintenance guide" },
				new LmsLesson { Kind = (int)LmsLessonKinds.ProtocolVersion, Title = "Rehab protocol", ProtocolVersionId = 700 },
				new LmsLesson { Kind = (int)LmsLessonKinds.ProtocolVersion, Title = "Vendor protocol", ProtocolVersionId = 710 },
				new LmsLesson { Kind = (int)LmsLessonKinds.Assessment, Title = "Test " + Secret, LmsQuestionBankId = bank.LmsQuestionBankId },
				new LmsLesson { Kind = (int)LmsLessonKinds.Survey, Title = "Survey", SurveyJson = JsonConvert.SerializeObject(new[] { new LmsSurveyQuestion { Key = "q", Text = "Survey " + Secret } }) });
			var stored = _world.Courses.Rows.Single(c => c.LmsCourseId == course.Course.LmsCourseId);
			stored.IsSelfEnrollable = selfEnrollable;
			course.Course.IsSelfEnrollable = selfEnrollable;

			// Learner data the source must never read into a chunk: an enrollment with progress, an uploaded file and a survey answer.
			await _world.Enrollments.InsertAsync(new LmsEnrollment
			{
				DepartmentId = Dept, LmsCourseId = course.Course.LmsCourseId, LmsCourseVersionId = course.Version.LmsCourseVersionId, UserId = LmsWorld.Member2,
				Status = (int)LmsEnrollmentStatuses.InProgress, IsOpen = true, AssignedOn = DateTime.UtcNow, RolePathKey = Secret
			}, CancellationToken.None);
			await _world.Files.InsertAsync(new LmsLessonFile { DepartmentId = Dept, LmsLessonId = course.Lessons[1].LmsLessonId, UserId = LmsWorld.Member2, FileName = Secret + ".pdf",
				Data = System.Text.Encoding.UTF8.GetBytes(Secret) }, CancellationToken.None);
			return course;
		}

		[Test]
		public async Task Published_lessons_are_chunked_per_block_with_lesson_scoped_anchors_and_nothing_else()
		{
			var (course, version, lessons) = await CourseAsync();

			(await _source.ListItemIdsAsync(Dept, KnowledgeSourceKinds.Course)).Should().BeEquivalentTo(course.LmsCourseId.ToString());
			var content = await _source.BuildAsync(Dept, KnowledgeSourceKinds.Course, course.LmsCourseId.ToString());

			var chunks = content.Chunks;
			chunks.Should().OnlyContain(c => c.SourceKind == (int)KnowledgeSourceKinds.Course && c.ItemId == course.LmsCourseId.ToString() &&
				c.VersionId == version.LmsCourseVersionId.ToString() && c.AccessFlags == (int)KnowledgeAccessFlags.AssigneesRead);
			var flow = lessons.Single(l => l.Title == "Flow").LmsLessonId;
			chunks.Should().Contain(c => c.Anchor == "l" + flow + "-b-h1" && c.Text.Contains("Open the hydrant slowly"), "a heading block's paragraphs cite its block anchor");
			chunks.Should().Contain(c => c.Anchor == "l" + flow + "-b-s1" && c.Text.Contains("Flush"), "a structured block cites its own anchor");
			chunks.Should().Contain(c => c.Anchor.StartsWith("l" + lessons.Single(l => l.Title == "Html lesson").LmsLessonId + "-p-") && c.HeadingPath.Contains("Hose lays"));
			chunks.Should().Contain(c => c.Text.Contains("The transcript says open slowly"));
			chunks.Should().Contain(c => c.Text.Contains("Hydrant maintenance guide"));
			chunks.Should().Contain(c => c.Text.Contains("Rotate crews every two cylinders"), "an embedded protocol version joins through its course");
			chunks.Should().NotContain(c => c.Text.Contains("LICENSED-TEXT"), "a licensed protocol never joins the corpus, even inside a course");
			chunks.Should().NotContain(c => (c.Text + c.HeadingPath + c.Title + c.Anchor).Contains(Secret), "answer keys, explanations, attempts and learner data are never indexed");
			foreach (var chunk in chunks)
			{
				KnowledgeLinks.TryParseCourseAnchor(chunk.Anchor, out var lessonId, out _).Should().BeTrue(chunk.Anchor);
				lessons.Should().Contain(l => l.LmsLessonId == lessonId, "every chunk cites its own lesson");
			}
		}

		[Test]
		public void The_source_reads_no_question_bank_attempt_progress_file_or_survey_store()
		{
			var forbidden = new[] { typeof(ILmsQuestionBanksRepository), typeof(ILmsQuestionsRepository), typeof(ILmsAssessmentAttemptsRepository), typeof(ILmsLessonFilesRepository),
				typeof(ILmsLessonProgressRepository), typeof(ILmsSurveyResponsesRepository), typeof(ILmsAssessmentService), typeof(ILmsLearnerService), typeof(ILearningRecordService),
				typeof(ILearningRecordWriter) };
			var parameters = typeof(LmsCourseKnowledgeSource).GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).ToList();
			parameters.Should().NotContain(forbidden);
			LmsCourseKnowledgeSource.IsIndexedLesson(new LmsLesson { Kind = (int)LmsLessonKinds.Assessment }).Should().BeFalse();
			LmsCourseKnowledgeSource.IsIndexedLesson(new LmsLesson { Kind = (int)LmsLessonKinds.Survey }).Should().BeFalse();
		}

		[Test]
		public async Task Drafts_retired_system_courses_and_a_dark_LMS_are_never_indexed()
		{
			var (course, _, _) = await CourseAsync();
			_access.Setup(a => a.IsFlagEnabledAsync(Dept)).ReturnsAsync(false);
			(await _source.ListItemIdsAsync(Dept, KnowledgeSourceKinds.Course)).Should().BeEmpty();
			(await _source.BuildAsync(Dept, KnowledgeSourceKinds.Course, course.LmsCourseId.ToString())).Should().BeNull();

			_access.Setup(a => a.IsFlagEnabledAsync(Dept)).ReturnsAsync(true);
			var stored = _world.Courses.Rows.Single(c => c.LmsCourseId == course.LmsCourseId);
			stored.Status = (int)ProtocolStatuses.Retired;
			(await _source.BuildAsync(Dept, KnowledgeSourceKinds.Course, course.LmsCourseId.ToString())).Should().BeNull();
			LmsCourseKnowledgeSource.IsIndexable(new LmsCourse { DepartmentId = 0, PublisherKind = (int)LmsPublisherKinds.System, SystemKey = "academy:x", Status = (int)ProtocolStatuses.Published, CurrentVersionId = 1 }, Dept)
				.Should().BeFalse("the Academy is its own system corpus");
			LmsCourseKnowledgeSource.IsIndexable(new LmsCourse { DepartmentId = Dept, Status = (int)ProtocolStatuses.Draft }, Dept).Should().BeFalse();
		}

		private static KnowledgeReader Reader(string user, bool trainings = true) =>
			new KnowledgeReader { DepartmentId = Dept, UserId = user, CanViewTrainings = trainings, CanViewProtocols = true };

		[Test]
		public async Task Readers_are_the_enrolled_and_those_whose_catalog_lists_the_course_behind_the_LMS_gate()
		{
			var (course, version, _) = await CourseAsync(selfEnrollable: false);
			var id = course.LmsCourseId.ToString();

			async Task<bool> Reads(string user, bool trainings = true)
			{
				var scope = new KnowledgeReaderScope { DepartmentId = Dept, UserId = user, IsCurrentMember = true };
				await _source.AddReadableAsync(Reader(user, trainings), scope);
				return scope.ReadableItems.TryGetValue(KnowledgeSourceKinds.Course, out var set) && set.Contains(id);
			}

			(await Reads(LmsWorld.Member)).Should().BeFalse("not enrolled and not in the catalog");
			(await Reads(LmsWorld.Member2)).Should().BeTrue("enrolled");
			(await Reads(LmsWorld.Member2, trainings: false)).Should().BeFalse("no Training view claim");

			// Self-enrollable with an audience the member is not in: still not theirs; once the audience names their role it is.
			var stored = _world.Courses.Rows.Single(c => c.LmsCourseId == course.LmsCourseId);
			stored.IsSelfEnrollable = true;
			await _world.Audiences.ReplaceAsync(Dept, course.LmsCourseId, new[] { new ContentAudienceRule(ContentAudienceKinds.Role, "99") });
			(await Reads(LmsWorld.Member)).Should().BeFalse();
			_gate.Setup(g => g.GetActorAsync(Dept, LmsWorld.Member)).ReturnsAsync(() =>
			{
				var actor = _world.Actor(LmsWorld.Member);
				actor.Facts.RoleIds.Add(99);
				return actor;
			});
			(await Reads(LmsWorld.Member)).Should().BeTrue("in the catalog through their role");

			// The LMS gate closed (add-on off, or billing unknown): nobody reads a course.
			_world.Gate.AddonActive = false;
			(await Reads(LmsWorld.Member2)).Should().BeFalse();
			_world.Gate.AddonActive = null;
			(await Reads(LmsWorld.Member2)).Should().BeFalse();
			_world.Gate.AddonActive = true;
			_world.Gate.FlagOn = false;
			(await Reads(LmsWorld.Member2)).Should().BeFalse();
		}

		[Test]
		public void The_access_filter_reads_a_course_chunk_only_through_the_live_readable_set()
		{
			var chunk = new KnowledgeChunk
			{
				DepartmentId = Dept, SourceKind = (int)KnowledgeSourceKinds.Course, ItemId = "5", Text = "x", AccessFlags = (int)KnowledgeAccessFlags.AssigneesRead,
				AudienceJson = KnowledgeAccessFilter.SerializeAudience(new[] { new ContentAudienceRule(ContentAudienceKinds.Group, "3") })
			};
			var scope = new KnowledgeReaderScope { DepartmentId = Dept, UserId = LmsWorld.Member, IsCurrentMember = true };
			KnowledgeAccessFilter.CanRead(scope, chunk).Should().BeFalse("the live rule did not allow the course");
			scope.Allow(KnowledgeSourceKinds.Course, new[] { "5" });
			KnowledgeAccessFilter.CanRead(scope, chunk).Should().BeTrue();
			KnowledgeAccessFilter.CanRead(new KnowledgeReaderScope { DepartmentId = 8, UserId = LmsWorld.Member, IsCurrentMember = true, ReadableItems = scope.ReadableItems }, chunk)
				.Should().BeFalse("another department");
		}

		[Test]
		public async Task Ask_this_course_builds_the_learners_own_version_and_refuses_another_or_a_closed_gate()
		{
			var (course, version, lessons) = await CourseAsync(selfEnrollable: false);
			var id = course.LmsCourseId.ToString();

			var mine = await _source.LoadForReaderAsync(Reader(LmsWorld.Member2), KnowledgeSourceKinds.Course, id, null);
			mine.Outcome.Should().BeNull();
			mine.VersionId.Should().Be(version.LmsCourseVersionId.ToString());
			mine.Chunks.Should().NotBeEmpty().And.OnlyContain(c => c.AccessFlags == 0 && c.AudienceJson == null && c.KnowledgeChunkId < 0);
			mine.Chunks.Should().NotContain(c => c.Text.Contains(Secret));

			(await _source.LoadForReaderAsync(Reader(LmsWorld.Member2), KnowledgeSourceKinds.Course, id, "999999")).Outcome.Should().Be(KnowledgeOutcomes.Invalid);
			(await _source.LoadForReaderAsync(Reader(LmsWorld.Member), KnowledgeSourceKinds.Course, id, null)).Outcome.Should().Be(KnowledgeOutcomes.Invalid, "not enrolled, not in the catalog");

			_world.Gate.AddonActive = false;
			(await _source.LoadForReaderAsync(Reader(LmsWorld.Member2), KnowledgeSourceKinds.Course, id, null)).Outcome.Should().Be(KnowledgeOutcomes.NotEntitled);
			_world.Gate.AddonActive = true;
			(await _source.LoadForReaderAsync(Reader(LmsWorld.Member2, trainings: false), KnowledgeSourceKinds.Course, id, null)).Outcome.Should().Be(KnowledgeOutcomes.NotEntitled);
		}

		[Test]
		public async Task Retiring_and_changing_a_course_queue_its_reindex()
		{
			var created = await _world.CourseService.CreateAsync(_world.AdminActor, new LmsCourseInput { Title = "Ladders", IsSelfEnrollable = true });
			var courseId = created.Course.LmsCourseId.ToString();

			(await _world.CourseService.UpdateCourseAsync(_world.AdminActor, created.Course.LmsCourseId, new LmsCourseInput { Title = "Ladders 2", IsSelfEnrollable = false })).Succeeded.Should().BeTrue();
			_world.KnowledgeQueue.Verify(q => q.EnqueueAsync(Dept, KnowledgeSourceKinds.Course, courseId, It.IsAny<CancellationToken>()), Times.Once);

			(await _world.CourseService.RetireAsync(_world.AdminActor, created.Course.LmsCourseId)).Succeeded.Should().BeTrue();
			_world.KnowledgeQueue.Verify(q => q.EnqueueAsync(Dept, KnowledgeSourceKinds.Course, courseId, It.IsAny<CancellationToken>()), Times.Exactly(2));

			_world.KnowledgeQueue.Setup(q => q.EnqueueAsync(It.IsAny<int>(), It.IsAny<KnowledgeSourceKinds>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("queue down"));
			var other = await _world.CourseService.CreateAsync(_world.AdminActor, new LmsCourseInput { Title = "Ropes" });
			(await _world.CourseService.RetireAsync(_world.AdminActor, other.Course.LmsCourseId)).Succeeded.Should().BeTrue("a lost reindex never fails the caller");
		}

		[Test]
		public void Course_citations_open_the_lesson_at_its_block()
		{
			KnowledgeLinks.CourseAnchor(42, "b-s1").Should().Be("l42-b-s1");
			KnowledgeLinks.CourseAnchor(42, null).Should().Be("l42");
			KnowledgeLinks.CourseAnchor(42, "bad anchor\"").Should().Be("l42");
			KnowledgeLinks.WebPath((int)KnowledgeSourceKinds.Course, "7", "9", "l42-b-s1").Should().Be("/User/Courses/Open/7?lesson=42#b-s1");
			KnowledgeLinks.WebPath((int)KnowledgeSourceKinds.Course, "7", "9", "l42").Should().Be("/User/Courses/Open/7?lesson=42");
			KnowledgeLinks.WebPath((int)KnowledgeSourceKinds.Course, "7", "9", null).Should().Be("/User/Courses/Open/7");
			KnowledgeLinks.TryParseCourseAnchor("x42-b", out _, out _).Should().BeFalse();
		}
	}
}
