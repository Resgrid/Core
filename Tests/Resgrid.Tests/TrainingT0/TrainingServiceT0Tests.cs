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
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.Mocks;

namespace Resgrid.Tests.TrainingT0
{
	/// <summary>
	/// TrainingService after T0: the server-scored quiz with attempt history (T-D1, T-D2, T-D20, R-T6, R-T7), views written
	/// once (T-D13), saves that never touch assignees (T-D14), archive / delete / copy (T-D21), metadata-only reads (T-D22),
	/// workflow events (T-D18), reminders (T-D19, item 4), flags and the officer report.
	/// </summary>
	[TestFixture]
	public class TrainingServiceT0Tests
	{
		// Not 1: SystemBehaviorConfig.BypassDoNotBroadcastDepartments lets department 1 transmit even when broadcasting is off.
		private const int DepartmentId = 5;
		private const string Member = "member";
		private const string Creator = "officer";

		private MockTrainingRepository _trainings;
		private MockTrainingAttachmentRepository _attachments;
		private MockTrainingQuestionRepository _questions;
		private MockTrainingUserRepository _users;
		private Mock<ICommunicationService> _communication;
		private Mock<IDepartmentsService> _departments;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<ITrainingQuizAttemptsRepository> _attempts;
		private Mock<ITrainingCompletionHistoriesRepository> _histories;
		private Mock<ITrainingQuestionFlagsRepository> _flags;
		private Mock<IMemberLeaveService> _leave;
		private Mock<IEventAggregator> _events;
		private Mock<IDepartmentGroupsService> _groups;
		private List<TrainingQuizAttempt> _storedAttempts;
		private TrainingService _service;
		private bool _doNotBroadcast;

