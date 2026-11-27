using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;

namespace Resgrid.Tests.TrainingT2
{
	/// <summary>
	/// Round 10 A7 (owner decision 2026-11-26, close the LMS seat gap): every learner action on department LMS content needs the acting member's
	/// LMS seat, enforced in the LMS services (so no controller can skip it): enrolling oneself, the catalog's offer, the player (open, progress,
	/// complete, surveys, the SCORM / cmi5 / LTI outcome), assessments (start, an open attempt, submit), crew-viewing scans, lesson files and the
	/// in-progress reminder worker. Kept exactly: the Academy needs no seat; a sponsored member runs only the sponsor's copies; external learners
	/// follow the portal's own rules; the lapse windows still run for who held a seat; managers, graders and reporters act on others by
	/// permission. A removed seat leaves the enrollment read-only (nothing withdrawn or deleted) and own finished results readable.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class LmsLearnerSeatTests
	{
		private const int D = LmsWorld.DepartmentId;
		private LmsWorld _world;

		[SetUp]
		public void SetUp()
		{
			_world = new LmsWorld();
			LmsWorld.ResetAcademySeedMemo();
		}

		[TearDown]
		public void TearDown() => LmsWorld.ResetAcademySeedMemo();

		private void Unseat(string userId) => _world.SeatedMembers.Remove(userId);

		private async Task<LmsEnrollment> EnrollAsync(string userId, int courseId)
		{
			var result = await _world.Learner.EnrollSelfAsync(_world.Actor(userId), courseId);
			result.Succeeded.Should().BeTrue(result.Error.ToString());
			return result.Enrollment;
		}

		/// <summary>An admin assigns the course to the member (a manager action: no seat of the admin's own is asked).</summary>
		private async Task<LmsEnrollment> AssignAsync(string userId, int courseId, DateTime? due = null)
		{
			var outcome = await _world.Learner.AssignAsync(_world.AdminActor, courseId, new[] { new ContentAudienceRule(ContentAudienceKinds.User, userId) }, due);
			outcome.Error.Should().Be(LmsErrors.None);
			return _world.Enrollments.Rows.Single(e => e.LmsCourseId == courseId && e.UserId == userId);
		}

		private static LmsLesson Survey() => new LmsLesson
		{
			Kind = (int)LmsLessonKinds.Survey, Title = "How was it?", IsRequired = false,
			SurveyJson = JsonConvert.SerializeObject(new[] { new LmsSurveyQuestion { Key = "q1", Kind = (int)LmsSurveyQuestionKinds.Rating, Text = "Rate" } })
		};

		#region Enrolment and the catalog

		[Test]
		public async Task A_member_without_a_seat_cannot_enrol_themselves_and_the_catalog_says_why()
		{
			var (course, _, _) = await _world.PublishedCourseAsync("Ladders", LmsWorld.Reading("One"));
			await _world.PublishedCourseAsync("Hose", LmsWorld.Reading("One"));
			Unseat(LmsWorld.Member);

			(await _world.Learner.EnrollSelfAsync(_world.Actor(LmsWorld.Member), course.LmsCourseId)).Error.Should().Be(LmsErrors.NotLicensed);
			_world.Enrollments.Rows.Should().BeEmpty("nothing is written for a refused enrolment");

			var reads = _world.SeatReads;
			var catalog = await _world.Learner.GetCatalogAsync(_world.Actor(LmsWorld.Member));
			catalog.Should().HaveCount(2, "the catalog still lists what the department offers");
			catalog.Should().OnlyContain(e => !e.CanEnroll && e.BlockedBy == LmsErrors.NotLicensed);
			(_world.SeatReads - reads).Should().Be(1, "one seat read per request, not one per course (R-L9)");

			var seated = await _world.Learner.GetCatalogAsync(_world.Actor(LmsWorld.Member2));
			seated.Should().OnlyContain(e => e.CanEnroll && e.BlockedBy == LmsErrors.None);
			(await _world.Learner.EnrollSelfAsync(_world.Actor(LmsWorld.Member2), course.LmsCourseId)).Succeeded.Should().BeTrue();
		}

		[Test]
		public async Task A_manager_taking_a_course_is_a_learner_too_but_assigning_needs_only_the_permission()
		{
			var (course, _, _) = await _world.PublishedCourseAsync("Command", LmsWorld.Reading("One"));
			Unseat(LmsWorld.Admin);

			(await _world.Learner.EnrollSelfAsync(_world.AdminActor, course.LmsCourseId)).Error.Should().Be(LmsErrors.NotLicensed, "being trained uses a seat, whoever it is");
			var outcome = await _world.Learner.AssignAsync(_world.AdminActor, course.LmsCourseId, new[] { new ContentAudienceRule(ContentAudienceKinds.User, LmsWorld.Member) }, null);
			outcome.Error.Should().Be(LmsErrors.None, "assigning others needs Assign rights, never a seat of the admin's own");
			outcome.Created.Should().Be(1);
		}

		#endregion Enrolment and the catalog

		#region The player

		[Test]
		public async Task Every_player_action_on_a_department_course_is_refused_without_a_seat()
		{
			var (course, _, lessons) = await _world.PublishedCourseAsync("Ventilation",
				LmsWorld.Reading("Read"), new LmsLesson { Kind = (int)LmsLessonKinds.Scorm, Title = "Package", IsRequired = false }, Survey());
			var enrollment = await AssignAsync(LmsWorld.Member, course.LmsCourseId);
			Unseat(LmsWorld.Member);
			var id = enrollment.LmsEnrollmentId;

			var player = await _world.Learner.GetPlayerAsync(D, LmsWorld.Member, id, null);
			player.Error.Should().Be(LmsErrors.NotLicensed);
			player.Lesson.Should().BeNull("no lesson opens");
			player.Outline.Should().NotBeNull("the enrollment stays readable");

			(await _world.Learner.SaveProgressAsync(D, LmsWorld.Member, id, lessons[0].LmsLessonId, new LmsProgressInput { Seconds = 30 })).Error.Should().Be(LmsErrors.NotLicensed);
			(await _world.Learner.CompleteLessonAsync(D, LmsWorld.Member, id, lessons[0].LmsLessonId, null)).Error.Should().Be(LmsErrors.NotLicensed);
			(await _world.Learner.SubmitSurveyAsync(D, LmsWorld.Member, id, lessons[2].LmsLessonId, new Dictionary<string, string> { ["q1"] = "4" })).Error.Should().Be(LmsErrors.NotLicensed);
			(await _world.Learner.RecordRuntimeOutcomeAsync(D, LmsWorld.Member, id, lessons[1].LmsLessonId, true, 120)).Error.Should().Be(LmsErrors.NotLicensed,
				"a SCORM, cmi5 or LTI outcome is a learner action too");

			_world.Progress.Rows.Should().BeEmpty();
			_world.Surveys.Rows.Should().BeEmpty();
			_world.Records.Live.Should().BeEmpty();
		}

		[Test]
		public async Task A_seated_member_runs_the_course_as_before()
		{
			var (course, _, lessons) = await _world.PublishedCourseAsync("Ventilation", LmsWorld.Reading("Read"));
			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);

			(await _world.Learner.GetPlayerAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, null)).Error.Should().Be(LmsErrors.None);
			var done = await _world.Learner.CompleteLessonAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, null);
			done.Succeeded.Should().BeTrue();
			done.Count.Should().Be(1, "the course completed");
			_world.Records.Live.Should().ContainSingle();
		}

		[Test]
		public async Task A_removed_seat_leaves_the_enrollment_read_only_and_a_restored_one_resumes_it()
		{
			var (course, _, lessons) = await _world.PublishedCourseAsync("Pumps", LmsWorld.Reading("One"), LmsWorld.Reading("Two"));
			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);
			(await _world.Learner.CompleteLessonAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, null)).Succeeded.Should().BeTrue();

			Unseat(LmsWorld.Member);

			var stored = _world.Enrollments.Rows.Single();
			stored.IsOpen.Should().BeTrue("nothing is withdrawn when a seat is removed");
			stored.Status.Should().Be((int)LmsEnrollmentStatuses.InProgress);
			var mine = (await _world.Learner.GetMyCoursesAsync(D, LmsWorld.Member)).Should().ContainSingle().Subject;
			mine.CanRun.Should().BeFalse();
			mine.BlockedBy.Should().Be(LmsErrors.NotLicensed);
			var readOnly = await _world.Learner.GetPlayerAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, null);
			readOnly.Error.Should().Be(LmsErrors.NotLicensed);
			readOnly.Progress.Should().ContainKey(lessons[0].LmsLessonId, "the member still sees their own progress");
			readOnly.Progress[lessons[0].LmsLessonId].Status.Should().Be((int)LmsLessonProgressStatuses.Completed);
			(await _world.Learner.CompleteLessonAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[1].LmsLessonId, null)).Error.Should().Be(LmsErrors.NotLicensed);
			_world.Progress.Rows.Should().ContainSingle("the earlier progress is kept, nothing new is added");

			_world.SeatedMembers.Add(LmsWorld.Member);
			var resumed = await _world.Learner.CompleteLessonAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[1].LmsLessonId, null);
			resumed.Succeeded.Should().BeTrue("the same enrollment resumes where it stopped");
			resumed.Count.Should().Be(1);
		}

		[Test]
		public async Task My_courses_lists_everything_with_one_seat_read_and_the_academy_runs_without_a_seat()
		{
			var (first, _, _) = await _world.PublishedCourseAsync("First", LmsWorld.Reading("One"));
			var (second, _, _) = await _world.PublishedCourseAsync("Second", LmsWorld.Reading("One"));
			await AssignAsync(LmsWorld.Member, first.LmsCourseId);
			await AssignAsync(LmsWorld.Member, second.LmsCourseId);
			(await _world.Academy.AssignLessonAsync(_world.Actor(LmsWorld.Admin, assign: true), "00-start", new[] { new ContentAudienceRule(ContentAudienceKinds.User, LmsWorld.Member) }, null))
				.Created.Should().Be(1);
			Unseat(LmsWorld.Member);

			var reads = _world.SeatReads;
			var mine = await _world.Learner.GetMyCoursesAsync(D, LmsWorld.Member);

			mine.Should().HaveCount(3);
			mine.Where(c => !c.Course.IsSystem).Should().OnlyContain(c => !c.CanRun && c.BlockedBy == LmsErrors.NotLicensed);
			mine.Single(c => c.Course.IsSystem).CanRun.Should().BeTrue("the Resgrid Academy is free: no seat (R-S2)");
			(_world.SeatReads - reads).Should().Be(1);

			var academy = mine.Single(c => c.Course.IsSystem).Enrollment;
			(await _world.Learner.GetPlayerAsync(D, LmsWorld.Member, academy.LmsEnrollmentId, null)).Error.Should().Be(LmsErrors.None);
		}

		[Test]
		public async Task A_member_enrols_in_the_academy_without_a_seat()
		{
			await _world.Academy.EnsureSeededAsync();
			var lesson = await _world.Academy.GetCourseForLessonAsync("00-start");
			Unseat(LmsWorld.Member);

			var enrolled = await _world.Learner.EnrollSelfAsync(_world.Actor(LmsWorld.Member), lesson.LmsCourseId);

			enrolled.Succeeded.Should().BeTrue(enrolled.Error.ToString());
			(await _world.Learner.GetPlayerAsync(D, LmsWorld.Member, enrolled.Enrollment.LmsEnrollmentId, null)).Error.Should().Be(LmsErrors.None);
		}

		[Test]
		public async Task An_external_learner_follows_the_portals_rules_and_is_never_asked_for_a_member_seat()
		{
			var (course, _, lessons) = await _world.PublishedCourseAsync("Open course", LmsWorld.Reading("One"));
			var learner = LmsExternalLearnerIds.New();
			_world.SeatedMembers.Should().NotContain(learner);

			var enrolled = await _world.Learner.EnrollExternalAsync(D, course.LmsCourseId, learner, null, true);
			enrolled.Succeeded.Should().BeTrue(enrolled.Error.ToString());
			(await _world.Learner.GetPlayerAsync(D, learner, enrolled.Enrollment.LmsEnrollmentId, null)).Error.Should().Be(LmsErrors.None);
			(await _world.Learner.CompleteLessonAsync(D, learner, enrolled.Enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, null)).Succeeded.Should().BeTrue();
		}

		#endregion The player

		#region Sponsored seats and lapse

		[Test]
		public async Task A_sponsored_member_runs_only_the_sponsors_copies_and_only_with_a_seat()
		{
			var (copy, _, _) = await _world.PublishedCourseAsync("Sponsor's course", LmsWorld.Reading("One"));
			var (own, _, _) = await _world.PublishedCourseAsync("Our own course", LmsWorld.Reading("One"));
			// No add-on of its own: the department runs its managed copy of the sponsor's set on the sponsor's add-on (round 10 A3).
			_world.Gate = new LmsDepartmentGate
			{
				DepartmentId = D, ModuleOn = true, FlagOn = true, AddonActive = false, SponsoredCourses = new Dictionary<int, DateTime?> { [copy.LmsCourseId] = null }
			};

			var enrolled = await _world.Learner.EnrollSelfAsync(_world.Actor(LmsWorld.Member), copy.LmsCourseId);
			enrolled.Succeeded.Should().BeTrue(enrolled.Error.ToString());
			(await _world.Learner.GetPlayerAsync(D, LmsWorld.Member, enrolled.Enrollment.LmsEnrollmentId, null)).Error.Should().Be(LmsErrors.None);
			(await _world.Learner.EnrollSelfAsync(_world.Actor(LmsWorld.Member), own.LmsCourseId)).Error.Should().Be(LmsErrors.AddonRequired,
				"a sponsored seat runs the sponsor's managed copies only");

			Unseat(LmsWorld.Member2);
			(await _world.Learner.EnrollSelfAsync(_world.Actor(LmsWorld.Member2), copy.LmsCourseId)).Error.Should().Be(LmsErrors.NotLicensed,
				"a member the sponsorship does not seat");
			Unseat(LmsWorld.Member);
			(await _world.Learner.GetPlayerAsync(D, LmsWorld.Member, enrolled.Enrollment.LmsEnrollmentId, null)).Error.Should().Be(LmsErrors.NotLicensed);
		}

		[Test]
		public async Task During_a_lapse_assigned_work_runs_for_who_held_a_seat_and_the_lapse_window_still_ends()
		{
			var (course, _, _) = await _world.PublishedCourseAsync("Lapsing", LmsWorld.Reading("One"));
			var held = await AssignAsync(LmsWorld.Member, course.LmsCourseId, DateTime.UtcNow.AddDays(20));
			var unseated = await AssignAsync(LmsWorld.Member2, course.LmsCourseId, DateTime.UtcNow.AddDays(20));
			held.AssignedOn = unseated.AssignedOn = DateTime.UtcNow.AddDays(-10);
			Unseat(LmsWorld.Member2);

			_world.Gate = new LmsDepartmentGate { DepartmentId = D, ModuleOn = true, FlagOn = true, AddonActive = false, LastEntitledOn = DateTime.UtcNow.AddDays(-5) };

			(await _world.Learner.GetPlayerAsync(D, LmsWorld.Member, held.LmsEnrollmentId, null)).Error.Should().Be(LmsErrors.None, "lapse rule 4, as built");
			(await _world.Learner.GetPlayerAsync(D, LmsWorld.Member2, unseated.LmsEnrollmentId, null)).Error.Should().Be(LmsErrors.NotLicensed);

			held.DueOn = DateTime.UtcNow.AddDays(-1);
			(await _world.Learner.GetPlayerAsync(D, LmsWorld.Member, held.LmsEnrollmentId, null)).Error.Should().Be(LmsErrors.LapseWindowEnded,
				"the lapse rules answer first; a seat never extends a window");
		}

		#endregion Sponsored seats and lapse

		#region Assessments

		[Test]
		public async Task Assessments_need_a_seat_to_start_answer_or_submit_while_finished_results_stay_readable()
		{
			var bank = await _world.BankAsync(LmsWorld.Single("Nozzle?", "a", "a", "b"), LmsWorld.Single("Friction?", "b", "a", "b"));
			var (course, _, lessons) = await _world.PublishedCourseAsync("Hose", LmsWorld.Quiz(bank.LmsQuestionBankId, 100), LmsWorld.Quiz(bank.LmsQuestionBankId, 100));
			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);

			// A finished (failed) attempt, then an open one.
			var first = await _world.Assessment.StartAttemptAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, false, null, null, null);
			(await _world.Assessment.SubmitAttemptAsync(D, LmsWorld.Member, first.Attempt.LmsAssessmentAttemptId, _world.Answers(first.Attempt.LmsAssessmentAttemptId, false)))
				.Attempt.Passed.Should().BeFalse();
			var open = await _world.Assessment.StartAttemptAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, false, null, null, null);
			open.Error.Should().Be(LmsErrors.None);

			Unseat(LmsWorld.Member);

			(await _world.Assessment.StartAttemptAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, false, null, null, null)).Error
				.Should().Be(LmsErrors.NotLicensed);
			var shown = await _world.Assessment.GetAttemptAsync(D, LmsWorld.Member, open.Attempt.LmsAssessmentAttemptId);
			shown.Error.Should().Be(LmsErrors.NotLicensed);
			shown.Questions.Should().BeEmpty("no questions go out without a seat");
			var refused = await _world.Assessment.SubmitAttemptAsync(D, LmsWorld.Member, open.Attempt.LmsAssessmentAttemptId,
				_world.Answers(open.Attempt.LmsAssessmentAttemptId, true));
			refused.Error.Should().Be(LmsErrors.NotLicensed);
			var stored = _world.Attempts.Rows.Single(a => a.LmsAssessmentAttemptId == open.Attempt.LmsAssessmentAttemptId);
			stored.Status.Should().Be((int)LmsAttemptStatuses.InProgress, "nothing was scored");
			stored.AnswersJson.Should().BeNull();

			var result = await _world.Assessment.GetResultAsync(_world.Actor(LmsWorld.Member), first.Attempt.LmsAssessmentAttemptId);
			result.Error.Should().Be(LmsErrors.None, "the member's own finished attempt is their record, readable without a seat");
			(await _world.Assessment.GetAttemptAsync(D, LmsWorld.Member, first.Attempt.LmsAssessmentAttemptId)).Error.Should().Be(LmsErrors.AlreadySubmitted);
		}

		[Test]
		public async Task A_grader_without_a_seat_grades_and_the_member_is_credited()
		{
			var essay = new LmsQuestion { Kind = (int)LmsQuestionKinds.ShortAnswer, Text = "Describe a size-up.", Points = 2, Explanation = "Model answer" };
			var bank = await _world.BankAsync(LmsWorld.Single("Command?", "a", "a", "b"), essay);
			var (course, _, lessons) = await _world.PublishedCourseAsync("Command", LmsWorld.Quiz(bank.LmsQuestionBankId, 80));
			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);
			var view = await _world.Assessment.StartAttemptAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, false, null, null, null);
			(await _world.Assessment.SubmitAttemptAsync(D, LmsWorld.Member, view.Attempt.LmsAssessmentAttemptId, _world.Answers(view.Attempt.LmsAssessmentAttemptId, true)))
				.Attempt.Status.Should().Be((int)LmsAttemptStatuses.PendingGrading);
			Unseat(LmsWorld.Grader);

			var grader = _world.Actor(LmsWorld.Grader, grade: true);
			var item = (await _world.Assessment.GetGradingQueueAsync(grader)).Should().ContainSingle().Subject;
			var graded = await _world.Assessment.GradeAsync(grader, view.Attempt.LmsAssessmentAttemptId,
				new[] { new LmsManualGrade { QuestionId = item.Questions[0].QuestionId, Awarded = 2 } }, "Good");

			graded.Error.Should().Be(LmsErrors.None, "grading needs the Grade Assessments permission, never a seat");
			graded.CourseCompleted.Should().BeTrue();
			_world.Records.Live.Should().ContainSingle();
		}

		#endregion Assessments

		#region Lesson files, surveys, crew viewing and the reminder worker

		[Test]
		public async Task The_members_own_lesson_file_needs_their_seat_while_graders_and_reporters_read_it_by_permission()
		{
			var upload = new LmsLesson { Kind = (int)LmsLessonKinds.Blocks, Title = "Upload", ContentHtml = "<p>Upload</p>", IsRequired = true, FileRequestPrompt = "Your card" };
			var (course, _, lessons) = await _world.PublishedCourseAsync("Cards", upload);
			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);
			(await _world.Learner.CompleteLessonAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId,
				new LmsLessonCompletionInput { FileData = new byte[] { 37, 80, 68, 70 }, FileName = "card.pdf", ContentType = "application/pdf" })).Succeeded.Should().BeTrue();
			var file = _world.Files.Rows.Single();

			(await _world.Learner.GetLessonFileAsync(_world.Actor(LmsWorld.Member), file.LmsLessonFileId)).Should().NotBeNull();
			(await _world.Learner.GetMyLessonFileAsync(D, LmsWorld.Member, file.LmsLessonFileId)).Should().NotBeNull();

			Unseat(LmsWorld.Member);
			Unseat(LmsWorld.Grader);
			Unseat(LmsWorld.Admin);
			(await _world.Learner.GetLessonFileAsync(_world.Actor(LmsWorld.Member), file.LmsLessonFileId)).Should().BeNull();
			(await _world.Learner.GetMyLessonFileAsync(D, LmsWorld.Member, file.LmsLessonFileId)).Should().BeNull();
			(await _world.Learner.GetLessonFileAsync(_world.Actor(LmsWorld.Grader, grade: true), file.LmsLessonFileId)).Should().NotBeNull("graders need no seat");
			(await _world.Learner.GetLessonFileAsync(_world.Actor(LmsWorld.Admin, manage: true), file.LmsLessonFileId)).Should().NotBeNull("the course's reporters need no seat");
			(await _world.Learner.GetLessonFileAsync(_world.Actor(LmsWorld.Member2), file.LmsLessonFileId)).Should().BeNull("another member's upload, as before");
		}

		[Test]
		public async Task A_course_reporter_reads_the_survey_report_without_a_seat()
		{
			var (course, _, lessons) = await _world.PublishedCourseAsync("Evaluated", LmsWorld.Reading("Content"), Survey());
			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);
			await _world.Learner.CompleteLessonAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, null);
			(await _world.Learner.SubmitSurveyAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[1].LmsLessonId, new Dictionary<string, string> { ["q1"] = "4" }))
				.Succeeded.Should().BeTrue();
			Unseat(LmsWorld.Admin);

			(await _world.Learner.GetSurveyReportAsync(_world.AdminActor, lessons[1].LmsLessonId)).Should().NotBeNull();
		}

		[Test]
		public async Task A_crew_viewing_scan_without_a_seat_is_refused_and_leaves_no_trace()
		{
			var (course, _, lessons) = await _world.PublishedCourseAsync("Extrication", LmsWorld.Reading("Video together", 300));
			Unseat(LmsWorld.Admin);
			var started = await _world.Crew.StartAsync(_world.AdminActor, course.LmsCourseId, lessons[0].LmsLessonId);
			started.Succeeded.Should().BeTrue("starting a crew viewing is teaching: Assign rights, no seat");
			var viewing = (LmsCrewViewing)started.Value;
			var code = _world.Crew.CurrentCode(viewing, DateTime.UtcNow);
			Unseat(LmsWorld.Member2);

			(await _world.Crew.ScanAsync(D, LmsWorld.Member2, viewing.LmsCrewViewingId, code, "10.1.1.2")).Error.Should().Be(LmsErrors.NotLicensed);
			_world.Enrollments.Rows.Should().NotContain(e => e.UserId == LmsWorld.Member2, "no enrollment is created");
			(await _world.Crew.GetAttendeesAsync(D, viewing.LmsCrewViewingId)).Should().BeEmpty("nobody was marked present");

			(await _world.Crew.ScanAsync(D, LmsWorld.Member, viewing.LmsCrewViewingId, code, "10.1.1.1")).Succeeded.Should().BeTrue();
			(await _world.Crew.GetAttendeesAsync(D, viewing.LmsCrewViewingId)).Select(a => a.UserId).Should().BeEquivalentTo(new[] { LmsWorld.Member });
		}

		[Test]
		public async Task The_in_progress_reminder_skips_a_member_without_a_seat_with_one_seat_read_per_department()
		{
			var (course, _, lessons) = await _world.PublishedCourseAsync("Idle", LmsWorld.Reading("One"), LmsWorld.Reading("Two"));
			foreach (var user in new[] { LmsWorld.Member, LmsWorld.Member2 })
			{
				var enrollment = await EnrollAsync(user, course.LmsCourseId);
				await _world.Learner.SaveProgressAsync(D, user, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, new LmsProgressInput { Seconds = 10 });
				var stored = _world.Enrollments.Rows.Single(e => e.LmsEnrollmentId == enrollment.LmsEnrollmentId);
				stored.Status.Should().Be((int)LmsEnrollmentStatuses.InProgress);
				stored.LastActivityOn = DateTime.UtcNow.AddDays(-30);
			}
			Unseat(LmsWorld.Member2);

			var reads = _world.SeatReads;
			await _world.Learner.SendInProgressRemindersAsync(DateTime.UtcNow);

			_world.Enrollments.Rows.Single(e => e.UserId == LmsWorld.Member).ReminderSentOn.Should().NotBeNull("a seated member is reminded");
			_world.Enrollments.Rows.Single(e => e.UserId == LmsWorld.Member2).ReminderSentOn.Should().BeNull("no reminder for a course the member cannot run");
			(_world.SeatReads - reads).Should().Be(1, "one seat read for the department (R-L9)");
		}

		#endregion Lesson files, surveys, crew viewing and the reminder worker
	
		#region Assignment (§17.4: a member without a seat cannot be enrolled)

		[Test]
		public async Task Assigning_a_department_course_skips_a_member_without_a_seat_as_not_licensed_and_tells_only_the_enrolled()
		{
			var (course, _, _) = await _world.PublishedCourseAsync("Ladders", LmsWorld.Reading("One"));
			Unseat(LmsWorld.Member2);
			Unseat(LmsWorld.Admin);

			var outcome = await _world.Learner.AssignDirectAsync(_world.AdminActor, course.LmsCourseId, new[] { new ContentAudienceRule(ContentAudienceKinds.Group, "3") }, null);

			outcome.Error.Should().Be(LmsErrors.None, "assigning needs Assign rights, never a seat of the admin's own");
			outcome.Created.Should().Be(1);
			outcome.CreatedUserIds.Should().Equal(LmsWorld.Member);
			outcome.Skipped.Should().ContainSingle(s => s.UserId == LmsWorld.Member2 && s.Reason == ContentAudienceSkipReasons.NotLicensed);
			outcome.NotLicensed.Should().Be(1);
			_world.Enrollments.Rows.Should().ContainSingle().Which.UserId.Should().Be(LmsWorld.Member, "nothing is written for the member without a seat");
			_world.CourseEvents.Should().NotContain(e => e.UserId == LmsWorld.Member2, "the skipped member is never announced or notified");

			// Once a seat is enabled, the next assignment enrols them.
			_world.SeatedMembers.Add(LmsWorld.Member2);
			(await _world.Learner.AssignDirectAsync(_world.AdminActor, course.LmsCourseId, new[] { new ContentAudienceRule(ContentAudienceKinds.User, LmsWorld.Member2) }, null))
				.Created.Should().Be(1);
		}

		[Test]
		public async Task Academy_lessons_and_role_paths_are_assigned_without_a_seat()
		{
			_world.SeatedMembers.Clear();
			var reads = _world.SeatReads;

			var lesson = await _world.Academy.AssignLessonAsync(_world.Actor(LmsWorld.Admin, assign: true), "00-start",
				new[] { new ContentAudienceRule(ContentAudienceKinds.User, LmsWorld.Member) }, null);
			var path = await _world.Academy.AssignPathAsync(_world.Actor(LmsWorld.Admin, assign: true), AcademyCourseManifest.RolePaths[0].Key,
				new[] { new ContentAudienceRule(ContentAudienceKinds.User, LmsWorld.Member2) }, null);

			lesson.Created.Should().Be(1, "the Resgrid Academy is free (R-S2)");
			lesson.NotLicensed.Should().Be(0);
			path.Created.Should().BeGreaterThan(0);
			path.NotLicensed.Should().Be(0);
			(_world.SeatReads - reads).Should().Be(0, "Academy assignments never ask for seats");
		}

		[Test]
		public async Task A_caller_that_already_read_the_seats_hands_them_over_and_nothing_is_read_again()
		{
			var (course, _, _) = await _world.PublishedCourseAsync("Ladders", LmsWorld.Reading("One"));
			var actor = _world.AdminActor;
			actor.SeatHolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { LmsWorld.Member };
			var reads = _world.SeatReads;

			var outcome = await _world.Learner.AssignAsync(actor, course.LmsCourseId, new[] { new ContentAudienceRule(ContentAudienceKinds.Group, "3") }, null);

			outcome.Created.Should().Be(1);
			outcome.NotLicensed.Should().Be(1);
			(_world.SeatReads - reads).Should().Be(0, "a worker run reads the seats once per department (R-L9)");
		}

		#endregion Assignment (§17.4: a member without a seat cannot be enrolled)
}
}
