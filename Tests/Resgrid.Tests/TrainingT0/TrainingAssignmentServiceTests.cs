using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.Mocks;

namespace Resgrid.Tests.TrainingT0
{
	/// <summary>
	/// Dynamic assignment (T0 item 2): audience re-evaluation on membership change, relative and late-joiner due dates,
	/// withdrawal of never-opened rows, the one simple repeat with completion history, self-enrolment, removal, extensions,
	/// exemptions and resets - all per row.
	/// </summary>
	[TestFixture]
	public class TrainingAssignmentServiceTests
	{
		private const int DepartmentId = 10;
		private static readonly DateTime Now = new DateTime(2026, 11, 24, 15, 0, 0, DateTimeKind.Utc);
		private static readonly Department Department = new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" };

		private MockTrainingRepository _trainings;
		private MockTrainingUserRepository _users;
		private Mock<ITrainingAudiencesRepository> _audiences;
		private Mock<ITrainingCompletionHistoriesRepository> _histories;
		private Mock<IContentAudienceService> _audienceService;
		private Mock<IDepartmentsService> _departments;
		private Mock<IEventAggregator> _events;
		private List<TrainingCompletionHistory> _historyRows;
		private TrainingAssignmentService _service;

		[SetUp]
		public void SetUp()
		{
			_trainings = new MockTrainingRepository();
			_users = new MockTrainingUserRepository();
			_audiences = new Mock<ITrainingAudiencesRepository>();
			_histories = new Mock<ITrainingCompletionHistoriesRepository>();
			_audienceService = new Mock<IContentAudienceService>();
			_departments = new Mock<IDepartmentsService>();
			_events = new Mock<IEventAggregator>();
			_historyRows = new List<TrainingCompletionHistory>();

			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(Department);
			_departments.Setup(x => x.GetActiveMemberUserIdsAsync(DepartmentId))
				.ReturnsAsync(new HashSet<string>(new[] { "a", "b", "c", "done", "viewed", "manual" }, StringComparer.OrdinalIgnoreCase));
			_histories.Setup(x => x.InsertAsync(It.IsAny<TrainingCompletionHistory>(), It.IsAny<CancellationToken>(), true))
				.Callback<TrainingCompletionHistory, CancellationToken, bool>((h, _, _) => _historyRows.Add(h))
				.ReturnsAsync((TrainingCompletionHistory h, CancellationToken _, bool _) => h);
			_audienceService.Setup(x => x.ExplainSkippedAsync(DepartmentId, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<ContentAudienceSkip>());

			_service = new TrainingAssignmentService(_trainings, _users, _audiences.Object, _histories.Object, _audienceService.Object, _departments.Object, _events.Object);
		}

		private Training Seed(int id = 1, int? dueDays = null, DateTime? fixedDue = null, Action<Training> configure = null)
		{
			var training = new Training { TrainingId = id, DepartmentId = DepartmentId, Name = "Training " + id, CreatedByUserId = "officer", CreatedOn = Now.AddDays(-60), DueDays = dueDays, ToBeCompletedBy = fixedDue, Users = new List<TrainingUser>() };
			configure?.Invoke(training);
			_trainings.SeedTraining(training);
			return training;
		}

		private TrainingUser Row(Training training, string userId, TrainingAssignmentSources source = TrainingAssignmentSources.Audience, Action<TrainingUser> configure = null)
		{
			var row = new TrainingUser
			{
				TrainingUserId = _users.GetAllAsync().Result.Count() + 1,
				TrainingId = training.TrainingId,
				UserId = userId,
				AssignmentSource = (int)source,
				AssignedOn = Now.AddDays(-20),
				DueOn = Now.AddDays(5)
			};
			configure?.Invoke(row);
			_users.SeedUser(row);
			training.Users.Add(row);
			return row;
		}

		private void Audience(Training training, params ContentAudienceRule[] rules)
		{
			var rows = rules.Select(r => new TrainingAudience { DepartmentId = DepartmentId, TrainingId = training.TrainingId, Kind = r.Kind, RefId = r.RefId }).ToList();
			_audiences.Setup(x => x.GetForOpenTrainingsAsync(DepartmentId)).ReturnsAsync(rows);
			_audiences.Setup(x => x.GetForTrainingAsync(DepartmentId, training.TrainingId)).ReturnsAsync(rows);
		}

		private void Facts(string userId, bool active = true, params int[] groupIds)
		{
			_audienceService.Setup(x => x.GetMemberFactsAsync(DepartmentId, userId))
				.ReturnsAsync(new ContentAudienceMemberFacts { UserId = userId, IsActive = active, GroupIds = new HashSet<int>(groupIds) });
		}

		private TrainingUser Stored(string userId) => _users.GetAllAsync().Result.SingleOrDefault(u => string.Equals(u.UserId, userId, StringComparison.OrdinalIgnoreCase));

		[Test]
		public async Task A_member_who_joins_the_audience_is_assigned_with_a_relative_due_date()
		{
			var training = Seed(dueDays: 14);
			Audience(training, new ContentAudienceRule(ContentAudienceKinds.Group, "3"));
			Facts("a", true, 3);

			var (added, withdrawn) = await _service.ReevaluateMemberAsync(DepartmentId, "a", Now);

			added.Should().Be(1);
			withdrawn.Should().Be(0);
			var row = Stored("a");
			row.AssignmentSource.Should().Be((int)TrainingAssignmentSources.Audience);
			row.AssignedOn.Should().Be(Now);
			row.DueOn.Should().Be(TrainingDueDates.EndOfLocalDayUtc(new DateTime(2026, 11, 24).AddDays(14), Department));
			row.NotifiedOn.Should().BeNull("the notifier announces it");
		}

		[Test]
		public async Task A_member_who_joins_after_the_fixed_date_gets_thirty_days_not_an_overdue_assignment()
		{
			var training = Seed(fixedDue: new DateTime(2026, 11, 1));
			Audience(training, new ContentAudienceRule(ContentAudienceKinds.Group, "3"));
			Facts("a", true, 3);

			await _service.ReevaluateMemberAsync(DepartmentId, "a", Now);

			Stored("a").DueOn.Should().Be(TrainingDueDates.EndOfLocalDayUtc(new DateTime(2026, 11, 24).AddDays(TrainingDueDates.LateJoinerDueDays), Department));
		}

		[Test]
		public async Task An_inactive_member_is_not_assigned_and_their_unopened_audience_row_is_withdrawn()
		{
			var training = Seed();
			Audience(training, new ContentAudienceRule(ContentAudienceKinds.Everyone));
			Row(training, "hidden");
			Facts("hidden", active: false);
			Facts("new-hidden", active: false);

			(await _service.ReevaluateMemberAsync(DepartmentId, "new-hidden", Now)).Item1.Should().Be(0);
			(await _service.ReevaluateMemberAsync(DepartmentId, "hidden", Now)).Item2.Should().Be(1);

			Stored("new-hidden").Should().BeNull();
			Stored("hidden").Should().BeNull();
		}

		[Test]
		public async Task Leaving_the_audience_withdraws_only_never_opened_audience_rows()
		{
			var training = Seed();
			Audience(training, new ContentAudienceRule(ContentAudienceKinds.Group, "3"));
			Row(training, "a");
			Row(training, "viewed", configure: r => r.Viewed = true);
			Row(training, "done", configure: r => r.Complete = true);
			Row(training, "manual", TrainingAssignmentSources.Manual);
			Row(training, "tried", configure: r => r.AttemptCount = 1);
			foreach (var user in new[] { "a", "viewed", "done", "manual", "tried" })
				Facts(user, true, 9);

			var withdrawn = 0;
			foreach (var user in new[] { "a", "viewed", "done", "manual", "tried" })
				withdrawn += (await _service.ReevaluateMemberAsync(DepartmentId, user, Now)).Item2;

			withdrawn.Should().Be(1);
			Stored("a").Should().BeNull();
			Stored("viewed").Should().NotBeNull();
			Stored("done").Should().NotBeNull();
			Stored("manual").Should().NotBeNull();
			Stored("tried").Should().NotBeNull();
		}

		[Test]
		public async Task A_member_an_officer_removed_is_not_added_back_by_the_audience()
		{
			var training = Seed();
			Audience(training, new ContentAudienceRule(ContentAudienceKinds.Group, "3"));
			Row(training, "a", configure: r => { r.Complete = true; r.RemovedOn = Now.AddDays(-1); });
			Facts("a", true, 3);

			(await _service.ReevaluateMemberAsync(DepartmentId, "a", Now)).Item1.Should().Be(0);
			Stored("a").RemovedOn.Should().NotBeNull();
		}

		[Test]
		public async Task A_closed_or_archived_training_takes_nobody_new()
		{
			var closed = Seed(1, configure: t => t.ClosedToNewAssignees = true);
			Audience(closed, new ContentAudienceRule(ContentAudienceKinds.Everyone));
			Facts("a", true);

			(await _service.ReevaluateMemberAsync(DepartmentId, "a", Now)).Item1.Should().Be(0);

			var outcome = await _service.AssignAsync(closed, new[] { "a" }, null, TrainingAssignmentSources.Manual, null, Now);
			outcome.Closed.Should().BeTrue();
			outcome.Added.Should().BeEmpty();

			var archived = Seed(2, configure: t => t.ArchivedOn = Now.AddDays(-1));
			(await _service.AssignAsync(archived, new[] { "a" }, null, TrainingAssignmentSources.Manual, null, Now)).Closed.Should().BeTrue();
			Stored("a").Should().BeNull();
		}

		[Test]
		public async Task A_department_wide_change_adds_the_resolved_members_and_withdraws_the_rest()
		{
			var training = Seed();
			Audience(training, new ContentAudienceRule(ContentAudienceKinds.Shift, "4"));
			Row(training, "left");
			Row(training, "b");
			_audienceService.Setup(x => x.ResolveAsync(DepartmentId, It.IsAny<IEnumerable<IContentAudienceRule>>()))
				.ReturnsAsync(new ContentAudienceResolution { MemberUserIds = new HashSet<string>(new[] { "B", "c" }, StringComparer.OrdinalIgnoreCase) });

			var (added, withdrawn) = await _service.ReevaluateDepartmentAsync(DepartmentId, Now);

			added.Should().Be(1);
			withdrawn.Should().Be(1);
			Stored("c").Should().NotBeNull();
			Stored("left").Should().BeNull();
			Stored("b").Should().NotBeNull();
		}

		[Test]
		public async Task Picked_members_are_assigned_and_the_officer_hears_who_was_skipped_out_of_scope_or_already_there()
		{
			var training = Seed();
			Row(training, "b", TrainingAssignmentSources.Manual);
			Row(training, "c", TrainingAssignmentSources.Manual, r => { r.Complete = true; r.CompletedOn = Now.AddDays(-3); r.RemovedOn = Now.AddDays(-1); });
			_audienceService.Setup(x => x.ExplainSkippedAsync(DepartmentId, It.IsAny<IEnumerable<string>>()))
				.ReturnsAsync(new List<ContentAudienceSkip> { new ContentAudienceSkip { UserId = "hidden", Reason = ContentAudienceSkipReasons.Hidden } });
			var scope = new TrainingRightScope { ManagesOwn = true, AdministeredMemberUserIds = new HashSet<string>(new[] { "a", "b", "c" }, StringComparer.OrdinalIgnoreCase) };

			var outcome = await _service.AssignAsync(training, new[] { "a", "B", "c", "hidden", "other-station", "a" }, null, TrainingAssignmentSources.Manual, scope, Now);

			outcome.Added.Should().BeEquivalentTo(new[] { "a", "c" });
			outcome.AlreadyAssigned.Should().BeEquivalentTo(new[] { "b" });
			outcome.OutOfScope.Should().BeEquivalentTo(new[] { "other-station" });
			outcome.Skipped.Should().ContainSingle(s => s.UserId == "hidden" && s.Reason == ContentAudienceSkipReasons.Hidden);
			Stored("a").AssignmentSource.Should().Be((int)TrainingAssignmentSources.Manual);
			// Tracked again, the completion kept.
			var returned = Stored("c");
			returned.RemovedOn.Should().BeNull();
			returned.Complete.Should().BeTrue();
			returned.CompletedOn.Should().Be(Now.AddDays(-3));
		}

		[Test]
		public async Task Saving_an_audience_stores_normalized_rules_and_assigns_the_resolved_members()
		{
			var training = Seed();
			IReadOnlyCollection<TrainingAudience> stored = null;
			_audiences.Setup(x => x.ReplaceForTrainingAsync(DepartmentId, 1, It.IsAny<IReadOnlyCollection<TrainingAudience>>(), It.IsAny<CancellationToken>()))
				.Callback<int, int, IReadOnlyCollection<TrainingAudience>, CancellationToken>((_, _, rows, _) => stored = rows).Returns(Task.CompletedTask);
			_audienceService.Setup(x => x.ResolveAsync(DepartmentId, It.IsAny<IEnumerable<IContentAudienceRule>>()))
				.ReturnsAsync(new ContentAudienceResolution { MemberUserIds = new HashSet<string>(new[] { "a", "b" }, StringComparer.OrdinalIgnoreCase) });

			var outcome = await _service.SaveAudienceAsync(training, new[] { new ContentAudienceRule(ContentAudienceKinds.Group, "3"), new ContentAudienceRule(ContentAudienceKinds.Group, "3") }, "officer", null, Now);

			stored.Should().ContainSingle(r => r.Kind == (int)ContentAudienceKinds.Group && r.RefId == "3" && r.CreatedByUserId == "officer");
			outcome.Added.Should().BeEquivalentTo(new[] { "a", "b" });
			Stored("a").AssignmentSource.Should().Be((int)TrainingAssignmentSources.Audience);
		}

		[Test]
		public async Task A_group_admin_can_store_only_named_members_of_their_own_group()
		{
			var training = Seed();
			IReadOnlyCollection<TrainingAudience> stored = null;
			_audiences.Setup(x => x.ReplaceForTrainingAsync(DepartmentId, 1, It.IsAny<IReadOnlyCollection<TrainingAudience>>(), It.IsAny<CancellationToken>()))
				.Callback<int, int, IReadOnlyCollection<TrainingAudience>, CancellationToken>((_, _, rows, _) => stored = rows).Returns(Task.CompletedTask);
			_audienceService.Setup(x => x.ResolveAsync(DepartmentId, It.IsAny<IEnumerable<IContentAudienceRule>>())).ReturnsAsync(new ContentAudienceResolution());
			var scope = new TrainingRightScope { ManagesOwn = true, AdministeredMemberUserIds = new HashSet<string>(new[] { "a" }, StringComparer.OrdinalIgnoreCase) };

			await _service.SaveAudienceAsync(training, new[]
			{
				new ContentAudienceRule(ContentAudienceKinds.Group, "99"), new ContentAudienceRule(ContentAudienceKinds.User, "a"), new ContentAudienceRule(ContentAudienceKinds.User, "b")
			}, "captain", scope, Now);

			stored.Select(r => r.RefId).Should().Equal("a");
		}

		[Test]
		public async Task The_preview_lists_who_would_be_added_and_who_is_already_there()
		{
			var training = Seed();
			Row(training, "a");
			_audienceService.Setup(x => x.ResolveAsync(DepartmentId, It.IsAny<IEnumerable<IContentAudienceRule>>()))
				.ReturnsAsync(new ContentAudienceResolution { MemberUserIds = new HashSet<string>(new[] { "a", "b" }, StringComparer.OrdinalIgnoreCase) });

			var preview = await _service.PreviewAudienceAsync(training, new[] { new ContentAudienceRule(ContentAudienceKinds.Everyone) });

			preview.Added.Should().Equal("b");
			preview.AlreadyAssigned.Should().Equal("a");
			Stored("b").Should().BeNull("a preview writes nothing");
		}

		[Test]
		public async Task A_repeat_keeps_the_earlier_completion_as_history_and_assigns_everyone_again()
		{
			var training = Seed(dueDays: 30, configure: t => { t.RepeatMonths = 12; t.NextRepeatOn = Now.AddDays(-1); });
			Audience(training);
			Row(training, "done", configure: r => { r.Complete = true; r.Viewed = true; r.Passed = true; r.Score = 90; r.CompletedOn = Now.AddMonths(-2); r.NotifiedOn = Now.AddMonths(-12); r.Cycle = 2; });
			Row(training, "viewed", configure: r => { r.Viewed = true; r.AttemptCount = 2; r.Passed = false; });
			Row(training, "inactive");
			Row(training, "manual", configure: r => { r.Complete = true; r.RemovedOn = Now.AddDays(-5); });

			var count = await _service.RunRepeatAsync(training, Now);

			count.Should().Be(2);
			training.NextRepeatOn.Should().Be(Now.AddDays(-1).AddMonths(12));
			_trainings.Trainings.Single().NextRepeatOn.Should().Be(Now.AddDays(-1).AddMonths(12));

			_historyRows.Should().ContainSingle();
			var history = _historyRows.Single();
			history.UserId.Should().Be("done");
			history.Reason.Should().Be((int)TrainingCompletionHistoryReasons.Repeat);
			history.Score.Should().Be(90);
			history.Cycle.Should().Be(2);

			var done = Stored("done");
			done.Complete.Should().BeFalse();
			done.Passed.Should().BeNull();
			done.Cycle.Should().Be(3);
			done.AssignedOn.Should().Be(Now);
			done.DueOn.Should().Be(TrainingDueDates.DueForAssignment(training, Now, Department));
			done.NotifiedOn.Should().BeNull("the member is told about the new cycle");

			var viewed = Stored("viewed");
			viewed.Viewed.Should().BeTrue("progress on an open assignment is kept");
			viewed.AttemptCount.Should().Be(0);
			viewed.Cycle.Should().Be(1);

			Stored("inactive").Cycle.Should().Be(0);
			Stored("manual").Complete.Should().BeTrue("a removed member is not repeated");
		}

		[Test]
		public async Task A_repeat_of_a_dynamic_training_also_assigns_new_audience_members()
		{
			var training = Seed(configure: t => { t.RepeatMonths = 6; t.NextRepeatOn = Now.AddMinutes(-5); });
			Audience(training, new ContentAudienceRule(ContentAudienceKinds.Everyone));
			_audienceService.Setup(x => x.ResolveAsync(DepartmentId, It.IsAny<IEnumerable<IContentAudienceRule>>()))
				.ReturnsAsync(new ContentAudienceResolution { MemberUserIds = new HashSet<string>(new[] { "b" }, StringComparer.OrdinalIgnoreCase) });

			(await _service.RunRepeatAsync(training, Now)).Should().Be(1);
			Stored("b").Should().NotBeNull();
		}

		[Test]
		public async Task A_material_edit_reassigns_completed_members_and_keeps_their_result_in_history()
		{
			var training = Seed(dueDays: 7);
			Row(training, "done", configure: r => { r.Complete = true; r.Passed = true; r.Score = 100; r.CompletedOn = Now.AddDays(-2); });
			Row(training, "a");

			(await _service.ReassignCompletedAsync(training, "officer", Now)).Should().Be(1);

			_historyRows.Should().ContainSingle(h => h.UserId == "done" && h.Reason == (int)TrainingCompletionHistoryReasons.MaterialEdit && h.RecordedByUserId == "officer");
			Stored("done").Complete.Should().BeFalse();
			Stored("done").DueOn.Should().Be(TrainingDueDates.DueForAssignment(training, Now, Department));
			Stored("a").Cycle.Should().Be(0);
		}

		[Test]
		public async Task A_member_enrols_only_in_an_open_self_enrol_training_and_only_once()
		{
			var open = Seed(1, dueDays: 10, configure: t => { t.OpenToAllMembers = true; t.AllowSelfEnroll = true; });
			var notSelf = Seed(2, configure: t => t.OpenToAllMembers = true);
			var closed = Seed(3, configure: t => { t.OpenToAllMembers = true; t.AllowSelfEnroll = true; t.ClosedToNewAssignees = true; });
			_departments.Setup(x => x.GetDepartmentMemberAsync("a", DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentMember { DepartmentId = DepartmentId, UserId = "a" });
			_departments.Setup(x => x.GetDepartmentMemberAsync("hidden", DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentMember { DepartmentId = DepartmentId, UserId = "hidden", IsHidden = true });

			var row = await _service.SelfEnrollAsync(DepartmentId, 1, "a", DateTime.UtcNow);
			row.Should().NotBeNull();
			row.AssignmentSource.Should().Be((int)TrainingAssignmentSources.SelfEnrolled);
			(await _service.SelfEnrollAsync(DepartmentId, 1, "a", DateTime.UtcNow)).TrainingUserId.Should().Be(row.TrainingUserId);
			_users.GetAllAsync().Result.Count(u => u.UserId == "a").Should().Be(1);

			(await _service.SelfEnrollAsync(DepartmentId, 2, "a", DateTime.UtcNow)).Should().BeNull();
			(await _service.SelfEnrollAsync(DepartmentId, 3, "a", DateTime.UtcNow)).Should().BeNull();
			(await _service.SelfEnrollAsync(DepartmentId, 1, "hidden", DateTime.UtcNow)).Should().BeNull();
			(await _service.SelfEnrollAsync(99, 1, "a", DateTime.UtcNow)).Should().BeNull();
		}

		[Test]
		public async Task Removing_a_member_who_completed_keeps_the_completion_and_anyone_else_is_deleted()
		{
			var training = Seed();
			Row(training, "done", configure: r => { r.Complete = true; r.CompletedOn = Now.AddDays(-1); });
			Row(training, "a");

			(await _service.RemoveAssigneeAsync(DepartmentId, 1, "DONE", "officer")).Should().BeTrue();
			(await _service.RemoveAssigneeAsync(DepartmentId, 1, "a", "officer")).Should().BeTrue();
			(await _service.RemoveAssigneeAsync(DepartmentId, 1, "nobody", "officer")).Should().BeFalse();
			(await _service.RemoveAssigneeAsync(99, 1, "done", "officer")).Should().BeFalse();

			Stored("done").RemovedOn.Should().NotBeNull();
			Stored("done").RemovedByUserId.Should().Be("officer");
			Stored("done").Complete.Should().BeTrue();
			Stored("a").Should().BeNull();
			_events.Verify(x => x.SendMessage(It.Is<AuditEvent>(e => e.Type == AuditLogTypes.TrainingAssigneeRemoved)), Times.Exactly(2));
		}

		[Test]
		public async Task An_extension_moves_the_due_date_keeps_the_original_and_clears_the_reminder_stamps()
		{
			var training = Seed();
			var original = Now.AddDays(1);
			Row(training, "a", configure: r => { r.DueOn = original; r.LastReminderLeadDays = 1; r.RemindedOn = Now; r.OverdueNotifiedOn = Now; });
			var future = DateTime.UtcNow.Date.AddDays(30);

			(await _service.ExtendAsync(DepartmentId, 1, "a", future, " ", "officer")).Should().BeNull("a reason is required");
			(await _service.ExtendAsync(DepartmentId, 1, "a", DateTime.UtcNow.Date.AddDays(-3), "Sick", "officer")).Should().BeNull("a past date is refused");

			var row = await _service.ExtendAsync(DepartmentId, 1, "a", future, "Deployed", "officer");
			row.DueOn.Should().Be(TrainingDueDates.EndOfLocalDayUtc(future, Department));
			row.OriginalDueOn.Should().Be(original);
			row.ExtensionReason.Should().Be("Deployed");
			row.ExtendedByUserId.Should().Be("officer");
			row.LastReminderLeadDays.Should().BeNull();
			row.RemindedOn.Should().BeNull();
			row.OverdueNotifiedOn.Should().BeNull();

			await _service.ExtendAsync(DepartmentId, 1, "a", future.AddDays(10), "Again", "officer");
			Stored("a").OriginalDueOn.Should().Be(original, "the first due date is kept across extensions");
			_events.Verify(x => x.SendMessage(It.Is<AuditEvent>(e => e.Type == AuditLogTypes.TrainingAssignmentExtended)), Times.Exactly(2));
		}

		[Test]
		public async Task An_exemption_shows_as_exempt_never_complete_and_can_expire_or_be_cleared()
		{
			var training = Seed();
			Row(training, "a");
			var until = DateTime.UtcNow.Date.AddDays(60);

			(await _service.ExemptAsync(DepartmentId, 1, "a", "", null, "officer")).Should().BeNull();
			var row = await _service.ExemptAsync(DepartmentId, 1, "a", "Prior certificate", until, "officer");

			row.ExemptOn.Should().NotBeNull();
			row.ExemptUntil.Should().Be(TrainingDueDates.EndOfLocalDayUtc(until, Department));
			row.Complete.Should().BeFalse();
			row.IsExemptOn(DateTime.UtcNow).Should().BeTrue();
			row.IsExemptOn(row.ExemptUntil.Value.AddMinutes(1)).Should().BeFalse("an expired exemption is open again");
			TrainingDueDates.StateOf(training, row, Now.AddDays(-1), false, DateTime.UtcNow).Should().Be(TrainingAssignmentStates.Exempt);

			var cleared = await _service.ClearExemptionAsync(DepartmentId, 1, "a", "officer");
			cleared.ExemptOn.Should().BeNull();
			cleared.ExemptReason.Should().BeNull();
			(await _service.ClearExemptionAsync(DepartmentId, 1, "a", "officer")).Should().BeNull("nothing to clear");
		}

		[Test]
		public async Task A_reset_moves_a_completion_to_history_clears_progress_and_does_not_announce_again()
		{
			var training = Seed(dueDays: 5);
			var notified = Now.AddDays(-10);
			Row(training, "a", configure: r => { r.Complete = true; r.Viewed = true; r.Score = 80; r.Passed = true; r.NotifiedOn = notified; r.AttemptCount = 2; });

			var row = await _service.ResetMemberAsync(DepartmentId, 1, "a", "officer");

			row.Complete.Should().BeFalse();
			row.Viewed.Should().BeFalse();
			row.Score.Should().Be(0);
			row.Passed.Should().BeNull();
			row.AttemptCount.Should().Be(0);
			row.NotifiedOn.Should().Be(notified);
			_historyRows.Should().ContainSingle(h => h.Reason == (int)TrainingCompletionHistoryReasons.Reset && h.Score == 80);
		}

		[Test]
		public async Task A_double_submit_that_loses_the_insert_race_is_reported_as_already_assigned_not_duplicated()
		{
			// UX_TrainingUsers_TrainingId_UserId (T-D14): the repository answers null when the member already has a row.
			var users = new Mock<ITrainingUserRepository>();
			users.Setup(x => x.GetByTrainingIdAsync(1)).ReturnsAsync(new List<TrainingUser>());
			users.Setup(x => x.InsertAssignmentAsync(It.IsAny<TrainingUser>(), It.IsAny<CancellationToken>())).ReturnsAsync((TrainingUser)null);
			var service = new TrainingAssignmentService(_trainings, users.Object, _audiences.Object, _histories.Object, _audienceService.Object, _departments.Object, _events.Object);
			var training = new Training { TrainingId = 1, DepartmentId = DepartmentId };

			var outcome = await service.AssignAsync(training, new[] { "a" }, null, TrainingAssignmentSources.Manual, null, Now);

			outcome.Added.Should().BeEmpty();
			outcome.AlreadyAssigned.Should().Equal("a");
			training.Users.Should().BeEmpty();
			users.Verify(x => x.InsertAssignmentAsync(It.Is<TrainingUser>(u => u.UserId == "a" && u.AssignedOn == Now && u.AssignmentSource == (int)TrainingAssignmentSources.Manual), It.IsAny<CancellationToken>()), Times.Once);
			users.Verify(x => x.SaveOrUpdateAsync(It.IsAny<TrainingUser>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never, "assignees are written row by row, never by a whole-tree save");
		}

		[Test]
		public void Only_a_never_opened_audience_assignment_can_be_withdrawn()
		{
			TrainingAssignmentService.CanWithdraw(new TrainingUser { AssignmentSource = (int)TrainingAssignmentSources.Audience }).Should().BeTrue();
			TrainingAssignmentService.CanWithdraw(new TrainingUser { AssignmentSource = (int)TrainingAssignmentSources.Manual }).Should().BeFalse();
			TrainingAssignmentService.CanWithdraw(new TrainingUser { AssignmentSource = (int)TrainingAssignmentSources.SelfEnrolled }).Should().BeFalse();
			TrainingAssignmentService.CanWithdraw(new TrainingUser { AssignmentSource = (int)TrainingAssignmentSources.Audience, Viewed = true }).Should().BeFalse();
			TrainingAssignmentService.CanWithdraw(new TrainingUser { AssignmentSource = (int)TrainingAssignmentSources.Audience, ExtendedOn = Now }).Should().BeFalse();
			TrainingAssignmentService.CanWithdraw(new TrainingUser { AssignmentSource = (int)TrainingAssignmentSources.Audience, ExemptOn = Now }).Should().BeFalse();
			TrainingAssignmentService.CanWithdraw(null).Should().BeFalse();
		}
	}
}