		[SetUp]
		public void SetUp()
		{
			_doNotBroadcast = Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast;
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = false;

			_trainings = new MockTrainingRepository();
			_attachments = new MockTrainingAttachmentRepository();
			_questions = new MockTrainingQuestionRepository();
			_users = new MockTrainingUserRepository();
			_communication = new Mock<ICommunicationService>();
			_departments = new Mock<IDepartmentsService>();
			_settings = new Mock<IDepartmentSettingsService>();
			_attempts = new Mock<ITrainingQuizAttemptsRepository>();
			_histories = new Mock<ITrainingCompletionHistoriesRepository>();
			_flags = new Mock<ITrainingQuestionFlagsRepository>();
			_leave = new Mock<IMemberLeaveService>();
			_events = new Mock<IEventAggregator>();
			_groups = new Mock<IDepartmentGroupsService>();
			_storedAttempts = new List<TrainingQuizAttempt>();

			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" });
			_departments.Setup(x => x.GetActiveMemberUserIdsAsync(DepartmentId))
				.ReturnsAsync(new HashSet<string>(new[] { Member, Creator, "new", "due", "reminded", "overdue", "leave", "legacy", "done", "exempt" }, StringComparer.OrdinalIgnoreCase));
			_settings.Setup(x => x.GetTextToCallNumberForDepartmentAsync(DepartmentId)).ReturnsAsync("15555550100");
			_leave.Setup(x => x.GetByMemberAsync(DepartmentId)).ReturnsAsync(new Dictionary<string, List<MemberLeavePeriod>>(StringComparer.OrdinalIgnoreCase));
			_attempts.Setup(x => x.InsertAsync(It.IsAny<TrainingQuizAttempt>(), It.IsAny<CancellationToken>(), true))
				.Callback<TrainingQuizAttempt, CancellationToken, bool>((a, _, _) => { a.TrainingQuizAttemptId = _storedAttempts.Count + 1; _storedAttempts.Add(a); })
				.ReturnsAsync((TrainingQuizAttempt a, CancellationToken _, bool _) => a);
			_attempts.Setup(x => x.GetLastAttemptNumberAsync(DepartmentId, It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync(() => _storedAttempts.Count == 0 ? 0 : _storedAttempts.Max(a => a.AttemptNumber));

			_service = new TrainingService(_trainings, _attachments, _users, _questions, _communication.Object, _departments.Object, _settings.Object,
				null, _attempts.Object, null, _histories.Object, _flags.Object, _leave.Object, _events.Object, _groups.Object);
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = _doNotBroadcast;
		}

		private Training SeedQuiz(double minimum = 50, Action<Training> configure = null, params TrainingQuestion[] questions)
		{
			var training = new Training
			{
				TrainingId = 1, DepartmentId = DepartmentId, Name = "Ladders", Description = "d", TrainingText = "t", CreatedByUserId = Creator,
				CreatedOn = DateTime.UtcNow.AddDays(-1), MinimumScore = minimum, Users = new List<TrainingUser>()
			};
			configure?.Invoke(training);
			_trainings.SeedTraining(training);
			foreach (var question in questions)
			{
				question.TrainingId = training.TrainingId;
				_questions.SeedQuestion(question);
			}
			return training;
		}

		private TrainingUser Assign(Training training, string userId, Action<TrainingUser> configure = null)
		{
			var row = new TrainingUser { TrainingUserId = _users.GetAllAsync().Result.Count() + 1, TrainingId = training.TrainingId, UserId = userId, AssignedOn = DateTime.UtcNow.AddDays(-1) };
			configure?.Invoke(row);
			_users.SeedUser(row);
			training.Users.Add(row);
			return row;
		}

		private static Dictionary<int, List<int>> Pick(params (int Question, int Option)[] picks) =>
			picks.GroupBy(p => p.Question).ToDictionary(g => g.Key, g => g.Select(p => p.Option).ToList());

		#region Quiz

		[Test]
		public async Task A_failed_retake_with_nothing_right_still_writes_its_score_and_does_not_complete()
		{
			var training = SeedQuiz(50, null, TrainingQuizScorerTests.Single(1), TrainingQuizScorerTests.Single(2));
			var row = Assign(training, Member, r => { r.Score = 50; r.Passed = false; r.AttemptCount = 1; r.Viewed = true; });

			var submission = await _service.RecordQuizAttemptAsync(DepartmentId, 1, Member, Pick((1, 12), (2, 22)));

			submission.Status.Should().Be(TrainingQuizSubmissionStatuses.Recorded);
			row.Score.Should().Be(0, "T-D1: the previous score never survives a retake");
			row.Passed.Should().BeFalse();
			row.Complete.Should().BeFalse("T-D2: a failed quiz is not a completion");
			row.CompletedOn.Should().BeNull();
			row.AttemptCount.Should().Be(2);
		}

		[Test]
		public async Task A_pass_completes_once_and_a_completed_member_cannot_submit_again()
		{
			var training = SeedQuiz(50, null, TrainingQuizScorerTests.Single(1));
			var row = Assign(training, Member);

			(await _service.RecordQuizAttemptAsync(DepartmentId, 1, Member, Pick((1, 11)))).Result.Passed.Should().BeTrue();
			row.Complete.Should().BeTrue();
			row.Passed.Should().BeTrue();
			var completedOn = row.CompletedOn;
			completedOn.Should().NotBeNull();

			(await _service.RecordQuizAttemptAsync(DepartmentId, 1, Member, Pick((1, 12)))).Status.Should().Be(TrainingQuizSubmissionStatuses.AlreadyPassed);
			row.Score.Should().Be(100);
			row.CompletedOn.Should().Be(completedOn);
		}

		[Test]
		public async Task An_attempt_keeps_the_quiz_it_was_taken_against_and_is_never_recomputed_after_an_edit()
		{
			var training = SeedQuiz(50, null, TrainingQuizScorerTests.Single(1), TrainingQuizScorerTests.Single(2));
			var row = Assign(training, Member, r => r.Cycle = 2);

			await _service.RecordQuizAttemptAsync(DepartmentId, 1, Member, Pick((1, 11), (2, 21)));

			var attempt = _storedAttempts.Single();
			attempt.AttemptNumber.Should().Be(1);
			attempt.Cycle.Should().Be(2);
			attempt.Passed.Should().BeTrue();
			attempt.Score.Should().Be(100);
			attempt.MinimumScore.Should().Be(50);
			attempt.QuestionCount.Should().Be(2);
			var snapshot = JsonConvert.DeserializeObject<TrainingQuizSnapshot>(attempt.Snapshot);
			snapshot.Questions.Should().HaveCount(2);
			snapshot.Questions[0].Options.Single(o => o.Correct).OptionId.Should().Be(11);
			snapshot.Questions[0].ChosenOptionIds.Should().Equal(11);
			var takenHash = attempt.QuizRevisionHash;

			// The author edits the answer key and raises the pass mark after the member passed.
			var stored = await _service.GetTrainingByIdAsync(1);
			stored.Questions.First().Answers.First().Correct = false;
			stored.Questions.First().Answers.Last().Correct = true;
			stored.MinimumScore = 100;

			TrainingQuizScorer.RevisionHash(stored).Should().NotBe(takenHash);
			attempt.Passed.Should().BeTrue("T-D20: the stored result stands against the revision taken");
			row.Passed.Should().BeTrue();
			row.Score.Should().Be(100);
			row.Complete.Should().BeTrue();
			(await _service.GetOutstandingTrainingsForUserAsync(DepartmentId, Member)).Should().BeEmpty();
		}

		[Test]
		public async Task The_attempt_limit_refuses_further_attempts_and_counts_down()
		{
			var training = SeedQuiz(100, t => t.QuizAttemptLimit = 2, TrainingQuizScorerTests.Single(1));
			Assign(training, Member);

			(await _service.RecordQuizAttemptAsync(DepartmentId, 1, Member, Pick((1, 12)))).AttemptsLeft.Should().Be(1);
			(await _service.RecordQuizAttemptAsync(DepartmentId, 1, Member, Pick((1, 12)))).AttemptsLeft.Should().Be(0);
			var refused = await _service.RecordQuizAttemptAsync(DepartmentId, 1, Member, Pick((1, 11)));

			refused.Status.Should().Be(TrainingQuizSubmissionStatuses.AttemptLimitReached);
			_storedAttempts.Select(a => a.AttemptNumber).Should().Equal(1, 2);
		}

		[Test]
		public async Task Submissions_from_outside_the_assignment_are_refused()
		{
			var training = SeedQuiz(50, null, TrainingQuizScorerTests.Single(1));
			Assign(training, "removed", r => r.RemovedOn = DateTime.UtcNow);

			(await _service.RecordQuizAttemptAsync(DepartmentId, 1, "stranger", Pick((1, 11)))).Status.Should().Be(TrainingQuizSubmissionStatuses.NotAssigned);
			(await _service.RecordQuizAttemptAsync(DepartmentId, 1, "removed", Pick((1, 11)))).Status.Should().Be(TrainingQuizSubmissionStatuses.NotAssigned);
			(await _service.RecordQuizAttemptAsync(99, 1, "stranger", Pick((1, 11)))).Status.Should().Be(TrainingQuizSubmissionStatuses.NotFound);

			var noQuiz = new Training { TrainingId = 2, DepartmentId = DepartmentId, Name = "Read only", Users = new List<TrainingUser>() };
			_trainings.SeedTraining(noQuiz);
			Assign(noQuiz, Member);
			(await _service.RecordQuizAttemptAsync(DepartmentId, 2, Member, Pick((1, 11)))).Status.Should().Be(TrainingQuizSubmissionStatuses.NoQuiz);

			var scheduled = new Training { TrainingId = 3, DepartmentId = DepartmentId, Name = "Later", StartsOn = DateTime.UtcNow.AddDays(3), Users = new List<TrainingUser>() };
			_trainings.SeedTraining(scheduled);
			Assign(scheduled, Member);
			(await _service.RecordQuizAttemptAsync(DepartmentId, 3, Member, Pick((1, 11)))).Status.Should().Be(TrainingQuizSubmissionStatuses.NotStarted);
			_storedAttempts.Should().BeEmpty();
		}

		#endregion Quiz

		#region Views and saves

		[Test]
		public async Task Viewed_on_and_completed_on_are_written_once()
		{
			var training = SeedQuiz(0);
			var firstView = DateTime.UtcNow.AddDays(-3);
			var row = Assign(training, Member, r => { r.Viewed = true; r.ViewedOn = firstView; r.Complete = true; r.CompletedOn = firstView; });

			await _service.SetTrainingAsViewedAsync(1, Member);

			row.ViewedOn.Should().Be(firstView);
			row.CompletedOn.Should().Be(firstView);
		}

		[Test]
		public async Task Opening_a_training_with_a_quiz_marks_it_viewed_but_not_complete()
		{
			var training = SeedQuiz(50, null, TrainingQuizScorerTests.Single(1));
			var row = Assign(training, Member);

			await _service.SetTrainingAsViewedAsync(1, Member);

			row.Viewed.Should().BeTrue();
			row.ViewedOn.Should().NotBeNull();
			row.Complete.Should().BeFalse();
		}

		[Test]
		public async Task An_archived_or_unstarted_training_is_not_marked_viewed()
		{
			var training = SeedQuiz(0, t => t.StartsOn = DateTime.UtcNow.AddDays(2));
			var row = Assign(training, Member);

			await _service.SetTrainingAsViewedAsync(1, Member);
			row.Viewed.Should().BeFalse();

			training.StartsOn = null;
			training.ArchivedOn = DateTime.UtcNow;
			await _service.SetTrainingAsViewedAsync(1, Member);
			row.Viewed.Should().BeFalse();
		}

		[Test]
		public async Task Saving_a_training_never_writes_its_assignees_so_a_concurrent_completion_survives()
		{
			var users = new Mock<ITrainingUserRepository>(MockBehavior.Strict);
			var service = new TrainingService(_trainings, _attachments, users.Object, _questions, _communication.Object, _departments.Object, _settings.Object,
				null, _attempts.Object, null, _histories.Object, _flags.Object, _leave.Object, _events.Object, _groups.Object);
			var training = SeedQuiz(50, null, TrainingQuizScorerTests.Single(1));
			var stale = new TrainingUser { TrainingUserId = 1, TrainingId = 1, UserId = Member, Complete = false };
			training.Users.Add(stale);
			var loaded = await service.GetTrainingByIdAsync(1);

			loaded.Name = "Ladders (revised)";
			await service.SaveAsync(loaded);

			// Strict: any read or write of TrainingUsers during the save would have thrown.
			users.VerifyNoOtherCalls();
			_trainings.Trainings.Single().Name.Should().Be("Ladders (revised)");
		}

		[Test]
		public async Task Saving_bounds_the_settings_on_the_server()
		{
			var training = new Training
			{
				DepartmentId = DepartmentId, Name = "Bounds", Description = "d", TrainingText = "t", CreatedByUserId = Creator, MinimumScore = 250,
				DueDays = -4, RepeatMonths = 6, QuizAttemptLimit = 0, OverdueRepeatDays = -1, ReminderLeadDaysCsv = "1, 7, nonsense", AllowSelfEnroll = true
			};

			var saved = await _service.SaveAsync(training);

			saved.MinimumScore.Should().Be(100);
			saved.DueDays.Should().BeNull();
			saved.QuizAttemptLimit.Should().BeNull();
			saved.OverdueRepeatDays.Should().BeNull();
			saved.ReminderLeadDaysCsv.Should().Be("7,1");
			saved.AllowSelfEnroll.Should().BeFalse("self-enrolment needs an open training");
			saved.NextRepeatOn.Should().BeCloseTo(saved.CreatedOn.AddMonths(6), TimeSpan.FromSeconds(5));

			saved.RepeatMonths = null;
			(await _service.SaveAsync(saved)).NextRepeatOn.Should().BeNull();
		}

		[Test]
		public async Task Saving_publishes_the_workflow_events_without_assignees_or_the_answer_key()
		{
			var training = new Training
			{
				DepartmentId = DepartmentId, Name = "Event", Description = "d", TrainingText = "t", CreatedByUserId = Creator,
				Users = new List<TrainingUser> { new TrainingUser { UserId = Member } },
				Questions = new List<TrainingQuestion> { TrainingQuizScorerTests.Single(0) }
			};
			training.Questions.Single().TrainingQuestionId = 0;
			foreach (var answer in training.Questions.Single().Answers)
				answer.TrainingQuestionAnswerId = 0;

			var saved = await _service.SaveAsync(training);
			await _service.SaveAsync(saved);

			_events.Verify(x => x.SendMessage(It.Is<TrainingAddedEvent>(e => e.DepartmentId == DepartmentId && e.Training.Name == "Event" && e.Training.Users == null && e.Training.Questions == null)), Times.Once);
			_events.Verify(x => x.SendMessage(It.Is<TrainingUpdatedEvent>(e => e.Training.TrainingId == saved.TrainingId && e.Training.Users == null && e.Training.Questions == null)), Times.Once);
		}

		[Test]
		public async Task Saving_keeps_an_allowed_embed_and_drops_any_other_frame()
		{
			var saved = await _service.SaveAsync(new Training
			{
				DepartmentId = DepartmentId, Name = "Embeds", Description = "d", CreatedByUserId = Creator,
				TrainingText = "<p>Watch this</p><iframe class=\"ql-video\" src=\"https://www.youtube.com/embed/dQw4w9WgXcQ\" onload=\"alert(1)\"></iframe>" +
					"<iframe src=\"https://evil.example.com/x\"></iframe><script>alert(2)</script>"
			});

			saved.TrainingText.Should().Contain("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ");
			saved.TrainingText.Should().Contain("Watch this");
			saved.TrainingText.Should().NotContain("evil.example.com");
			saved.TrainingText.Should().NotContain("onload");
			saved.TrainingText.Should().NotContain("<script");
		}

		[Test]
		public async Task A_training_by_id_carries_attachment_metadata_but_never_the_bytes()
		{
			SeedQuiz(0);
			_attachments.SeedAttachment(new TrainingAttachment { TrainingAttachmentId = 4, TrainingId = 1, FileName = "manual.pdf", FileType = "application/pdf", Data = new byte[] { 1, 2, 3, 4, 5 } });

			var training = await _service.GetTrainingByIdAsync(1);

			training.Attachments.Should().ContainSingle();
			training.Attachments.Single().Data.Should().BeNull();
			training.Attachments.Single().FileSize.Should().Be(5);
			(await _service.GetTrainingAttachmentByIdAsync(4)).Data.Should().BeNull();
			(await _service.GetTrainingAttachmentWithDataAsync(4)).Data.Should().HaveCount(5);
		}

		[Test]
		public async Task The_department_list_reads_question_counts_in_one_query()
		{
			var questions = new Mock<ITrainingQuestionRepository>(MockBehavior.Strict);
			var service = new TrainingService(_trainings, _attachments, _users, questions.Object, null, _departments.Object, null);
			SeedQuiz(0);
			_trainings.SeedTraining(new Training { TrainingId = 2, DepartmentId = DepartmentId, Name = "Second" });
			_trainings.QuestionCounts[1] = 3;

			var list = await service.GetAllTrainingsForDepartmentAsync(DepartmentId);

			list.Single(t => t.TrainingId == 1).QuestionCount.Should().Be(3);
			list.Single(t => t.TrainingId == 1).HasQuiz.Should().BeTrue();
			list.Single(t => t.TrainingId == 2).QuestionCount.Should().Be(0);
			list.Single(t => t.TrainingId == 2).Users.Should().NotBeNull();
			questions.VerifyNoOtherCalls();
		}

		[Test]
		public async Task The_gdpr_export_rows_name_their_training_and_carry_nobody_elses_rows()
		{
			var training = SeedQuiz(0);
			Assign(training, Member);
			Assign(training, "someone-else");

			var rows = await _service.GetTrainingUsersForUserAsync(Member);

			rows.Should().ContainSingle();
			rows.Single().Training.Name.Should().Be("Ladders");
			rows.Single().Training.Users.Should().BeNull();
		}

		#endregion Views and saves

		#region Archive, delete, copy

		[Test]
		public async Task Archiving_writes_only_the_archive_columns_audits_and_restore_reverses_it()
		{
			var training = SeedQuiz(0);
			Assign(training, Member, r => r.Complete = true);

			(await _service.ArchiveTrainingAsync(DepartmentId, 1, Creator)).Should().BeTrue();
			training.ArchivedOn.Should().NotBeNull();
			training.ArchivedByUserId.Should().Be(Creator);
			_users.GetAllAsync().Result.Single().Complete.Should().BeTrue("the completion is kept for reports");
			_events.Verify(x => x.SendMessage(It.Is<AuditEvent>(e => e.Type == AuditLogTypes.TrainingArchived && e.DepartmentId == DepartmentId)), Times.Once);

			(await _service.ArchiveTrainingAsync(99, 1, Creator)).Should().BeFalse();

			(await _service.RestoreTrainingAsync(DepartmentId, 1, Creator)).Should().BeTrue();
			training.ArchivedOn.Should().BeNull();
			_events.Verify(x => x.SendMessage(It.Is<AuditEvent>(e => e.Type == AuditLogTypes.TrainingRestored)), Times.Once);
		}

		[Test]
		public async Task A_training_somebody_completed_is_never_deleted_outright()
		{
			SeedQuiz(0);
			_trainings.Completions[1] = 1;

			(await _service.DeleteTrainingAsync(DepartmentId, 1, Creator)).Should().Be(TrainingDeleteOutcomes.HasCompletions);
			_trainings.Trainings.Should().ContainSingle();

			_trainings.Completions[1] = 0;
			(await _service.DeleteTrainingAsync(99, 1, Creator)).Should().Be(TrainingDeleteOutcomes.NotFound);
			(await _service.DeleteTrainingAsync(DepartmentId, 1, Creator)).Should().Be(TrainingDeleteOutcomes.Deleted);
			_trainings.Trainings.Should().BeEmpty();
			_events.Verify(x => x.SendMessage(It.Is<AuditEvent>(e => e.Type == AuditLogTypes.TrainingDeleted)), Times.Once);
		}

		[Test]
		public async Task A_copy_takes_the_content_quiz_settings_and_attachments_but_not_the_assignees()
		{
			var source = SeedQuiz(70, t =>
			{
				t.ToBeCompletedBy = DateTime.UtcNow.AddDays(-5);
				t.StartsOn = DateTime.UtcNow.AddDays(-10);
				t.DueDays = null;
				t.ClosedToNewAssignees = true;
				t.Notified = DateTime.UtcNow.AddDays(-9);
				t.Hours = 2.5m;
				t.ReminderLeadDaysCsv = "7,1";
				t.OpenToAllMembers = true;
				t.AllowSelfEnroll = true;
			}, TrainingQuizScorerTests.Single(1), TrainingQuizScorerTests.Multi(2, 1, 2));
			Assign(source, Member, r => r.Complete = true);
			_attachments.SeedAttachment(new TrainingAttachment { TrainingId = 1, FileName = "a.pdf", Data = new byte[] { 9 } });

			var copy = await _service.CopyTrainingAsync(DepartmentId, 1, "copier", "Ladders 2027");

			copy.TrainingId.Should().NotBe(1);
			copy.Name.Should().Be("Ladders 2027");
			copy.CreatedByUserId.Should().Be("copier");
			copy.MinimumScore.Should().Be(70);
			copy.Hours.Should().Be(2.5m);
			copy.ReminderLeadDaysCsv.Should().Be("7,1");
			copy.AllowSelfEnroll.Should().BeTrue();
			copy.ToBeCompletedBy.Should().BeNull("a due date already passed is not copied");
			copy.StartsOn.Should().BeNull();
			copy.ClosedToNewAssignees.Should().BeFalse();
			copy.Notified.Should().BeNull();
			copy.Users.Should().BeNullOrEmpty();

			var copiedQuestions = (await _questions.GetTrainingQuestionsByTrainingIdAsync(copy.TrainingId)).ToList();
			copiedQuestions.Should().HaveCount(2);
			copiedQuestions.Select(q => q.TrainingQuestionId).Should().NotContain(new[] { 1, 2 });
			copiedQuestions.Single(q => q.Type == (int)TrainingQuestionTypes.MultiChoice).Answers.Count(a => a.Correct).Should().Be(2);
			(await _attachments.GetTrainingAttachmentsByTrainingIdAsync(copy.TrainingId)).Should().ContainSingle(a => a.FileName == "a.pdf");
			_users.GetAllAsync().Result.Should().OnlyContain(u => u.TrainingId == 1);
			_events.Verify(x => x.SendMessage(It.Is<AuditEvent>(e => e.Type == AuditLogTypes.TrainingCopied)), Times.Once);

			(await _service.CopyTrainingAsync(99, 1, "copier")).Should().BeNull();
		}

		#endregion Archive, delete, copy

		#region Notices

		[Test]
		public async Task One_run_sends_what_each_assignee_is_owed_and_stamps_it_on_their_row()
		{
			var now = DateTime.UtcNow;
			var training = SeedQuiz(0, t => { t.SendOverdueNotices = true; t.ToBeCompletedBy = now.AddDays(30); t.Notified = now.AddDays(-20); });
			var fresh = Assign(training, "new", r => r.AssignedOn = now.AddHours(-1));
			var due = Assign(training, "due", r => { r.NotifiedOn = now.AddDays(-10); r.DueOn = now.AddHours(20); });
			var reminded = Assign(training, "reminded", r => { r.NotifiedOn = now.AddDays(-10); r.DueOn = now.AddHours(10); r.LastReminderLeadDays = 1; });
			var overdue = Assign(training, "overdue", r => { r.NotifiedOn = now.AddDays(-10); r.DueOn = now.AddDays(-2); });
			var onLeave = Assign(training, "leave", r => { r.NotifiedOn = now.AddDays(-10); r.DueOn = now.AddHours(5); r.AssignedOn = now.AddDays(-10); });
			var legacy = Assign(training, "legacy", r => r.NotifiedOn = now.AddDays(-10));
			var inactive = Assign(training, "gone", r => r.AssignedOn = now.AddHours(-1));
			var done = Assign(training, "done", r => { r.Complete = true; r.DueOn = now.AddHours(3); r.NotifiedOn = now.AddDays(-10); });
			_leave.Setup(x => x.GetByMemberAsync(DepartmentId)).ReturnsAsync(new Dictionary<string, List<MemberLeavePeriod>>(StringComparer.OrdinalIgnoreCase)
			{
				["LEAVE"] = new List<MemberLeavePeriod> { new MemberLeavePeriod { UserId = "leave", StartsOn = now.AddDays(-1), EndsOn = now.AddDays(3) } }
			});

			var sent = await _service.SendTrainingNoticesAsync(training, now);

			sent.Should().Be(4, "new, due, overdue and the creator's overdue digest");
			fresh.NotifiedOn.Should().Be(now);
			due.RemindedOn.Should().Be(now);
			due.LastReminderLeadDays.Should().Be(1);
			reminded.RemindedOn.Should().BeNull();
			overdue.OverdueNotifiedOn.Should().Be(now);
			onLeave.RemindedOn.Should().BeNull();
			onLeave.OverdueNotifiedOn.Should().BeNull();
			legacy.DueOn.Should().Be(TrainingDueDates.FixedDueUtc(training, new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" }), "rows from before T0 get their due moment written once");
			inactive.NotifiedOn.Should().Be(now, "an inactive member is marked, never told");
			done.RemindedOn.Should().BeNull();

			_communication.Verify(x => x.SendNotificationAsync("new", DepartmentId, It.IsAny<string>(), "15555550100", It.IsAny<Department>(), TrainingNoticePlanner.NewTrainingTitle, It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Once);
			_communication.Verify(x => x.SendNotificationAsync("due", DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), TrainingNoticePlanner.DueReminderTitle, It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Once);
			_communication.Verify(x => x.SendNotificationAsync("overdue", DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), TrainingNoticePlanner.OverdueTitle, It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Once);
			_communication.Verify(x => x.SendNotificationAsync(Creator, DepartmentId, It.Is<string>(m => m.Contains("1 member(s) are overdue")), It.IsAny<string>(), It.IsAny<Department>(), TrainingNoticePlanner.OverdueTitle, It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Once);
			_communication.Verify(x => x.SendNotificationAsync("gone", It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Never);
			_communication.Verify(x => x.SendNotificationAsync("leave", It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Never);

			// The next run finds nothing new to say.
			_communication.Invocations.Clear();
			(await _service.SendTrainingNoticesAsync(training, now.AddMinutes(30))).Should().Be(0);
		}

		[Test]
		public async Task Rows_are_stamped_but_nothing_is_sent_while_the_department_may_not_transmit()
		{
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = true;
			var training = SeedQuiz(0);
			var fresh = Assign(training, "new", r => r.AssignedOn = DateTime.UtcNow.AddHours(-1));

			(await _service.SendTrainingNoticesAsync(training, DateTime.UtcNow)).Should().Be(0);

			fresh.NotifiedOn.Should().NotBeNull("re-enabling transmission must not release a backlog");
			_communication.VerifyNoOtherCalls();
		}

		[Test]
		public async Task An_edit_that_just_added_members_notifies_only_them()
		{
			var now = DateTime.UtcNow;
			var training = SeedQuiz(0);
			var added = Assign(training, "new", r => r.AssignedOn = now);
			var other = Assign(training, "due", r => r.AssignedOn = now);

			(await _service.SendTrainingNoticesAsync(training, now, new[] { "NEW" })).Should().Be(1);

			added.NotifiedOn.Should().Be(now);
			other.NotifiedOn.Should().BeNull();
		}

		#endregion Notices

		#region Flags and report

		[Test]
		public async Task A_flag_is_accepted_only_for_the_trainings_own_question_and_the_author_is_told()
		{
			SeedQuiz(0, null, TrainingQuizScorerTests.Single(1));
			_flags.Setup(x => x.InsertAsync(It.IsAny<TrainingQuestionFlag>(), It.IsAny<CancellationToken>(), true))
				.ReturnsAsync((TrainingQuestionFlag f, CancellationToken _, bool _) => f);

			(await _service.FlagQuestionAsync(DepartmentId, 1, 77, Member, "wrong")).Should().BeNull();
			(await _service.FlagQuestionAsync(99, 1, 1, Member, "wrong")).Should().BeNull();
			var flag = await _service.FlagQuestionAsync(DepartmentId, 1, 1, Member, new string('x', 1200));

			flag.Comment.Should().HaveLength(1000);
			flag.UserId.Should().Be(Member);
			_communication.Verify(x => x.SendNotificationAsync(Creator, DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), TrainingService.QuestionFlaggedTitle, It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task A_flag_is_resolved_only_through_its_own_training()
		{
			_flags.Setup(x => x.GetForTrainingAsync(DepartmentId, 1, false)).ReturnsAsync(new List<TrainingQuestionFlag> { new TrainingQuestionFlag { TrainingQuestionFlagId = 5, TrainingId = 1 } });
			_flags.Setup(x => x.ResolveAsync(DepartmentId, 5, Creator, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			(await _service.ResolveQuestionFlagAsync(DepartmentId, 1, 6, Creator)).Should().BeFalse();
			(await _service.ResolveQuestionFlagAsync(DepartmentId, 1, 5, Creator)).Should().BeTrue();
		}

		[Test]
		public async Task The_report_states_each_assignee_and_counts_completion_over_tracked_members_only()
		{
			var now = DateTime.UtcNow;
			var training = SeedQuiz(0);
			Assign(training, Member, r => { r.Complete = true; r.CompletedOn = now.AddDays(-1); });
			Assign(training, "overdue", r => r.DueOn = now.AddDays(-1));
			Assign(training, "exempt", r => r.ExemptOn = now.AddDays(-1));
			Assign(training, "done", r => { r.Complete = true; r.RemovedOn = now.AddDays(-1); });
			Assign(training, "departed");
			_departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName>
			{
				new PersonName { UserId = Member, FirstName = "Mia", LastName = "Member" }
			});
			_groups.Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup> { new DepartmentGroup { DepartmentGroupId = 3, Name = "Station 3" } });
			_groups.Setup(x => x.GetGroupIdsForAllUsersInDepartmentAsync(DepartmentId)).ReturnsAsync(new Dictionary<string, int> { [Member] = 3 });
			_attempts.Setup(x => x.GetForTrainingAsync(DepartmentId, 1)).ReturnsAsync(new List<TrainingQuizAttempt>
			{
				new TrainingQuizAttempt { UserId = "OVERDUE", AttemptNumber = 1, Passed = false }, new TrainingQuizAttempt { UserId = "overdue", AttemptNumber = 2, Passed = false }
			});

			var report = await _service.GetTrainingReportAsync(DepartmentId, 1, now);

			report.Rows.Single(r => r.UserId == Member).Name.Should().Be("Mia Member");
			report.Rows.Single(r => r.UserId == Member).GroupName.Should().Be("Station 3");
			report.Rows.Single(r => r.UserId == "overdue").State.Should().Be(TrainingAssignmentStates.Overdue);
			report.Rows.Single(r => r.UserId == "overdue").AttemptCount.Should().Be(2);
			report.Rows.Single(r => r.UserId == "overdue").LastAttempt.AttemptNumber.Should().Be(2);
			report.Rows.Single(r => r.UserId == "exempt").State.Should().Be(TrainingAssignmentStates.Exempt);
			report.Rows.Single(r => r.UserId == "done").State.Should().Be(TrainingAssignmentStates.Removed);
			report.Rows.Single(r => r.UserId == "departed").IsActiveMember.Should().BeFalse();
			report.CompleteCount.Should().Be(1);
			report.OverdueCount.Should().Be(1);
			report.ExemptCount.Should().Be(1);
			report.CompletionPercent.Should().Be(50, "one of the two tracked members: exempt, removed and departed members are not tracked");
			(await _service.GetTrainingReportAsync(99, 1, now)).Should().BeNull();
		}

		[Test]
		public async Task The_manager_tiles_count_overdue_due_this_week_and_completion()
		{
			var now = DateTime.UtcNow;
			var training = SeedQuiz(0);
			Assign(training, Member, r => r.Complete = true);
			Assign(training, "overdue", r => r.DueOn = now.AddDays(-1));
			Assign(training, "due", r => r.DueOn = now.AddDays(3));
			Assign(training, "new", r => r.DueOn = now.AddDays(30));
			var archived = new Training { TrainingId = 2, DepartmentId = DepartmentId, ArchivedOn = now, Users = new List<TrainingUser> { new TrainingUser { UserId = "overdue", DueOn = now.AddDays(-9) } } };

			var tiles = await _service.GetManagerSummaryAsync(DepartmentId, new[] { training, archived }, now);

			tiles.Overdue.Should().Be(1);
			tiles.DueThisWeek.Should().Be(1);
			tiles.CompletionPercent.Should().Be(25);
		}

		#endregion Flags and report
	}
}
