using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;

namespace Resgrid.Tests.TrainingT2
{
	/// <summary>Training T2 and A1 through the services: lifecycle, learners, assessments, grading, crew viewing, media and the Academy.</summary>
	[TestFixture, NonParallelizable]
	public class LmsServiceTests
	{
		private LmsWorld _world;
		private const int D = LmsWorld.DepartmentId;

		[SetUp]
		public void SetUp()
		{
			_world = new LmsWorld();
			LmsWorld.ResetAcademySeedMemo();
		}

		[TearDown]
		public void TearDown() => LmsWorld.ResetAcademySeedMemo();

		private async Task<LmsEnrollment> EnrollAsync(string userId, int courseId)
		{
			var result = await _world.Learner.EnrollSelfAsync(_world.Actor(userId), courseId);
			result.Succeeded.Should().BeTrue(result.Error.ToString());
			return result.Enrollment;
		}

		#region Content-core lifecycle

		private async Task<LmsCourseVersion> ApproveAndPublishAsync(int versionId, bool major = false, LmsMajorChangePolicies policy = LmsMajorChangePolicies.Nothing, int? due = null)
		{
			var admin = _world.AdminActor;
			var submitted = await _world.CourseService.SubmitAsync(admin, versionId, "Change summary");
			submitted.Succeeded.Should().BeTrue(submitted.Error + " " + submitted.LifecycleError);
			var approved = await _world.CourseService.ApproveAsync(admin, versionId, "ok", new SignatureProof { UserId = LmsWorld.Admin, StepUpVerifiedOnUtc = DateTime.UtcNow });
			approved.Succeeded.Should().BeTrue(approved.Error + " " + approved.LifecycleError);
			var published = await _world.CourseService.PublishAsync(admin, versionId, major, null, policy, due);
			published.Succeeded.Should().BeTrue(published.Error + " " + published.LifecycleError);
			return published.Version;
		}

		[Test]
		public async Task A_course_moves_through_the_protocol_content_core_lifecycle()
		{
			var admin = _world.AdminActor;
			var created = await _world.CourseService.CreateAsync(admin, new LmsCourseInput { Title = "Pump operations", IsSelfEnrollable = true });
			created.Succeeded.Should().BeTrue();
			var draft = created.Version;
			draft.Status.Should().Be((int)ProtocolVersionStatuses.Draft);

			// An empty draft cannot be submitted (the content core's EmptyContent).
			(await _world.CourseService.SubmitAsync(admin, draft.LmsCourseVersionId, "First")).LifecycleError.Should().Be(ProtocolLifecycleErrors.EmptyContent);

			var module = _world.Modules.Rows.Single(m => m.LmsCourseVersionId == draft.LmsCourseVersionId);
			var lesson = await _world.CourseService.SaveLessonAsync(admin, new LmsLessonInput
			{
				LmsCourseVersionId = draft.LmsCourseVersionId, LmsCourseModuleId = module.LmsCourseModuleId, Kind = (int)LmsLessonKinds.Blocks, Title = "Intro", ContentHtml = "<p>Pressure</p>"
			});
			lesson.Succeeded.Should().BeTrue(lesson.Error.ToString());

			// Publishing needs an approved version; approving needs one in review.
			(await _world.CourseService.PublishAsync(admin, draft.LmsCourseVersionId, false, "x", LmsMajorChangePolicies.Nothing, null)).LifecycleError
				.Should().Be(ProtocolLifecycleErrors.NotApproved);
			(await _world.CourseService.ApproveAsync(admin, draft.LmsCourseVersionId, null, new SignatureProof { UserId = LmsWorld.Admin })).LifecycleError
				.Should().Be(ProtocolLifecycleErrors.NotInReview);

			(await _world.CourseService.SubmitAsync(admin, draft.LmsCourseVersionId, "First version")).Succeeded.Should().BeTrue();
			_world.Versions.Rows.Single(v => v.LmsCourseVersionId == draft.LmsCourseVersionId).Status.Should().Be((int)ProtocolVersionStatuses.InReview);

			// In review the content is frozen (ProtocolLifecycle.IsContentEditable).
			(await _world.CourseService.SaveLessonAsync(admin, new LmsLessonInput
			{
				LmsCourseVersionId = draft.LmsCourseVersionId, LmsCourseModuleId = module.LmsCourseModuleId, Kind = (int)LmsLessonKinds.Blocks, Title = "More", ContentHtml = "<p>x</p>"
			})).Error.Should().Be(LmsErrors.NotDraft);

			// The approval is an L2 signature bound to the course content hash.
			(await _world.CourseService.ApproveAsync(admin, draft.LmsCourseVersionId, null, null)).LifecycleError.Should().Be(ProtocolLifecycleErrors.SignatureRequired);
			var approved = await _world.CourseService.ApproveAsync(admin, draft.LmsCourseVersionId, "Looks right", new SignatureProof { UserId = LmsWorld.Admin, StepUpVerifiedOnUtc = DateTime.UtcNow });
			approved.Succeeded.Should().BeTrue();
			approved.Signature.ArtifactSha256.Should().Be(_world.Versions.Rows.Single(v => v.LmsCourseVersionId == draft.LmsCourseVersionId).ContentHash);
			approved.Signature.SubjectKind.Should().Be((int)SignatureSubjectKinds.CourseVersion);
			approved.Signature.Level.Should().Be((int)SignatureLevels.StepUp);

			var published = await _world.CourseService.PublishAsync(admin, draft.LmsCourseVersionId, false, null, LmsMajorChangePolicies.Nothing, null);
			published.Succeeded.Should().BeTrue();
			published.Version.Major.Should().Be(1);
			published.Version.Minor.Should().Be(0);
			_world.Courses.Rows.Single().CurrentVersionId.Should().Be(draft.LmsCourseVersionId);
			_world.Courses.Rows.Single().Status.Should().Be((int)ProtocolStatuses.Published);

			// A second draft publishes as 1.1 and supersedes 1.0.
			var second = await _world.CourseService.StartDraftAsync(admin, created.Course.LmsCourseId);
			second.Succeeded.Should().BeTrue();
			(await _world.CourseService.StartDraftAsync(admin, created.Course.LmsCourseId)).LifecycleError.Should().Be(ProtocolLifecycleErrors.DraftExists);
			var v2 = await ApproveAndPublishAsync(second.Version.LmsCourseVersionId);
			v2.Label.Should().Be("1.1");
			_world.Versions.Rows.Single(v => v.LmsCourseVersionId == draft.LmsCourseVersionId).Status.Should().Be((int)ProtocolVersionStatuses.Superseded);
		}

