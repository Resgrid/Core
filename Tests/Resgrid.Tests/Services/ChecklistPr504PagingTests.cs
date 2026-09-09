using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Checklists;

namespace Resgrid.Tests.Services
{
	public partial class ChecklistWorkflowTests
	{
		[Test]
		public async Task Due_pages_count_visible_rows_and_recheck_management_on_the_next_operation()
		{
			var setup = await Scheduled();
			var schedule = await _store.GetAsync<ChecklistSchedule>(77, setup.Input.Id);
			var expected = new List<string>();
			for (var i = 0; i < 125; i++)
			{
				var row = new ChecklistOccurrence { DepartmentId = 77, ParentId = schedule.ParentId, ScheduleId = schedule.Id, VersionId = schedule.VersionId,
					TargetType = (int)ChecklistTargetType.Group, TargetId = i < 70 ? "hidden" : "visible", State = (int)ChecklistOccurrenceState.Scheduled,
					PeriodStartUtc = setup.Clock.Now.UtcDateTime.AddMinutes(i - 125), WindowEndUtc = setup.Clock.Now.UtcDateTime.AddDays(1), Content = "OCCURRENCE-CONTENT-CANARY" };
				await _store.WriteAsync(row, true); if (i >= 70) expected.Add(row.Id);
			}
			_authorization.Setup(a => a.TargetAsync(_actor, ChecklistTargetType.Group, "hidden")).ThrowsAsync(new ChecklistException(404, "Hidden target"));
			_authorization.Invocations.Clear();
			var first = await _service.DueAsync(_actor, includeNext: true);
			first.Select(v => v.Occurrence.Id).Should().Equal(expected.Take(51));
			first.Should().OnlyContain(v => v.CanSkip && v.Occurrence.Content == null);
			_authorization.Verify(a => a.CanManageAsync(_actor), Times.Once);
			_authorization.Setup(a => a.CanManageAsync(_actor)).ReturnsAsync(false);
			_authorization.Invocations.Clear();
			var second = await _service.DueAsync(_actor, page: 1, includeNext: true);
			second.Select(v => v.Occurrence.Id).Should().Equal(expected.Skip(50));
			second.Should().OnlyContain(v => !v.CanSkip);
			_authorization.Verify(a => a.CanManageAsync(_actor), Times.Once);
		}

		[Test]
		public async Task Calendar_target_snapshot_is_reused_only_within_one_operation_and_rechecks_revocation()
		{
			var setup = await Scheduled(); await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime);
			_authorization.Invocations.Clear();
			var from = setup.Clock.Now.UtcDateTime.Date; var until = from.AddDays(7);
			(await _service.CalendarAsync(_actor, from, until)).Should().HaveCount(7);
			_authorization.Verify(a => a.TargetAsync(_actor, ChecklistTargetType.Department, "77"), Times.Once);
			_authorization.Setup(a => a.TargetAsync(_actor, ChecklistTargetType.Department, "77")).ThrowsAsync(new ChecklistException(404, "Revoked target"));
			_authorization.Invocations.Clear();
			(await _service.CalendarAsync(_actor, from, until)).Should().BeEmpty();
			_authorization.Verify(a => a.TargetAsync(_actor, ChecklistTargetType.Department, "77"), Times.Once);
		}
	}
}
