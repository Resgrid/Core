using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Ai;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>
	/// T5 item 5, flashcards: the gates fail closed (Enhanced AI off, the LMS off, no edit right); cards cite one section and hold no number it
	/// lacks; accepting reads the cards back from the stored draft, never from the client; learners see only active cards whose source version
	/// is in force; publishing a newer version of the source (course or protocol) flags the cards for review, and a review re-confirms them
	/// against the version in force. Practice never writes a record: the service holds no writer and the learner's deck writes nothing.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class LmsFlashcardServiceTests
	{
		private CourseStudioWorld _world;

		[SetUp]
		public void SetUp() => _world = new CourseStudioWorld();

		[TearDown]
		public void TearDown() => _world.Dispose();

		/// <summary>A published starter course (cloned, then approved and published through the real course service).</summary>
		private async Task<(LmsCourse Course, LmsCourseVersion Version)> PublishedCourseAsync()
		{
			var cloned = await _world.Starters.CloneAsync(_world.Lms.AdminActor, "driver-evo-basics", "en");
			cloned.Outcome.Should().Be(LmsStudioOutcomes.Created);
			var version = await _world.PublishAsync(cloned.LmsCourseVersionId);
			var course = await _world.Lms.CourseService.GetCourseAsync(CourseStudioWorld.DepartmentId, cloned.LmsCourseId);
			return (course, version);
		}

		private LmsFlashcardDraftRequest Request(int courseId, LmsFlashcardSourceKinds kind = LmsFlashcardSourceKinds.CourseVersion, int protocolId = 0) => new LmsFlashcardDraftRequest
		{
			DepartmentId = CourseStudioWorld.DepartmentId, UserId = LmsWorld.Admin, Locale = "en", LmsCourseId = courseId, SourceKind = (int)kind, ProtocolId = protocolId, Count = 5
		};

		/// <summary>Assigns the course to one member (the cloned course is not self-enrollable) and returns their enrollment id.</summary>
		private async Task<int> EnrollAsync(int courseId, string userId)
		{
			var outcome = await _world.Lms.Learner.AssignAsync(_world.Lms.AdminActor, courseId, new[] { new ContentAudienceRule(ContentAudienceKinds.User, userId) }, null);
			outcome.Should().NotBeNull();
			return _world.Lms.Enrollments.Rows.Single(e => e.LmsCourseId == courseId && string.Equals(e.UserId, userId, StringComparison.OrdinalIgnoreCase)).LmsEnrollmentId;
		}

		private static object Card(string front, string back, string source) => new { front, back, source };

		/// <summary>The S-number of the course section holding <paramref name="text"/> (sections in outline order, as the drafter numbers them).</summary>
		private async Task<string> RefOfAsync(LmsCourse course, string text)
		{
			var outline = await _world.Lms.CourseService.GetOutlineAsync(CourseStudioWorld.DepartmentId, course.CurrentVersionId.Value);
			var sections = LmsStudioSources.Course(outline).Sections;
			return "S" + (sections.FindIndex(s => s.Text.Contains(text)) + 1);
		}

		private async Task<(LmsCourse Course, LmsFlashcardDraft Draft)> DraftCourseCardsAsync()
		{
			var (course, _) = await PublishedCourseAsync();
			var spotter = await RefOfAsync(course, "the driver stops");
			var seatbelt = await RefOfAsync(course, "seatbelt");
			_world.ModelReturns(AiCourseStudioPrompt.CardsTool, new
			{
				cards = new object[]
				{
					Card("What does the driver do when the spotter is out of sight?", "The driver stops.", spotter),
					Card("When does everyone on board wear a seatbelt?", "Before the vehicle moves.", seatbelt),
					// An invented number: left out.
					Card("How far back does the spotter stand?", "3 metres behind the vehicle.", spotter),
					// The same front twice: left out.
					Card("What does the driver do when the spotter is out of sight?", "Stops.", spotter)
				}
			});
			var draft = await _world.Flashcards.DraftAsync(Request(course.LmsCourseId));
			return (course, draft);
		}

		#region Gates

		[Test]
		public async Task Drafting_fails_closed_without_Enhanced_AI_or_the_LMS_or_the_edit_right()
		{
			var (course, _) = await PublishedCourseAsync();

			_world.Knowledge.Setup(x => x.IsEntitledAsync(CourseStudioWorld.DepartmentId)).ReturnsAsync(false);
			(await _world.Flashcards.DraftAsync(Request(course.LmsCourseId))).Outcome.Should().Be(LmsStudioOutcomes.NotEntitled);
			_world.Knowledge.Setup(x => x.IsEntitledAsync(CourseStudioWorld.DepartmentId)).ReturnsAsync(true);

			var member = Request(course.LmsCourseId);
			member.UserId = LmsWorld.Member;
			(await _world.Flashcards.DraftAsync(member)).Outcome.Should().Be(LmsStudioOutcomes.NotAllowed);

			_world.Lms.Gate.AddonActive = false;
			(await _world.Flashcards.DraftAsync(Request(course.LmsCourseId))).Outcome.Should().Be(LmsStudioOutcomes.NotEntitled);

			_world.NoModelCall();
			_world.Cards.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task A_protocol_the_course_is_not_built_from_is_not_a_card_source()
		{
			var (course, _) = await PublishedCourseAsync();
			(await _world.Flashcards.DraftAsync(Request(course.LmsCourseId, LmsFlashcardSourceKinds.ProtocolVersion, CourseStudioWorld.ProtocolId))).Outcome
				.Should().Be(LmsStudioOutcomes.NotFound);
			(await _world.Flashcards.GetSourceChoicesAsync(CourseStudioWorld.DepartmentId, LmsWorld.Admin, course.LmsCourseId))
				.Should().ContainSingle(c => c.SourceKind == (int)LmsFlashcardSourceKinds.CourseVersion);
			_world.NoModelCall();
		}

		#endregion Gates

		#region Draft and accept

		[Test]
		public async Task Drafted_cards_cite_one_section_and_hold_no_number_it_lacks_and_nothing_is_saved_until_accepted()
		{
			var (course, draft) = await DraftCourseCardsAsync();

			draft.Outcome.Should().Be(LmsStudioOutcomes.Drafted);
			draft.Cards.Select(c => c.Front).Should().Equal("What does the driver do when the spotter is out of sight?", "When does everyone on board wear a seatbelt?");
			draft.Dropped.Should().Be(2);
			draft.Cards.Should().OnlyContain(c => c.Anchor.StartsWith("l"), "a course card cites its lesson");
			_world.Cards.Rows.Should().BeEmpty("a draft saves nothing but the audit row");
			_world.Rows.Single().PromptVersion.Should().Be(AiCourseStudioPrompt.CardsVersion);

			var accepted = await _world.Flashcards.AcceptAsync(CourseStudioWorld.DepartmentId, LmsWorld.Admin, course.LmsCourseId, draft.ExchangeId, new[] { 0, 1, 7 });

			accepted.Outcome.Should().Be(LmsStudioOutcomes.Created);
			accepted.Count.Should().Be(2, "an index outside the draft is ignored");
			_world.Cards.Rows.Should().OnlyContain(c => c.Status == (int)LmsFlashcardStatuses.Active && c.SourceVersionId == course.CurrentVersionId &&
				c.SourceKind == (int)LmsFlashcardSourceKinds.CourseVersion && c.SourceItemId == course.LmsCourseId && c.ExchangeId == draft.ExchangeId);
			_world.Cards.Rows.Select(c => c.SourceAnchor).Should().Equal(draft.Cards.Select(c => c.Anchor));

			// Accepting again adds nothing (the same fronts are already there).
			(await _world.Flashcards.AcceptAsync(CourseStudioWorld.DepartmentId, LmsWorld.Admin, course.LmsCourseId, draft.ExchangeId, new[] { 0 })).Count.Should().Be(0);
		}

		[Test]
		public async Task Another_authors_or_a_forged_exchange_cannot_be_accepted()
		{
			var (course, draft) = await DraftCourseCardsAsync();

			(await _world.Flashcards.AcceptAsync(CourseStudioWorld.DepartmentId, LmsWorld.Admin, course.LmsCourseId, Guid.NewGuid().ToString("D"), new[] { 0 })).Outcome
				.Should().Be(LmsStudioOutcomes.NotFound);
			(await _world.Flashcards.AcceptAsync(CourseStudioWorld.DepartmentId, LmsWorld.Admin, course.LmsCourseId, "not-a-guid", new[] { 0 })).Outcome
				.Should().Be(LmsStudioOutcomes.Invalid);
			// The draft belongs to one course: it cannot be accepted onto another.
			var other = await _world.Starters.CloneAsync(_world.Lms.AdminActor, "resgrid-onboarding", "en");
			(await _world.Flashcards.AcceptAsync(CourseStudioWorld.DepartmentId, LmsWorld.Admin, other.LmsCourseId, draft.ExchangeId, new[] { 0 })).Outcome
				.Should().Be(LmsStudioOutcomes.NotFound);
			_world.Cards.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task An_edit_keeps_a_card_true_to_its_section()
		{
			var (course, draft) = await DraftCourseCardsAsync();
			await _world.Flashcards.AcceptAsync(CourseStudioWorld.DepartmentId, LmsWorld.Admin, course.LmsCourseId, draft.ExchangeId, new[] { 0 });
			var card = _world.Cards.Rows.Single();

			(await _world.Flashcards.SaveAsync(CourseStudioWorld.DepartmentId, LmsWorld.Admin, card.LmsFlashcardId, "Spotter out of sight?", "Stop within 2 seconds."))
				.Outcome.Should().Be(LmsStudioOutcomes.Refused, "a number the section does not hold is never added");
			(await _world.Flashcards.SaveAsync(CourseStudioWorld.DepartmentId, LmsWorld.Admin, card.LmsFlashcardId, "Spotter out of sight?", "The driver stops."))
				.Outcome.Should().Be(LmsStudioOutcomes.Created);
			_world.Cards.Rows.Single().Front.Should().Be("Spotter out of sight?");
			(await _world.Flashcards.SaveAsync(CourseStudioWorld.DepartmentId, LmsWorld.Member, card.LmsFlashcardId, "x", "y")).Outcome.Should().Be(LmsStudioOutcomes.NotAllowed);
		}

		#endregion Draft and accept

		#region Supersede, review and the learner's deck

		[Test]
		public async Task A_new_course_version_flags_the_cards_hides_them_from_learners_and_a_review_brings_them_back()
		{
			var (course, draft) = await DraftCourseCardsAsync();
			await _world.Flashcards.AcceptAsync(CourseStudioWorld.DepartmentId, LmsWorld.Admin, course.LmsCourseId, draft.ExchangeId, new[] { 0, 1 });
			var enrollmentId = await EnrollAsync(course.LmsCourseId, LmsWorld.Member);

			var deck = await _world.Flashcards.GetDeckAsync(CourseStudioWorld.DepartmentId, LmsWorld.Member, enrollmentId);
			deck.Should().HaveCount(2);
			deck.Should().OnlyContain(c => c.SourceUrl.StartsWith("/User/Courses/Open/" + course.LmsCourseId + "?lesson="));

			// A new version of the course goes into force: the hook flags every card drafted from the version before it.
			var next = await _world.Lms.CourseService.StartDraftAsync(_world.Lms.AdminActor, course.LmsCourseId);
			next.Succeeded.Should().BeTrue(next.Error.ToString());
			var v2 = await _world.PublishAsync(next.Version.LmsCourseVersionId);
			_world.Cards.Rows.Should().OnlyContain(c => c.Status == (int)LmsFlashcardStatuses.NeedsReview && c.FlagReason == (int)LmsFlashcardFlagReasons.SourceSuperseded && c.FlaggedOn.HasValue);
			(await _world.Flashcards.GetDeckAsync(CourseStudioWorld.DepartmentId, LmsWorld.Member, enrollmentId)).Should().BeEmpty("a flagged card is hidden until reviewed");

			// The review re-confirms the card against the version in force (its lesson found again under the new version's lesson ids).
			var card = _world.Cards.Rows.First();
			var reviewed = await _world.Flashcards.ReviewAsync(CourseStudioWorld.DepartmentId, LmsWorld.Admin, card.LmsFlashcardId);
			reviewed.Outcome.Should().Be(LmsStudioOutcomes.Created, reviewed.Outcome);
			var stored = _world.Cards.Rows.Single(c => c.LmsFlashcardId == card.LmsFlashcardId);
			stored.Status.Should().Be((int)LmsFlashcardStatuses.Active);
			stored.SourceVersionId.Should().Be(v2.LmsCourseVersionId);
			stored.SourceAnchor.Should().NotBe(card.SourceAnchor, "the new version's lesson has a new id");
			stored.ReviewedByUserId.Should().Be(LmsWorld.Admin);
			(await _world.Flashcards.GetDeckAsync(CourseStudioWorld.DepartmentId, LmsWorld.Member, enrollmentId)).Should().ContainSingle(c => c.Id == card.LmsFlashcardId);
		}

		[Test]
		public async Task A_new_protocol_version_flags_only_that_protocols_cards_of_earlier_versions()
		{
			var now = DateTime.UtcNow;
			foreach (var (id, kind, item, version) in new[] { (1, 1, 40, 401), (2, 1, 40, 402), (3, 1, 41, 401), (4, 0, 40, 401) })
				await _world.Cards.InsertAsync(new LmsFlashcard
				{
					LmsFlashcardId = id, DepartmentId = CourseStudioWorld.DepartmentId, LmsCourseId = 9, SourceKind = kind, SourceItemId = item, SourceVersionId = version,
					Front = "F" + id, Back = "B", Status = (int)LmsFlashcardStatuses.Active, CreatedOn = now
				}, CancellationToken.None);

			await new LmsFlashcardSupersedeHook(_world.Cards).OnVersionPublishedAsync(new ContentVersionPublished
			{
				DepartmentId = CourseStudioWorld.DepartmentId, Kind = ContentVersionKinds.ProtocolVersion, ItemId = 40, VersionId = 402, PublishedOnUtc = now
			});

			_world.Cards.Rows.Where(c => c.Status == (int)LmsFlashcardStatuses.NeedsReview).Select(c => c.LmsFlashcardId).Should().Equal(1);
			LmsFlashcardSupersedeHook.SourceKindOf(ContentVersionKinds.CourseVersion).Should().Be((int)LmsFlashcardSourceKinds.CourseVersion);
		}

		[Test]
		public async Task The_deck_is_empty_while_either_gate_is_closed_and_for_anyone_but_the_learner()
		{
			var (course, draft) = await DraftCourseCardsAsync();
			await _world.Flashcards.AcceptAsync(CourseStudioWorld.DepartmentId, LmsWorld.Admin, course.LmsCourseId, draft.ExchangeId, new[] { 0, 1 });
			var enrollmentId = await EnrollAsync(course.LmsCourseId, LmsWorld.Member);

			(await _world.Flashcards.GetDeckAsync(CourseStudioWorld.DepartmentId, LmsWorld.Member2, enrollmentId)).Should().BeEmpty("another member's enrollment");
			_world.Knowledge.Setup(x => x.IsEntitledAsync(CourseStudioWorld.DepartmentId)).ReturnsAsync(false);
			(await _world.Flashcards.GetDeckAsync(CourseStudioWorld.DepartmentId, LmsWorld.Member, enrollmentId)).Should().BeEmpty("flashcards need Enhanced AI");
			_world.Knowledge.Setup(x => x.IsEntitledAsync(CourseStudioWorld.DepartmentId)).ReturnsAsync(true);
			_world.Lms.Gate.AddonActive = false;
			(await _world.Flashcards.GetDeckAsync(CourseStudioWorld.DepartmentId, LmsWorld.Member, enrollmentId)).Should().BeEmpty("flashcards need the LMS");
		}

		[Test]
		public async Task Round_10_a7_the_practice_deck_needs_the_learners_lms_seat()
		{
			var (course, draft) = await DraftCourseCardsAsync();
			await _world.Flashcards.AcceptAsync(CourseStudioWorld.DepartmentId, LmsWorld.Admin, course.LmsCourseId, draft.ExchangeId, new[] { 0, 1 });
			var enrollmentId = await EnrollAsync(course.LmsCourseId, LmsWorld.Member);
			(await _world.Flashcards.GetDeckAsync(CourseStudioWorld.DepartmentId, LmsWorld.Member, enrollmentId)).Should().HaveCount(2);

			_world.Lms.SeatedMembers.Remove(LmsWorld.Member);

			(await _world.Flashcards.GetDeckAsync(CourseStudioWorld.DepartmentId, LmsWorld.Member, enrollmentId)).Should().BeEmpty("a department course's deck is a learner item");
			_world.Cards.Rows.Should().HaveCount(2, "the cards themselves are untouched");
		}

		#endregion Supersede, review and the learner's deck

		#region Never a record

		[Test]
		public async Task Practice_never_writes_a_record_the_deck_writes_nothing()
		{
			var (course, draft) = await DraftCourseCardsAsync();
			await _world.Flashcards.AcceptAsync(CourseStudioWorld.DepartmentId, LmsWorld.Admin, course.LmsCourseId, draft.ExchangeId, new[] { 0, 1 });
			var enrollmentId = await EnrollAsync(course.LmsCourseId, LmsWorld.Member);
			var writes = _world.Cards.Writes;
			var records = _world.Lms.Records.Rows.Count;
			var progress = _world.Lms.Progress.Rows.Count;
			var attempts = _world.Lms.Attempts.Rows.Count;
			var enrollment = _world.Lms.Enrollments.Rows.Single(e => e.LmsEnrollmentId == enrollmentId);
			var before = (enrollment.Status, enrollment.ProgressPercent, enrollment.RowVersion);

			for (var i = 0; i < 3; i++)
				(await _world.Flashcards.GetDeckAsync(CourseStudioWorld.DepartmentId, LmsWorld.Member, enrollmentId)).Should().HaveCount(2);

			_world.Cards.Writes.Should().Be(writes);
			_world.Lms.Records.Rows.Should().HaveCount(records);
			_world.Lms.Progress.Rows.Should().HaveCount(progress);
			_world.Lms.Attempts.Rows.Should().HaveCount(attempts);
			var after = _world.Lms.Enrollments.Rows.Single(e => e.LmsEnrollmentId == enrollmentId);
			(after.Status, after.ProgressPercent, after.RowVersion).Should().Be(before);
		}

		[Test]
		public void The_flashcard_service_holds_no_writer_of_records_certifications_progress_or_attempts()
		{
			var forbidden = new[]
			{
				typeof(ILearningRecordWriter), typeof(ILearningRecordService), typeof(ILmsLearnerService), typeof(ILmsAssessmentService), typeof(ICertificationService),
				typeof(ILearningRecordsRepository), typeof(ILmsLessonProgressRepository), typeof(ILmsAssessmentAttemptsRepository), typeof(ITrainingService)
			};
			var parameters = typeof(LmsFlashcardService).GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).ToList();
			parameters.Should().NotContain(forbidden);
			typeof(LmsFlashcardService).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Select(f => f.FieldType).Should().NotContain(forbidden);
		}

		#endregion Never a record
	}
}