		[Test]
		public async Task A_learner_keeps_the_version_started_and_unstarted_learners_move_to_the_new_one()
		{
			var admin = _world.AdminActor;
			var created = await _world.CourseService.CreateAsync(admin, new LmsCourseInput { Title = "Hazmat awareness", IsSelfEnrollable = true });
			var module = _world.Modules.Rows.Single(m => m.LmsCourseVersionId == created.Version.LmsCourseVersionId);
			await _world.CourseService.SaveLessonAsync(admin, new LmsLessonInput
			{
				LmsCourseVersionId = created.Version.LmsCourseVersionId, LmsCourseModuleId = module.LmsCourseModuleId, Kind = (int)LmsLessonKinds.Blocks, Title = "Placards", ContentHtml = "<p>Read</p>"
			});
			var v1 = await ApproveAndPublishAsync(created.Version.LmsCourseVersionId);

			var started = await EnrollAsync(LmsWorld.Member, created.Course.LmsCourseId);
			var waiting = await EnrollAsync(LmsWorld.Member2, created.Course.LmsCourseId);
			var lessonId = _world.Lessons.Rows.Single(l => l.LmsCourseVersionId == v1.LmsCourseVersionId).LmsLessonId;
			(await _world.Learner.SaveProgressAsync(D, LmsWorld.Member, started.LmsEnrollmentId, lessonId, new LmsProgressInput { Seconds = 10 })).Succeeded.Should().BeTrue();

			var draft = await _world.CourseService.StartDraftAsync(admin, created.Course.LmsCourseId);
			var v2 = await ApproveAndPublishAsync(draft.Version.LmsCourseVersionId);

			_world.Enrollments.Rows.Single(e => e.LmsEnrollmentId == started.LmsEnrollmentId).LmsCourseVersionId.Should().Be(v1.LmsCourseVersionId, "a learner keeps the version they started");
			_world.Enrollments.Rows.Single(e => e.LmsEnrollmentId == waiting.LmsEnrollmentId).LmsCourseVersionId.Should().Be(v2.LmsCourseVersionId);

			// The started learner still plays and completes the old version.
			var player = await _world.Learner.GetPlayerAsync(D, LmsWorld.Member, started.LmsEnrollmentId, null);
			player.Error.Should().Be(LmsErrors.None);
			player.Outline.Version.LmsCourseVersionId.Should().Be(v1.LmsCourseVersionId);
			(await _world.Learner.CompleteLessonAsync(D, LmsWorld.Member, started.LmsEnrollmentId, lessonId, null)).Count.Should().Be(1);
			(await _world.Learner.IsCourseVersionPassedAsync(LmsWorld.Member, v1.LmsCourseVersionId)).Should().BeTrue();
			(await _world.Learner.IsCourseVersionPassedAsync(LmsWorld.Member, v2.LmsCourseVersionId)).Should().BeFalse();
		}

		[Test]
		public async Task A_major_version_with_the_reassign_policy_reassigns_completed_learners()
		{
			var admin = _world.AdminActor;
			var created = await _world.CourseService.CreateAsync(admin, new LmsCourseInput { Title = "SCBA", IsSelfEnrollable = true });
			var module = _world.Modules.Rows.Single(m => m.LmsCourseVersionId == created.Version.LmsCourseVersionId);
			await _world.CourseService.SaveLessonAsync(admin, new LmsLessonInput
			{
				LmsCourseVersionId = created.Version.LmsCourseVersionId, LmsCourseModuleId = module.LmsCourseModuleId, Kind = (int)LmsLessonKinds.Blocks, Title = "Donning", ContentHtml = "<p>Read</p>"
			});
			var v1 = await ApproveAndPublishAsync(created.Version.LmsCourseVersionId);
			var enrollment = await EnrollAsync(LmsWorld.Member, created.Course.LmsCourseId);
			var lessonId = _world.Lessons.Rows.Single(l => l.LmsCourseVersionId == v1.LmsCourseVersionId).LmsLessonId;
			await _world.Learner.CompleteLessonAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessonId, null);

			var draft = await _world.CourseService.StartDraftAsync(admin, created.Course.LmsCourseId);
			var v2 = await ApproveAndPublishAsync(draft.Version.LmsCourseVersionId, true, LmsMajorChangePolicies.Reassign, 14);

