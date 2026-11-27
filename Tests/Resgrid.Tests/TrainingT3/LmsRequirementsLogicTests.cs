using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.TrainingT3
{
	/// <summary>
	/// Worker 93 (Training T3): only the departments the due-work query names run, each at most once per department-local day (claimed
	/// first, so the LMS gate is asked once per department per day, R-L9); a failed pass gives its day back; the undo pass runs for every
	/// department with an open enforcement action and asks no gate.
	/// </summary>
	[TestFixture]
	public class LmsRequirementsLogicTests
	{
		private static readonly DateTime Now = new DateTime(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc);

		private Mock<ILmsRequirementsRepository> _requirements;
		private Mock<ILmsRequirementEnforcementsRepository> _enforcements;
		private Mock<ILmsRequirementDepartmentStatesRepository> _states;
		private Mock<ILmsRequirementService> _engine;
		private Mock<ILmsPathService> _paths;
		private Mock<IDepartmentsService> _departments;
		private HashSet<(int, DateTime)> _claimed;

		[SetUp]
		public void SetUp()
		{
			_requirements = new Mock<ILmsRequirementsRepository>();
			_enforcements = new Mock<ILmsRequirementEnforcementsRepository>();
			_states = new Mock<ILmsRequirementDepartmentStatesRepository>();
			_engine = new Mock<ILmsRequirementService>();
			_paths = new Mock<ILmsPathService>();
			_departments = new Mock<IDepartmentsService>();
			_claimed = new HashSet<(int, DateTime)>();
			_departments.Setup(d => d.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync((int id, bool _) => new Department { DepartmentId = id, TimeZone = "UTC" });
			_states.Setup(s => s.TryClaimDayAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, DateTime day, DateTime _, CancellationToken __) => _claimed.Add((id, day.Date)));
			_states.Setup(s => s.ReleaseDayAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.Callback((int id, DateTime day, CancellationToken _) => _claimed.Remove((id, day.Date))).Returns(Task.CompletedTask);
			_engine.Setup(e => e.RunDailyAsync(It.IsAny<int>(), Now, It.IsAny<CancellationToken>())).ReturnsAsync(new LmsRequirementRunResult { Transitions = 1 });
			_enforcements.Setup(e => e.GetDepartmentIdsWithOpenAsync()).ReturnsAsync(new List<int>());
		}

		private Task<LmsRequirementsRunResult> Run() =>
			LmsRequirementsLogic.Process(_requirements.Object, _enforcements.Object, _states.Object, _engine.Object, _paths.Object, _departments.Object, Now);

		[Test]
		public async Task Only_departments_with_due_work_run_and_each_once_per_local_day()
		{
			_requirements.Setup(r => r.GetDepartmentIdsWithDueWorkAsync()).ReturnsAsync(new List<int> { 4, 9 });

			var first = await Run();
			var second = await Run();

			first.DailyPasses.Should().Be(2);
			second.DailyPasses.Should().Be(0, "the local day is already claimed");
			_engine.Verify(e => e.RunDailyAsync(4, Now, It.IsAny<CancellationToken>()), Times.Once());
			_engine.Verify(e => e.RunDailyAsync(9, Now, It.IsAny<CancellationToken>()), Times.Once());
			_engine.Verify(e => e.RunDailyAsync(It.IsNotIn(4, 9), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never(), "never a sweep of every department");
		}

		[Test]
		public async Task A_failed_pass_gives_its_day_back_and_the_others_still_run()
		{
			_requirements.Setup(r => r.GetDepartmentIdsWithDueWorkAsync()).ReturnsAsync(new List<int> { 4, 9 });
			_engine.Setup(e => e.RunDailyAsync(4, Now, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

			var result = await Run();

			result.Failures.Should().Be(1);
			result.DailyPasses.Should().Be(1);
			_claimed.Should().NotContain((4, Now.Date)).And.Contain((9, Now.Date));
		}

		[Test]
		public async Task The_undo_pass_runs_for_departments_with_open_actions_and_links_Academy_paths_first()
		{
			_requirements.Setup(r => r.GetDepartmentIdsWithDueWorkAsync()).ReturnsAsync(new List<int>());
			_enforcements.Setup(e => e.GetDepartmentIdsWithOpenAsync()).ReturnsAsync(new List<int> { 12 });
			_engine.Setup(e => e.UndoCompletedAsync(12, Now, It.IsAny<CancellationToken>())).ReturnsAsync(2);
			_paths.Setup(p => p.EnsureSystemPathsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(3);

			var result = await Run();

			result.Undone.Should().Be(2);
			result.PathsLinked.Should().Be(3);
			_engine.Verify(e => e.RunDailyAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never());
		}

		[Test]
		public async Task Worker_83_hands_the_same_membership_changes_to_the_requirements_once_per_department()
		{
			var changes = new Mock<IContentAudienceChangesRepository>();
			var assignments = new Mock<ITrainingAssignmentService>();
			var trainings = new Mock<ITrainingRepository>();
			changes.Setup(c => c.GetPendingAsync(TrainingAssignmentLogic.ChangesPerRun)).ReturnsAsync(new List<ContentAudienceChange>
			{
				new ContentAudienceChange { ContentAudienceChangeId = 1, DepartmentId = 10, UserId = "a", QueuedOn = Now },
				new ContentAudienceChange { ContentAudienceChangeId = 2, DepartmentId = 10, UserId = "A", QueuedOn = Now },
				new ContentAudienceChange { ContentAudienceChangeId = 3, DepartmentId = 20, UserId = null, QueuedOn = Now }
			});
			trainings.Setup(t => t.GetTrainingsDueForRepeatAsync(Now, TrainingAssignmentLogic.RepeatsPerRun)).ReturnsAsync(new List<Training>());
			assignments.Setup(a => a.ReevaluateMemberAsync(It.IsAny<int>(), It.IsAny<string>(), Now, It.IsAny<CancellationToken>())).ReturnsAsync(Tuple.Create(0, 0));
			assignments.Setup(a => a.ReevaluateDepartmentAsync(It.IsAny<int>(), Now, It.IsAny<CancellationToken>())).ReturnsAsync(Tuple.Create(0, 0));
			_engine.Setup(e => e.OnAudienceChangesAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>(), Now, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new LmsRequirementRunResult { MembersAdded = 1 });

			var result = await TrainingAssignmentLogic.Process(changes.Object, assignments.Object, trainings.Object, null, _engine.Object, Now);

			result.RequirementMembersAdded.Should().Be(2);
			_engine.Verify(e => e.OnAudienceChangesAsync(10, It.Is<IReadOnlyCollection<string>>(u => u.Count == 1), Now, It.IsAny<CancellationToken>()), Times.Once());
			_engine.Verify(e => e.OnAudienceChangesAsync(20, null, Now, It.IsAny<CancellationToken>()), Times.Once(), "a change without a member re-evaluates the whole department");
		}
	}
}
