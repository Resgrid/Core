using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Ai;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Services;
using Resgrid.Services.Knowledge;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>
	/// T5 item 5, course drafts from a protocol or SOP: the gates fail closed (Enhanced AI off, the LMS off, no Manage Courses, "no AI" content,
	/// Advanced Data Protection), a draft is a DRAFT course (never submitted or published), every lesson cites its source sections and carries
	/// their text as written, every number the model wrote is in the sections it cites, the provenance is recorded and the exchange is logged.
	/// No live model is called (the client is a mock).
	/// </summary>
	[TestFixture, NonParallelizable]
	public class LmsCourseDraftServiceTests
	{
		private CourseStudioWorld _world;

		[SetUp]
		public void SetUp() => _world = new CourseStudioWorld();

		[TearDown]
		public void TearDown() => _world.Dispose();

		private static LmsCourseDraftRequest FromProtocol(int questions = 2) => new LmsCourseDraftRequest
		{
			DepartmentId = CourseStudioWorld.DepartmentId, UserId = LmsWorld.Admin, Locale = "en", SourceKind = (int)LmsCourseDraftSourceKinds.ProtocolVersion,
			ProtocolId = CourseStudioWorld.ProtocolId, QuestionCount = questions
		};

		private static object Lesson(string title, string intro, string[] points, params string[] sources) => new { title, intro, key_points = points, sources };

		private static object Question(string question, string[] options, int correct, string explanation, params string[] sources) =>
			new { question, options, correct_index = correct, explanation, sources };

		/// <summary>S1 = "Adult cardiac arrest" (rates and the epinephrine dose), S2 = "Defibrillation".</summary>
		private void ModelDraftsTheCourse() => _world.ModelReturns(AiCourseStudioPrompt.CourseTool, new
		{
			title = "Cardiac arrest for crews",
			summary = "How the crew runs an adult cardiac arrest.",
			modules = new object[]
			{
				new
				{
					title = "Running the arrest",
					lessons = new object[]
					{
						Lesson("Compressions and drugs", "Compressions start at 100 to 120 per minute.", new[] { "Give epinephrine 1 mg IV every 3 to 5 minutes." }, "S1"),
						Lesson("Defibrillation", "Attach the defibrillator as soon as it arrives.", new[] { "Follow its prompts." }, "S2"),
						// Invented numbers (2 mg, every 4 minutes): left out.
						Lesson("A computed dose", "Give 2 mg every 4 minutes.", Array.Empty<string>(), "S1"),
						// A number from S1 cited against S2 only: left out (the number must be in the sections the lesson cites).
						Lesson("Shocks", "Shock at 100 joules.", Array.Empty<string>(), "S2"),
						// No citation, or a citation of a section that was never shown: left out.
						Lesson("Uncited", "Work as a team.", Array.Empty<string>()),
						Lesson("Unknown source", "Work as a team.", Array.Empty<string>(), "S9")
					}
				}
			},
			questions = new object[]
			{
				Question("How often is epinephrine given?", new[] { "Every 3 to 5 minutes", "Only once", "Only after a shock" }, 0, "The protocol says every 3 to 5 minutes.", "S1"),
				Question("How often is epinephrine given?", new[] { "Every 4 minutes", "Only once", "Never" }, 0, "Every 4 minutes.", "S1")
			}
		});

		#region Gates

		[Test]
		public async Task Without_Enhanced_AI_the_draft_fails_closed_and_reads_nothing()
		{
			_world.Knowledge.Setup(x => x.IsEntitledAsync(CourseStudioWorld.DepartmentId)).ReturnsAsync(false);

			var result = await _world.Drafts.DraftAsync(FromProtocol());

			result.Outcome.Should().Be(LmsStudioOutcomes.NotEntitled);
			_world.NoModelCall();
			_world.ProtocolAccess.Verify(x => x.GetReaderAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never, "no content is read without the entitlement");
			_world.Lms.Courses.Rows.Should().BeEmpty();
			_world.Rows.Should().BeEmpty();
			(await _world.Drafts.GetStatusAsync(CourseStudioWorld.DepartmentId, LmsWorld.Admin)).AiAvailable.Should().BeFalse();
		}

		[Test]
		public async Task Without_the_LMS_add_on_the_draft_fails_closed_before_Enhanced_AI_is_even_asked()
		{
			_world.Lms.Gate.AddonActive = false;

			(await _world.Drafts.DraftAsync(FromProtocol())).Outcome.Should().Be(LmsStudioOutcomes.NotEntitled);
			_world.Lms.Gate.AddonActive = null;
			(await _world.Drafts.DraftAsync(FromProtocol())).Outcome.Should().Be(LmsStudioOutcomes.NotEntitled, "unknown billing is no add-on");
			_world.Lms.Gate.AddonActive = true;
			_world.Lms.Gate.FlagOn = false;
			(await _world.Drafts.DraftAsync(FromProtocol())).Outcome.Should().Be(LmsStudioOutcomes.NotEntitled);

			_world.NoModelCall();
			_world.Knowledge.Verify(x => x.IsEntitledAsync(It.IsAny<int>()), Times.Never);
			_world.Lms.Courses.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task A_member_without_Manage_Courses_or_protocol_authorship_is_refused()
		{
			var request = FromProtocol();
			request.UserId = LmsWorld.Member;
			(await _world.Drafts.DraftAsync(request)).Outcome.Should().Be(LmsStudioOutcomes.NotAllowed);

			_world.Reader = new ProtocolReader { DepartmentId = CourseStudioWorld.DepartmentId, UserId = LmsWorld.Admin, IsCurrentMember = true };
			(await _world.Drafts.DraftAsync(FromProtocol())).Outcome.Should().Be(LmsStudioOutcomes.NotAllowed, "the protocol's author right is needed too");
			_world.NoModelCall();
		}

		[Test]
		public async Task No_AI_and_licensed_protocols_and_kept_local_departments_never_reach_the_model()
		{
			_world.Protocol.ExcludeFromAi = true;
			(await _world.Drafts.DraftAsync(FromProtocol())).Outcome.Should().Be(LmsStudioOutcomes.Excluded);
			_world.Protocol.ExcludeFromAi = false;
			_world.Protocol.IsLicensedContent = true;
			(await _world.Drafts.DraftAsync(FromProtocol())).Outcome.Should().Be(LmsStudioOutcomes.Excluded);
			(await _world.Drafts.GetDraftableProtocolsAsync(CourseStudioWorld.DepartmentId, LmsWorld.Admin)).Should().BeEmpty("licensed content is never offered");
			_world.Protocol.IsLicensedContent = false;

			_world.Protection.Setup(x => x.GetStateAsync(CourseStudioWorld.DepartmentId, true)).ReturnsAsync(DepartmentDataProtectionState.Enabled);
			(await _world.Drafts.DraftAsync(FromProtocol())).Outcome.Should().Be(LmsStudioOutcomes.KeptLocal);
			(await _world.Drafts.GetStatusAsync(CourseStudioWorld.DepartmentId, LmsWorld.Admin)).AiReason.Should().Be(LmsStudioOutcomes.KeptLocal);

			_world.NoModelCall();
			_world.Lms.Courses.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task An_unconfigured_model_or_an_unauditable_exchange_fails_closed()
		{
			var endpoint = Resgrid.Config.AiConfig.Endpoint;
			Resgrid.Config.AiConfig.Endpoint = "";
			(await _world.Drafts.DraftAsync(FromProtocol())).Outcome.Should().Be(LmsStudioOutcomes.Unconfigured);
			Resgrid.Config.AiConfig.Endpoint = endpoint;

			_world.ConversationProtection.Setup(x => x.PreflightAsync(It.IsAny<Resgrid.Model.AdminAssist.AdminAssistActor>(), It.IsAny<System.Threading.CancellationToken>()))
				.ThrowsAsync(new UnauthorizedAccessException());
			(await _world.Drafts.DraftAsync(FromProtocol())).Outcome.Should().Be(LmsStudioOutcomes.Unconfigured);
			_world.NoModelCall();
		}

		#endregion Gates

		#region Draft

		[Test]
		public async Task A_draft_is_a_draft_course_whose_lessons_cite_their_sections_and_quote_them_as_written()
		{
			ModelDraftsTheCourse();

			var result = await _world.Drafts.DraftAsync(FromProtocol());

			result.Outcome.Should().Be(LmsStudioOutcomes.Created, result.Outcome);
			result.Lessons.Should().Be(3, "two drafted lessons and the closing check");
			result.Questions.Should().Be(1, "the question with an invented interval is left out");
			result.Dropped.Should().Be(5);

			// A draft only: nothing submitted, approved or published.
			var course = _world.Lms.Courses.Rows.Single();
			course.Status.Should().Be((int)ProtocolStatuses.Draft);
			course.CurrentVersionId.Should().BeNull();
			course.Title.Should().Be("Cardiac arrest for crews");
			var version = _world.Lms.Versions.Rows.Single();
			version.Status.Should().Be((int)ProtocolVersionStatuses.Draft);
			_world.Lms.Approvals.Rows.Should().BeEmpty();

			var lessons = _world.Lms.Lessons.Rows.Where(l => l.Kind == (int)LmsLessonKinds.Blocks).OrderBy(l => l.SortOrder).ToList();
			lessons.Select(l => l.Title).Should().Equal("Compressions and drugs", "Defibrillation");
			lessons.Should().OnlyContain(l => l.ContentHtml.Contains("/User/Protocols/View/40?versionId=401#"), "every lesson links to its cited section");
			lessons[0].ContentHtml.Should().Contain("Give epinephrine 1 mg IV every 3 to 5 minutes.", "the dose appears only as the protocol wrote it");
			lessons[1].ContentHtml.Should().Contain("Attach the defibrillator as soon as it arrives and follow its prompts.");
			string.Join(" ", lessons.Select(l => l.ContentHtml)).Should().NotContain("2 mg").And.NotContain("joules").And.NotContain("Work as a team");

			// The question bank draft and the closing check.
			var bank = _world.Lms.Banks.Rows.Single();
			bank.Name.Should().Contain("Cardiac arrest for crews");
			_world.Lms.Questions.Rows.Single().Text.Should().Be("How often is epinephrine given?");
			_world.Lms.Lessons.Rows.Single(l => l.Kind == (int)LmsLessonKinds.Assessment).LmsQuestionBankId.Should().Be(bank.LmsQuestionBankId);

			// The course rules accept it as a draft the author can edit and submit.
			var outline = await _world.Lms.CourseService.GetOutlineAsync(CourseStudioWorld.DepartmentId, version.LmsCourseVersionId);
			LmsCourseValidator.Validate(outline, _world.LiveQuestionsByBank(), StringHelpers.HtmlToPlainText).Should().BeEmpty();
		}

		[Test]
		public async Task The_provenance_and_the_exchange_are_recorded_with_each_lessons_citations()
		{
			ModelDraftsTheCourse();
			var result = await _world.Drafts.DraftAsync(FromProtocol());

			var anchors = ProtocolKnowledgeSource.BuildVersionChunks(CourseStudioWorld.DepartmentId, _world.Protocol, _world.Versions[0], null).Select(c => c.Anchor).ToList();
			var origin = _world.Origins.Rows.Single();
			origin.LmsCourseId.Should().Be(result.LmsCourseId);
			origin.OriginKind.Should().Be((int)LmsCourseOriginKinds.AiDraftFromProtocol);
			origin.SourceKey.Should().Be(LmsCourseOriginKeys.Protocol(CourseStudioWorld.ProtocolId, CourseStudioWorld.VersionId));
			origin.SourceVersion.Should().Be("1.0");
			origin.ExchangeId.Should().Be(result.ExchangeId).And.NotBeNullOrEmpty();
			var citations = LmsLessonCitation.Parse(origin.CitationsJson);
			citations.Should().HaveCount(2);
			citations[0].Anchors.Should().Equal(anchors[0]);
			citations[1].Anchors.Should().Equal(anchors[1]);
			citations.Should().OnlyContain(c => c.LmsLessonId > 0 && c.Labels.Count == 1);

			var row = _world.Rows.Single();
			row.PromptVersion.Should().Be(AiCourseStudioPrompt.CourseVersion);
			row.Outcome.Should().Be(LmsStudioOutcomes.Drafted);
			row.Content.Should().StartWith("enc:", "the exchange is stored through the conversation protection");
			row.Content.Should().NotContain("epinephrine", "the audit row cites the source, never the content");

			// The model saw only the protocol's own sections.
			var prompt = _world.Requests.Single().Messages.Last().Content;
			prompt.Should().Contain("[S1]").And.Contain("[S2]").And.Contain("Start compressions at 100 to 120 per minute.");
			_world.Events.Verify(e => e.SendMessage(It.Is<Resgrid.Model.Events.AuditEvent>(a => a.Type == AuditLogTypes.LmsCourseDraftedWithAi)), Times.Once);
		}

		[Test]
		public async Task Output_with_nothing_valid_is_refused_and_no_course_is_made()
		{
			_world.ModelReturns(AiCourseStudioPrompt.CourseTool, new { modules = new object[] { new { title = "M", lessons = new object[] { Lesson("Dose", "Give 7 mg.", Array.Empty<string>(), "S1") } } } });

			var result = await _world.Drafts.DraftAsync(FromProtocol());

			result.Outcome.Should().Be(LmsStudioOutcomes.Refused);
			_world.Lms.Courses.Rows.Should().BeEmpty();
			_world.Lms.Banks.Rows.Should().BeEmpty();
			_world.Rows.Single().Outcome.Should().Be(LmsStudioOutcomes.Refused);
		}

		[Test]
		public async Task An_uploaded_SOP_is_drafted_from_its_text_and_cites_its_pages()
		{
			_world.ModelReturns(AiCourseStudioPrompt.CourseTool, new
			{
				modules = new object[] { new { title = "Mayday", lessons = new object[] { Lesson("Calling a mayday", "Say mayday three times.", Array.Empty<string>(), "S1") } } }
			});
			var request = FromProtocol(0);
			request.SourceKind = (int)LmsCourseDraftSourceKinds.Document;
			request.FileName = "mayday-sop.txt";
			request.Data = Encoding.UTF8.GetBytes("Mayday procedure. Say mayday three times, then your name, location and need. Activate your emergency button.");

			var result = await _world.Drafts.DraftAsync(request);

			result.Outcome.Should().Be(LmsStudioOutcomes.Created, result.Outcome);
			var lesson = _world.Lms.Lessons.Rows.Single();
			lesson.ContentHtml.Should().Contain("mayday-sop.txt").And.Contain("Activate your emergency button.");
			var origin = _world.Origins.Rows.Single();
			origin.OriginKind.Should().Be((int)LmsCourseOriginKinds.AiDraftFromDocument);
			origin.SourceKey.Should().Be("document:mayday-sop.txt");
			origin.SourceUrl.Should().BeNull("the file is not kept");
			_world.Lms.Banks.Rows.Should().BeEmpty("no questions were asked for");
		}

		[Test]
		public async Task An_unsupported_upload_is_invalid_and_never_sent()
		{
			var request = FromProtocol();
			request.SourceKind = (int)LmsCourseDraftSourceKinds.Document;
			request.FileName = "macro.exe";
			request.Data = new byte[] { 1, 2, 3 };

			(await _world.Drafts.DraftAsync(request)).Outcome.Should().Be(LmsStudioOutcomes.Invalid);
			_world.NoModelCall();
		}

		[Test]
		public async Task The_draftable_list_offers_published_protocols_the_member_authors()
		{
			var list = await _world.Drafts.GetDraftableProtocolsAsync(CourseStudioWorld.DepartmentId, LmsWorld.Admin);
			list.Should().ContainSingle(p => p.ProtocolId == CourseStudioWorld.ProtocolId && p.VersionId == CourseStudioWorld.VersionId && p.VersionLabel == "1.0");

			_world.Reader = new ProtocolReader { DepartmentId = CourseStudioWorld.DepartmentId, UserId = LmsWorld.Member, IsCurrentMember = true };
			(await _world.Drafts.GetDraftableProtocolsAsync(CourseStudioWorld.DepartmentId, LmsWorld.Member)).Should().BeEmpty();
		}

		#endregion Draft
	}
}