			v2.Label.Should().Be("2.0");
			var reassigned = _world.Enrollments.Rows.Single(e => e.IsOpen && e.UserId == LmsWorld.Member);
			reassigned.LmsCourseVersionId.Should().Be(v2.LmsCourseVersionId);
			reassigned.Source.Should().Be((int)LmsEnrollmentSources.Reassigned);
			reassigned.DueOn.Should().BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromMinutes(1));
		}

		#endregion Content-core lifecycle

		#region Player, seat time and completion

		[Test]
		public async Task Seat_time_is_capped_by_the_clock_and_the_player_resumes_where_the_learner_stopped()
		{
			var (course, _, lessons) = await _world.PublishedCourseAsync("Ladders", LmsWorld.Reading("Raise", 60), LmsWorld.Reading("Climb"));
			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);
			var first = lessons[0].LmsLessonId;
			var second = lessons[1].LmsLessonId;

			(await _world.Learner.GetPlayerAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, second)).Lesson.LmsLessonId.Should().Be(first, "the second lesson is locked behind the first");

			await _world.Learner.SaveProgressAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, first, new LmsProgressInput { Seconds = 600 });
			await _world.Learner.SaveProgressAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, first, new LmsProgressInput { Seconds = 600 });
			var progress = _world.Progress.Rows.Single(p => p.LmsLessonId == first);
			progress.SeatSeconds.Should().BeInRange(LmsSeatTime.FirstHeartbeatSeconds, LmsSeatTime.FirstHeartbeatSeconds + 4, "a claim of ten minutes is capped by the time that passed");
			(await _world.Learner.CompleteLessonAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, first, null)).Error.Should().Be(LmsErrors.SeatTimeNotMet);

			// Two minutes later (the stored heartbeat moves back) the next heartbeat may add what really passed.
			progress.LastSeenOn = DateTime.UtcNow.AddSeconds(-100);
			await _world.Learner.SaveProgressAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, first, new LmsProgressInput { Seconds = 100 });
			(await _world.Learner.CompleteLessonAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, first, null)).Succeeded.Should().BeTrue();

			await _world.Learner.SaveProgressAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, second, new LmsProgressInput { Seconds = 5, VideoPositionSeconds = 42 });
			var resumed = await _world.Learner.GetPlayerAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, null);
			resumed.Lesson.LmsLessonId.Should().Be(second);
			resumed.LessonProgress.VideoPositionSeconds.Should().Be(42);
			_world.Enrollments.Rows.Single().Status.Should().Be((int)LmsEnrollmentStatuses.InProgress);
		}

		[Test]
		public async Task Completion_writes_one_learning_record_and_issues_the_certification()
		{
			_world.CertificationTypes[8] = new DepartmentCertificationType
			{
				DepartmentCertificationTypeId = 8, DepartmentId = D, Type = "Pump Operator", AppliesTo = (int)CertificationAppliesTo.Person, DefaultValidityMonths = 24
			};
			var (course, version, lessons) = await _world.PublishedCourseAsync("Pump operator", LmsWorld.Reading("Hydraulics"));
			_world.Versions.Rows.Single(v => v.LmsCourseVersionId == version.LmsCourseVersionId).IssuesCertificationTypeId = 8;
			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);

			var done = await _world.Learner.CompleteLessonAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, null);
			done.Count.Should().Be(1, "the course completed");
			await _world.Learner.CompleteLessonAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, null);

			var record = _world.Records.Live.Should().ContainSingle().Subject;
			record.SourceKind.Should().Be((int)LearningRecordSourceKinds.LmsCourseCompletion);
			record.SourceRefId.Should().Be("lms-enrollment:" + enrollment.LmsEnrollmentId);
			record.UserId.Should().Be(LmsWorld.Member);
			record.Hours.Should().Be(2m);

			var stored = _world.Enrollments.Rows.Single();
			stored.Status.Should().Be((int)LmsEnrollmentStatuses.Completed);
			stored.IsOpen.Should().BeFalse();
			stored.LearningRecordId.Should().Be(record.LearningRecordId);

			var certification = _world.Certifications.Should().ContainSingle().Subject;
			certification.Status.Should().Be((int)PersonnelCertificationStatuses.Active);
			certification.ExpiresOn.Should().Be(DateTime.UtcNow.Date.AddMonths(24));
			stored.PersonnelCertificationId.Should().Be(certification.PersonnelCertificationId);
		}

		[Test]
		public async Task Completion_renews_an_expiring_certification_and_leaves_it_pending_when_the_type_requires_verification()
		{
			_world.CertificationTypes[9] = new DepartmentCertificationType
			{
				DepartmentCertificationTypeId = 9, DepartmentId = D, Type = "EMT refresher", AppliesTo = (int)CertificationAppliesTo.Person, DefaultValidityMonths = 12, RequiresVerification = true
			};
			_world.Certifications.Add(new PersonnelCertification
			{
				PersonnelCertificationId = 77, DepartmentId = D, UserId = LmsWorld.Member, DepartmentCertificationTypeId = 9, ExpiresOn = DateTime.UtcNow.AddDays(10),
				Status = (int)PersonnelCertificationStatuses.Active
			});
			var (course, version, lessons) = await _world.PublishedCourseAsync("EMT refresher", LmsWorld.Reading("Airway"));
			_world.Versions.Rows.Single(v => v.LmsCourseVersionId == version.LmsCourseVersionId).IssuesCertificationTypeId = 9;
			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);

			await _world.Learner.CompleteLessonAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, null);

			var renewed = _world.Certifications.Should().ContainSingle().Subject;
			renewed.PersonnelCertificationId.Should().Be(77);
			renewed.ExpiresOn.Should().Be(DateTime.UtcNow.Date.AddMonths(12));
			renewed.Status.Should().Be((int)PersonnelCertificationStatuses.PendingVerification);
		}

		[Test]
		public async Task A_survey_response_is_anonymous_and_its_report_waits_for_three_responses()
		{
			typeof(LmsSurveyResponse).GetProperties().Select(p => p.Name).Should().NotContain(new[] { "UserId", "LmsEnrollmentId" });

			var survey = new LmsLesson
			{
				Kind = (int)LmsLessonKinds.Survey, Title = "How was it?", IsRequired = false,
				SurveyJson = JsonConvert.SerializeObject(new[] { new LmsSurveyQuestion { Key = "q1", Kind = (int)LmsSurveyQuestionKinds.Rating, Text = "Rate" } })
			};
			var (course, _, lessons) = await _world.PublishedCourseAsync("Evaluated", LmsWorld.Reading("Content"), survey);
			var surveyId = lessons[1].LmsLessonId;
			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);
			await _world.Learner.CompleteLessonAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, null);

			(await _world.Learner.SubmitSurveyAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, surveyId, new Dictionary<string, string> { ["q1"] = "9" })).Error
				.Should().Be(LmsErrors.InvalidInput, "a rating is 1 to 5");
			(await _world.Learner.SubmitSurveyAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, surveyId, new Dictionary<string, string> { ["q1"] = "4" })).Succeeded.Should().BeTrue();

			var response = _world.Surveys.Rows.Should().ContainSingle().Subject;
			response.SubmittedOn.TimeOfDay.Should().Be(TimeSpan.Zero, "only the day is kept");
			response.AnswersJson.Should().NotContain(LmsWorld.Member);

			var report = await _world.Learner.GetSurveyReportAsync(_world.AdminActor, surveyId);
			report.Hidden.Should().BeTrue();
			report.Questions.Single().Average.Should().BeNull();
		}

		#endregion Player, seat time and completion

		#region Assessments and grading

		[Test]
		public async Task An_attempt_snapshots_its_questions_and_the_learner_never_receives_a_key()
		{
			var bank = await _world.BankAsync(LmsWorld.Single("Nozzle pressure?", "b", "a", "b", "c"), LmsWorld.Single("Friction loss?", "a", "a", "b", "c"));
			var (course, _, lessons) = await _world.PublishedCourseAsync("Hose", LmsWorld.Quiz(bank.LmsQuestionBankId, 100));
			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);
			var quiz = lessons[0].LmsLessonId;

			var view = await _world.Assessment.StartAttemptAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, quiz, false, "10.0.0.1", "test", null);
			view.Error.Should().Be(LmsErrors.None);
			var json = JsonConvert.SerializeObject(view.Questions);
			json.Should().NotContain("IsCorrect").And.NotContain("CorrectKeys");
			view.Questions.Should().HaveCount(2);

			var attempt = _world.Attempts.Rows.Single();
			attempt.RevisionHash.Should().Be(LmsAssessmentEngine.RevisionHash(LmsAttemptSnapshot.Parse(attempt.SnapshotJson)));
			var answers = _world.Answers(attempt.LmsAssessmentAttemptId, correct: true);

			// The author re-keys a question while the attempt is open: the attempt is scored against its own snapshot.
			var rekeyed = _world.Questions.Rows.First();
			rekeyed.Options = rekeyed.Options.Select(o => new LmsQuestionOption { Key = o.Key, Text = o.Text, IsCorrect = o.Key == "c" }).ToList();

			(await _world.Assessment.StartAttemptAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, quiz, false, null, null, null)).Attempt.LmsAssessmentAttemptId
				.Should().Be(attempt.LmsAssessmentAttemptId, "an open attempt is resumed, not redrawn");

			var result = await _world.Assessment.SubmitAttemptAsync(D, LmsWorld.Member, attempt.LmsAssessmentAttemptId, answers);
			result.Attempt.Passed.Should().BeTrue();
			result.Attempt.Percent.Should().Be(100);
			result.CourseCompleted.Should().BeTrue();
			_world.Records.Live.Should().ContainSingle();

			(await _world.Assessment.SubmitAttemptAsync(D, LmsWorld.Member, attempt.LmsAssessmentAttemptId, answers)).Error.Should().Be(LmsErrors.AlreadySubmitted);
			(await _world.Assessment.GetResultAsync(_world.Actor(LmsWorld.Member2), attempt.LmsAssessmentAttemptId)).Error
				.Should().Be(LmsErrors.NotAuthorized, "another member never sees an attempt");
		}

		[Test]
		public async Task A_failed_attempt_hides_the_key_under_the_after_passing_policy()
		{
			var bank = await _world.BankAsync(LmsWorld.Single("One?", "a", "a", "b"), LmsWorld.Single("Two?", "b", "a", "b"));
			var (course, _, lessons) = await _world.PublishedCourseAsync("Policy", LmsWorld.Quiz(bank.LmsQuestionBankId, 100, LmsShowAnswersPolicies.AfterPassing));
			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);

			var view = await _world.Assessment.StartAttemptAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, false, null, null, null);
			var result = await _world.Assessment.SubmitAttemptAsync(D, LmsWorld.Member, view.Attempt.LmsAssessmentAttemptId, _world.Answers(view.Attempt.LmsAssessmentAttemptId, false));

			result.Attempt.Passed.Should().BeFalse();
			result.ShowAnswers.Should().BeFalse();
			result.ShowCorrectness.Should().BeFalse();
			result.Questions.Should().OnlyContain(q => q.CorrectKeys == null && q.Explanation == null && q.Correct == null);
			_world.Records.Live.Should().BeEmpty();
		}

		[Test]
		public async Task Free_text_answers_wait_for_a_grader_who_is_not_the_learner()
		{
			var essay = new LmsQuestion { Kind = (int)LmsQuestionKinds.ShortAnswer, Text = "Describe a size-up.", Points = 2, Explanation = "Model answer" };
			var bank = await _world.BankAsync(LmsWorld.Single("Command?", "a", "a", "b"), essay);
			var (course, _, lessons) = await _world.PublishedCourseAsync("Command", LmsWorld.Quiz(bank.LmsQuestionBankId, 80));
			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);

			var view = await _world.Assessment.StartAttemptAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, false, null, null, null);
			var submitted = await _world.Assessment.SubmitAttemptAsync(D, LmsWorld.Member, view.Attempt.LmsAssessmentAttemptId, _world.Answers(view.Attempt.LmsAssessmentAttemptId, true));
			submitted.Attempt.Status.Should().Be((int)LmsAttemptStatuses.PendingGrading);
			submitted.Attempt.Passed.Should().BeNull();
			submitted.CourseCompleted.Should().BeFalse();

			(await _world.Assessment.GetGradingQueueAsync(_world.Actor(LmsWorld.Member2))).Should().BeEmpty("only graders see the queue");
			var item = (await _world.Assessment.GetGradingQueueAsync(_world.Actor(LmsWorld.Grader, grade: true))).Should().ContainSingle().Subject;
			item.Questions.Should().ContainSingle(q => q.AnswerText == "A considered answer." && q.ModelAnswer == "Model answer");

			var grades = new[] { new LmsManualGrade { QuestionId = item.Questions[0].QuestionId, Awarded = 2 } };
			(await _world.Assessment.GradeAsync(_world.Actor(LmsWorld.Member, grade: true), view.Attempt.LmsAssessmentAttemptId, grades, null)).Error
				.Should().Be(LmsErrors.NotAuthorized, "nobody grades their own attempt");
			(await _world.Assessment.GradeAsync(_world.Actor(LmsWorld.Grader, grade: true), view.Attempt.LmsAssessmentAttemptId, Array.Empty<LmsManualGrade>(), null)).Error
				.Should().Be(LmsErrors.InvalidInput, "every pending answer needs a grade");

			var graded = await _world.Assessment.GradeAsync(_world.Actor(LmsWorld.Grader, grade: true), view.Attempt.LmsAssessmentAttemptId, grades, "Good");
			graded.Attempt.Status.Should().Be((int)LmsAttemptStatuses.Graded);
			graded.Attempt.Passed.Should().BeTrue();
			graded.Attempt.GradedByUserId.Should().Be(LmsWorld.Grader);
			graded.CourseCompleted.Should().BeTrue();
			_world.Records.Live.Should().ContainSingle();
		}

		[Test]
		public async Task Item_analysis_is_for_graders_and_course_reporters_only()
		{
			var bank = await _world.BankAsync(LmsWorld.Single("Only?", "a", "a", "b"));
			var (course, _, lessons) = await _world.PublishedCourseAsync("Analysed", LmsWorld.Quiz(bank.LmsQuestionBankId, 100));
			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);
			var view = await _world.Assessment.StartAttemptAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, false, null, null, null);
			await _world.Assessment.SubmitAttemptAsync(D, LmsWorld.Member, view.Attempt.LmsAssessmentAttemptId, _world.Answers(view.Attempt.LmsAssessmentAttemptId, true));

			(await _world.Assessment.GetItemAnalysisAsync(_world.Actor(LmsWorld.Member2), lessons[0].LmsLessonId)).Should().BeNull();
			var report = await _world.Assessment.GetItemAnalysisAsync(_world.Actor(LmsWorld.Grader, grade: true), lessons[0].LmsLessonId);
			report.Should().NotBeNull();
			report.Items.Should().ContainSingle().Which.Difficulty.Should().Be(1m);
		}

		#endregion Assessments and grading

		#region Crew viewing

		[Test]
		public async Task Crew_viewing_marks_presence_for_each_member_who_scans_the_current_code()
		{
			var bank = await _world.BankAsync(LmsWorld.Single("Seen?", "a", "a", "b"));
			var (course, _, lessons) = await _world.PublishedCourseAsync("Extrication", LmsWorld.Reading("Video together", 300), LmsWorld.Quiz(bank.LmsQuestionBankId));

			var started = await _world.Crew.StartAsync(_world.AdminActor, course.LmsCourseId, lessons[0].LmsLessonId);
			started.Succeeded.Should().BeTrue();
			var viewing = (LmsCrewViewing)started.Value;
			var code = _world.Crew.CurrentCode(viewing, DateTime.UtcNow);

			(await _world.Crew.ScanAsync(D, LmsWorld.Member, viewing.LmsCrewViewingId, "000000000000", null)).Error.Should().Be(LmsErrors.CodeExpired);
			(await _world.Crew.ScanAsync(D, LmsWorld.Member, viewing.LmsCrewViewingId, code, "10.1.1.1")).Succeeded.Should().BeTrue();
			(await _world.Crew.ScanAsync(D, LmsWorld.Member2, viewing.LmsCrewViewingId, code, "10.1.1.2")).Succeeded.Should().BeTrue();
			(await _world.Crew.ScanAsync(D, LmsWorld.Member, viewing.LmsCrewViewingId, code, "10.1.1.1")).Succeeded.Should().BeTrue("a second scan is harmless");

			(await _world.Crew.GetAttendeesAsync(D, viewing.LmsCrewViewingId)).Select(a => a.UserId).Should().BeEquivalentTo(new[] { LmsWorld.Member, LmsWorld.Member2 });
			foreach (var user in new[] { LmsWorld.Member, LmsWorld.Member2 })
			{
				var enrollment = _world.Enrollments.Rows.Single(e => e.UserId == user);
				enrollment.Source.Should().Be((int)LmsEnrollmentSources.CrewViewing);
				enrollment.IsOpen.Should().BeTrue("the quiz each member takes on their own is still to do");
				var progress = _world.Progress.Rows.Single(p => p.LmsEnrollmentId == enrollment.LmsEnrollmentId);
				progress.Status.Should().Be((int)LmsLessonProgressStatuses.Completed);
				progress.LmsCrewViewingId.Should().Be(viewing.LmsCrewViewingId);
				progress.SeatSeconds.Should().BeGreaterThanOrEqualTo(300);
			}

			await _world.Crew.EndAsync(_world.AdminActor, viewing.LmsCrewViewingId);
			(await _world.Crew.ScanAsync(D, LmsWorld.Grader, viewing.LmsCrewViewingId, code, null)).Error.Should().Be(LmsErrors.CodeExpired, "an ended viewing takes no more scans");
		}

		#endregion Crew viewing

		#region Gate and lapse

		[Test]
		public async Task Department_courses_go_dark_with_the_flag_while_the_academy_keeps_running()
		{
			var (course, _, _) = await _world.PublishedCourseAsync("Department course", LmsWorld.Reading("One"));
			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);
			var academy = await _world.Academy.AssignLessonAsync(_world.Actor(LmsWorld.Admin, assign: true), "00-start", new[] { new ContentAudienceRule(ContentAudienceKinds.User, LmsWorld.Member) }, null);
			academy.Created.Should().Be(1);

			_world.Gate = new LmsDepartmentGate { DepartmentId = D, ModuleOn = true, FlagOn = false };
			var mine = await _world.Learner.GetMyCoursesAsync(D, LmsWorld.Member);
			mine.Should().ContainSingle().Which.Course.IsSystem.Should().BeTrue();
			(await _world.Learner.GetPlayerAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, null)).Error.Should().Be(LmsErrors.AddonRequired);
			(await _world.Learner.EnrollSelfAsync(_world.Actor(LmsWorld.Member2), course.LmsCourseId)).Error.Should().Be(LmsErrors.AddonRequired);
			(await _world.Learner.GetCatalogAsync(_world.Actor(LmsWorld.Member2))).Should().BeEmpty();
			var academyEnrollment = _world.Enrollments.Rows.Single(e => e.LmsCourseId != course.LmsCourseId);
			(await _world.Learner.GetPlayerAsync(D, LmsWorld.Member, academyEnrollment.LmsEnrollmentId, null)).Error.Should().Be(LmsErrors.None);

			_world.Gate = new LmsDepartmentGate { DepartmentId = D, ModuleOn = false };
			(await _world.Learner.GetPlayerAsync(D, LmsWorld.Member, academyEnrollment.LmsEnrollmentId, null)).Error.Should().Be(LmsErrors.NotAuthorized, "the Trainings module closes the Academy too");
		}

		[Test]
		public async Task After_a_lapse_existing_dated_assignments_run_and_nothing_new_can_be_assigned()
		{
			var (course, _, _) = await _world.PublishedCourseAsync("Lapsing", LmsWorld.Reading("One"));
			var outcome = await _world.Learner.AssignAsync(_world.AdminActor, course.LmsCourseId, new[] { new ContentAudienceRule(ContentAudienceKinds.User, LmsWorld.Member) },
				DateTime.UtcNow.AddDays(20));
			outcome.Created.Should().Be(1);
			var enrollment = _world.Enrollments.Rows.Single();
			enrollment.AssignedOn = DateTime.UtcNow.AddDays(-10);

			_world.Gate = new LmsDepartmentGate { DepartmentId = D, ModuleOn = true, FlagOn = true, AddonActive = false, LastEntitledOn = DateTime.UtcNow.AddDays(-5) };
			(await _world.Learner.GetPlayerAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, null)).Error.Should().Be(LmsErrors.None, "rule 4: takeable until due");
			(await _world.Learner.AssignAsync(_world.Actor(LmsWorld.Admin, manage: true), course.LmsCourseId,
				new[] { new ContentAudienceRule(ContentAudienceKinds.User, LmsWorld.Member2) }, null)).Error.Should().Be(LmsErrors.AddonRequired, "rule 3");
			(await _world.CourseService.CreateAsync(_world.AdminActor, new LmsCourseInput { Title = "New" })).Error.Should().Be(LmsErrors.AddonRequired);

			enrollment.DueOn = DateTime.UtcNow.AddDays(-1);
			(await _world.Learner.GetPlayerAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, null)).Error.Should().Be(LmsErrors.LapseWindowEnded);
		}

		#endregion Gate and lapse

		#region Hosted video (worker 87)

		private LmsMediaAsset QueueAsset()
		{
			var asset = new LmsMediaAsset
			{
				DepartmentId = D, Kind = 0, FileName = "drill.mp4", ContentType = "video/mp4", SourceKey = "lms/31/1/source.mp4", SourceBytes = 1000,
				Status = (int)LmsMediaStatuses.Queued, CreatedOn = DateTime.UtcNow
			};
			_world.Media.InsertAsync(asset, CancellationToken.None).GetAwaiter().GetResult();
			_world.Store.Setup(s => s.OpenReadAsync(asset.SourceKey, It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream(new byte[] { 1, 2, 3 }));
			return asset;
		}

		[Test]
		public async Task A_queued_upload_is_transcoded_to_hls_and_an_mp4_then_marked_ready()
		{
			var asset = QueueAsset();
			var uploaded = new List<string>();
			_world.Store.Setup(s => s.PutAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((string key, Stream stream, string type, CancellationToken ct) => uploaded.Add(key + "|" + type)).Returns(Task.CompletedTask);
			_world.Transcoder.Setup(t => t.TranscodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string source, string output, CancellationToken ct) =>
				{
					System.IO.File.Exists(source).Should().BeTrue("the source is downloaded before ffmpeg runs");
					Directory.CreateDirectory(output);
					System.IO.File.WriteAllText(Path.Combine(output, "index.m3u8"), "#EXTM3U\nseg_000.ts\n");
					System.IO.File.WriteAllBytes(Path.Combine(output, "seg_000.ts"), new byte[100]);
					System.IO.File.WriteAllBytes(Path.Combine(output, "video.mp4"), new byte[400]);
					return new LmsTranscodeOutput { Directory = output, Files = new List<string> { "index.m3u8", "seg_000.ts", "video.mp4" }, DurationSeconds = 95 };
				});

			(await _world.MediaService.ProcessTranscodeQueueAsync(2)).Should().Be(1);

			var stored = _world.Media.Rows.Single();
			stored.Status.Should().Be((int)LmsMediaStatuses.Ready);
			stored.Attempts.Should().Be(1);
			stored.HlsPrefix.Should().Be("lms/31/" + asset.LmsMediaAssetId + "/hls/");
			stored.Mp4Key.Should().Be("lms/31/" + asset.LmsMediaAssetId + "/video.mp4");
			stored.OutputBytes.Should().BeGreaterThan(500);
			stored.DurationSeconds.Should().Be(95);
			uploaded.Should().Contain(new[]
			{
				stored.HlsPrefix + "index.m3u8|application/vnd.apple.mpegurl", stored.HlsPrefix + "seg_000.ts|video/mp2t", stored.Mp4Key + "|video/mp4"
			});
			(await _world.MediaService.ProcessTranscodeQueueAsync(2)).Should().Be(0, "nothing is left in the queue");
		}

		[Test]
		public async Task A_failing_transcode_is_retried_then_marked_failed()
		{
			QueueAsset();
			_world.Transcoder.Setup(t => t.TranscodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("ffmpeg exited with 1"));

			await _world.MediaService.ProcessTranscodeQueueAsync(1);
			var stored = _world.Media.Rows.Single();
			stored.Status.Should().Be((int)LmsMediaStatuses.Queued);
			stored.LastError.Should().Contain("ffmpeg exited");

			await _world.MediaService.ProcessTranscodeQueueAsync(1);
			await _world.MediaService.ProcessTranscodeQueueAsync(1);
			stored = _world.Media.Rows.Single();
			stored.Attempts.Should().Be(LmsMediaStateMachine.MaxAttempts);
			stored.Status.Should().Be((int)LmsMediaStatuses.Failed);
			(await _world.MediaService.ProcessTranscodeQueueAsync(1)).Should().Be(0);
		}

		[Test]
		public async Task Hosted_media_is_archived_once_the_read_only_window_has_passed()
		{
			var asset = QueueAsset();
			_world.Media.Rows.Single().Status = (int)LmsMediaStatuses.Ready;
			await _world.States.StampEntitledAsync(D, DateTime.UtcNow.AddDays(-120));
			_world.States.MediaDepartments.Add(D);
			_world.Gate = new LmsDepartmentGate { DepartmentId = D, ModuleOn = true, FlagOn = true, AddonActive = false, LastEntitledOn = DateTime.UtcNow.AddDays(-120) };

			(await _world.MediaService.ArchiveLapsedMediaAsync(DateTime.UtcNow)).Should().Be(1);
			_world.Media.Rows.Single(m => m.LmsMediaAssetId == asset.LmsMediaAssetId).Status.Should().Be((int)LmsMediaStatuses.Archived);
		}

		#endregion Hosted video (worker 87)

		#region Academy (A1)

		[Test]
		public async Task Seeding_the_academy_is_idempotent()
		{
			var created = await _world.Academy.EnsureSeededAsync();
			created.Should().Be(AcademyCourseManifest.Courses.Count);

			var counts = (_world.Courses.Rows.Count, _world.Versions.Rows.Count, _world.Lessons.Rows.Count, _world.Banks.Rows.Count, _world.Questions.Rows.Count);
			counts.Item1.Should().Be(AcademyCourseManifest.Courses.Count);
			_world.Courses.Rows.Should().OnlyContain(c => c.DepartmentId == 0 && c.IsSystem && c.ManifestVersion == AcademyCourseManifest.ManifestVersion && c.CurrentVersionId.HasValue);
			_world.Questions.Rows.Should().HaveCount(AcademyCourseManifest.Courses.Sum(c => c.Questions.Count));

			// Another process (memo cleared) finds everything in place and writes nothing.
			LmsWorld.ResetAcademySeedMemo();
			(await _world.Academy.EnsureSeededAsync()).Should().Be(0);
			(_world.Courses.Rows.Count, _world.Versions.Rows.Count, _world.Lessons.Rows.Count, _world.Banks.Rows.Count, _world.Questions.Rows.Count).Should().Be(counts);
			(await _world.Academy.EnsureSeededAsync()).Should().Be(0);
		}

		[Test]
		public async Task Seeded_checks_carry_the_manifest_keys_and_every_locale()
		{
			await _world.Academy.EnsureSeededAsync();
			foreach (var definition in AcademyCourseManifest.Courses)
			{
				var course = _world.Courses.Rows.Single(c => c.SystemKey == definition.SystemKey);
				var version = _world.Versions.Rows.Single(v => v.LmsCourseVersionId == course.CurrentVersionId);
				version.Hours.Should().BeInRange(0.10m, 0.25m);
				var lessons = _world.Lessons.Rows.Where(l => l.LmsCourseVersionId == version.LmsCourseVersionId).OrderBy(l => l.SortOrder).ToList();
				lessons.Select(l => (LmsLessonKinds)l.Kind).Should().Equal(LmsLessonKinds.Video, LmsLessonKinds.Assessment);
				lessons[0].VideoSource.Should().Be((int)LmsVideoSources.Academy);
				lessons[0].VideoUrl.Should().StartWith("https://academy.resgrid.com/");
				LmsCaptionTrack.Parse(lessons[0].CaptionTracksJson).Should().ContainSingle(t => t.Url.EndsWith(".vtt"));

				var questions = _world.Questions.Rows.Where(q => q.LmsQuestionBankId == lessons[1].LmsQuestionBankId).OrderBy(q => q.SortOrder).ToList();
				questions.Should().HaveCount(definition.Questions.Count).And.HaveCountGreaterThanOrEqualTo(3);
				for (var i = 0; i < questions.Count; i++)
				{
					questions[i].Options.Single(o => o.IsCorrect).Key.Should().Be(definition.Questions[i].Correct, definition.LessonId);
					questions[i].Text.Should().NotStartWith(definition.LessonId, "the English text comes from the resources, not the key");
					var translations = LmsQuestionTranslation.Parse(questions[i].TranslationsJson);
					translations.Keys.Should().BeEquivalentTo(Resgrid.Localization.SupportedLocales.GetSupportedCultures().Where(l => l != "en"), definition.LessonId);
				}
			}
		}

		[Test]
		public void Every_academy_resource_exists_in_every_locale()
		{
			var resources = new ResourceManager("Resgrid.Localization.Areas.User.Academy.AcademyCourses", typeof(Resgrid.Localization.SupportedLocales).Assembly);
			// A2: lesson titles, questions and options live in the lesson manifests (AcademyManifestTests checks every locale there); the
			// resources keep the shared texts and the role paths.
			var keys = new List<string> { "Check.Title", "TrueFalse.t", "TrueFalse.f" };
			foreach (var path in AcademyCourseManifest.RolePaths)
				keys.AddRange(new[] { "Path." + path.Key, "Path." + path.Key + ".Audience" });

			foreach (var locale in Resgrid.Localization.SupportedLocales.GetSupportedCultures())
			{
				var set = resources.GetResourceSet(CultureInfo.GetCultureInfo(locale), true, false);
				set.Should().NotBeNull(locale);
				keys.Where(k => string.IsNullOrWhiteSpace(set.GetString(k))).Should().BeEmpty(locale + " is missing Academy text");
			}
		}

		[Test]
		public async Task The_academy_shows_only_lessons_the_department_is_entitled_to_and_never_needs_the_lms_add_on()
		{
			_world.Gate = new LmsDepartmentGate { DepartmentId = D, ModuleOn = true, FlagOn = false };
			_world.Entitlements = new AcademyEntitlements();
			var actor = _world.Actor(LmsWorld.Admin, assign: true);

			var catalog = await _world.Academy.GetCatalogAsync(actor);
			catalog.Error.Should().Be(LmsErrors.None);
			var shown = catalog.Lessons.Select(l => l.Definition.LessonId).ToList();
			shown.Should().Contain("00-start").And.Contain("05-calls");
			shown.Should().NotContain("07-mapping", "the Mapping module is off").And.NotContain("18-ptt", "no Push-To-Talk add-on");
			catalog.Paths.SelectMany(p => p.Lessons).Should().OnlyContain(l => shown.Contains(l.Definition.LessonId));

			(await _world.Academy.AssignLessonAsync(actor, "07-mapping", new[] { new ContentAudienceRule(ContentAudienceKinds.User, LmsWorld.Member) }, null)).Error
				.Should().Be(LmsErrors.NotEntitled);

			_world.Entitlements.Modules.Add("Mapping");
			(await _world.Academy.GetCatalogAsync(actor)).Lessons.Select(l => l.Definition.LessonId).Should().Contain("07-mapping");

			_world.Gate = new LmsDepartmentGate { DepartmentId = D, ModuleOn = false };
			(await _world.Academy.GetCatalogAsync(_world.Actor(LmsWorld.Admin, assign: true))).Error.Should().Be(LmsErrors.NotAuthorized, "the Trainings module is off");
		}

		[Test]
		public async Task A_role_path_assigns_each_visible_lesson_and_tracks_progress_per_member()
		{
			_world.Gate = new LmsDepartmentGate { DepartmentId = D, ModuleOn = true, FlagOn = false };
			var actor = _world.Actor(LmsWorld.Admin, assign: true);
			(await _world.Academy.AssignPathAsync(_world.Actor(LmsWorld.Member), "member", new[] { new ContentAudienceRule(ContentAudienceKinds.User, LmsWorld.Member) }, null)).Error
				.Should().Be(LmsErrors.NotAuthorized);

			var outcome = await _world.Academy.AssignPathAsync(actor, "member", new[] { new ContentAudienceRule(ContentAudienceKinds.User, LmsWorld.Member) }, DateTime.UtcNow.AddDays(30));
			outcome.Error.Should().Be(LmsErrors.None);
			var path = AcademyCourseManifest.FindPath("member");
			var visible = path.LessonIds.Count(id => _world.Entitlements.Allows(AcademyCourseManifest.Find(id).VisibilityKeys));
			outcome.Created.Should().Be(visible);
			_world.Enrollments.Rows.Should().OnlyContain(e => e.Source == (int)LmsEnrollmentSources.RolePath && e.RolePathKey == "member");

			(await _world.Academy.AssignPathAsync(actor, "member", new[] { new ContentAudienceRule(ContentAudienceKinds.User, LmsWorld.Member) }, null)).Created
				.Should().Be(0, "assigning again does not duplicate open lessons");

			var rows = await _world.Academy.GetPathProgressAsync(actor, "member");
			rows.Should().ContainSingle().Which.Total.Should().Be(visible);
			rows[0].Completed.Should().Be(0);
		}

		[Test]
		public async Task An_academy_lesson_completes_with_its_check_and_lands_under_systems_training()
		{
			_world.Gate = new LmsDepartmentGate { DepartmentId = D, ModuleOn = true, FlagOn = false };
			var course = await _world.Academy.GetCourseForLessonAsync("00-start");
			var enrollment = await EnrollAsync(LmsWorld.Member, course.LmsCourseId);
			var lessons = _world.Lessons.Rows.Where(l => l.LmsCourseVersionId == course.CurrentVersionId).OrderBy(l => l.SortOrder).ToList();

			var progress = _world.Progress;
			await _world.Learner.SaveProgressAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, new LmsProgressInput { Seconds = 10, WatchedPercent = 90 });
			(await _world.Learner.CompleteLessonAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, null)).Succeeded.Should().BeTrue();

			var view = await _world.Assessment.StartAttemptAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lessons[1].LmsLessonId, false, null, null, "de");
			view.Questions.Should().HaveCount(3);
			view.Questions.Should().Contain(q => q.Options.Any(o => o.Text == "Richtig"), "the true/false options are translated");
			var result = await _world.Assessment.SubmitAttemptAsync(D, LmsWorld.Member, view.Attempt.LmsAssessmentAttemptId, _world.Answers(view.Attempt.LmsAssessmentAttemptId, true));
			result.CourseCompleted.Should().BeTrue();
			result.ShowAnswers.Should().BeTrue("the Academy checks always explain");

			var record = _world.Records.Live.Should().ContainSingle().Subject;
			record.SourceKind.Should().Be((int)LearningRecordSourceKinds.AcademyLesson);
			record.CourseCode.Should().Be("RG-ACADEMY-00-start");
			record.Hours.Should().BeInRange(0.10m, 0.25m);
			var category = _world.Categories.Should().ContainSingle().Subject;
			category.ExternalCode.Should().Be(AcademyCourseManifest.CategoryCode);
			record.CategoryId.Should().Be(category.TrainingCategoryId);
			progress.Rows.Should().OnlyContain(p => p.DepartmentId == D);
		}

		#endregion Academy (A1)
	}
}
