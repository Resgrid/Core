using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.TrainingT0
{
	/// <summary>
	/// Worker 83: starts from the membership-change queue and the due-repeat query, never from a sweep of every department.
	/// </summary>
	[TestFixture]
	public class TrainingAssignmentLogicTests
	{
		private static readonly DateTime Now = new DateTime(2026, 11, 24, 15, 0, 0, DateTimeKind.Utc);
		private Mock<IContentAudienceChangesRepository> _changes;
		private Mock<ITrainingAssignmentService> _assignments;
		private Mock<ITrainingRepository> _trainings;
		private List<int> _processed;

		[SetUp]
		public void SetUp()
		{
			_changes = new Mock<IContentAudienceChangesRepository>();
			_assignments = new Mock<ITrainingAssignmentService>();
			_trainings = new Mock<ITrainingRepository>();
			_processed = new List<int>();

			_changes.Setup(x => x.MarkProcessedAsync(It.IsAny<IEnumerable<int>>(), Now, It.IsAny<CancellationToken>()))
				.Callback<IEnumerable<int>, DateTime, CancellationToken>((ids, _, _) => _processed.AddRange(ids))
				.ReturnsAsync(1);
			_trainings.Setup(x => x.GetTrainingsDueForRepeatAsync(Now, TrainingAssignmentLogic.RepeatsPerRun)).ReturnsAsync(new List<Training>());
			_assignments.Setup(x => x.ReevaluateMemberAsync(It.IsAny<int>(), It.IsAny<string>(), Now, It.IsAny<CancellationToken>())).ReturnsAsync(Tuple.Create(1, 0));
			_assignments.Setup(x => x.ReevaluateDepartmentAsync(It.IsAny<int>(), Now, It.IsAny<CancellationToken>())).ReturnsAsync(Tuple.Create(2, 1));
		}

		private static ContentAudienceChange Change(int id, int departmentId, string userId) =>
			new ContentAudienceChange { ContentAudienceChangeId = id, DepartmentId = departmentId, UserId = userId, QueuedOn = Now.AddMinutes(-id) };

		private Task<TrainingAssignmentRunResult> Run() => TrainingAssignmentLogic.Process(_changes.Object, _assignments.Object, _trainings.Object, Now);

		[Test]
		public async Task Queued_members_are_re_evaluated_once_each_and_marked_processed()
		{
			_changes.Setup(x => x.GetPendingAsync(TrainingAssignmentLogic.ChangesPerRun)).ReturnsAsync(new List<ContentAudienceChange>
			{
				Change(1, 10, "a"), Change(2, 10, "A"), Change(3, 10, "b"), Change(4, 20, "c")
			});

			var result = await Run();

			_assignments.Verify(x => x.ReevaluateMemberAsync(10, It.Is<string>(u => string.Equals(u, "a", StringComparison.OrdinalIgnoreCase)), Now, It.IsAny<CancellationToken>()), Times.Once);
			_assignments.Verify(x => x.ReevaluateMemberAsync(10, "b", Now, It.IsAny<CancellationToken>()), Times.Once);
			_assignments.Verify(x => x.ReevaluateMemberAsync(20, "c", Now, It.IsAny<CancellationToken>()), Times.Once);
			_assignments.Verify(x => x.ReevaluateDepartmentAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
			_processed.Should().BeEquivalentTo(new[] { 1, 2, 3, 4 });
			result.ChangesProcessed.Should().Be(4);
			result.Added.Should().Be(3);
		}

		[Test]
		public async Task A_change_without_a_member_re_evaluates_the_department_once_instead()
		{
			_changes.Setup(x => x.GetPendingAsync(It.IsAny<int>())).ReturnsAsync(new List<ContentAudienceChange> { Change(1, 10, "a"), Change(2, 10, null) });

			var result = await Run();

			_assignments.Verify(x => x.ReevaluateDepartmentAsync(10, Now, It.IsAny<CancellationToken>()), Times.Once);
			_assignments.Verify(x => x.ReevaluateMemberAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
			result.Added.Should().Be(2);
			result.Withdrawn.Should().Be(1);
		}

		[Test]
		public async Task A_failing_department_is_still_marked_and_does_not_block_the_others()
		{
			_changes.Setup(x => x.GetPendingAsync(It.IsAny<int>())).ReturnsAsync(new List<ContentAudienceChange> { Change(1, 10, "a"), Change(2, 20, "b") });
			_assignments.Setup(x => x.ReevaluateMemberAsync(10, "a", Now, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

			var result = await Run();

			result.Failures.Should().Be(1);
			_processed.Should().BeEquivalentTo(new[] { 1, 2 });
			_assignments.Verify(x => x.ReevaluateMemberAsync(20, "b", Now, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Due_repeats_run_and_processed_changes_are_purged()
		{
			_changes.Setup(x => x.GetPendingAsync(It.IsAny<int>())).ReturnsAsync(new List<ContentAudienceChange>());
			var due = new Training { TrainingId = 5, DepartmentId = 10, RepeatMonths = 12, NextRepeatOn = Now.AddHours(-1) };
			var broken = new Training { TrainingId = 6, DepartmentId = 10, RepeatMonths = 12, NextRepeatOn = Now.AddHours(-1) };
			_trainings.Setup(x => x.GetTrainingsDueForRepeatAsync(Now, TrainingAssignmentLogic.RepeatsPerRun)).ReturnsAsync(new List<Training> { due, broken });
			_assignments.Setup(x => x.RunRepeatAsync(due, Now, It.IsAny<CancellationToken>())).ReturnsAsync(4);
			_assignments.Setup(x => x.RunRepeatAsync(broken, Now, It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("x"));

			var result = await Run();

			result.Repeated.Should().Be(4);
			result.Failures.Should().Be(1);
			_changes.Verify(x => x.PurgeProcessedAsync(Now.AddDays(-TrainingAssignmentLogic.ProcessedRetentionDays), It.IsAny<CancellationToken>()), Times.Once);
			_changes.Verify(x => x.MarkProcessedAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task An_idle_run_touches_no_department()
		{
			_changes.Setup(x => x.GetPendingAsync(It.IsAny<int>())).ReturnsAsync(new List<ContentAudienceChange>());

			var result = await Run();

			result.ChangesProcessed.Should().Be(0);
			_assignments.VerifyNoOtherCalls();
		}
	}
}
