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
using Resgrid.Model.Checklists;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	public partial class ChecklistWorkflowTests
	{
		private void UseAssignmentService(ScheduleClock clock, IChecklistAssignmentService assignments, IChecklistAssetSource assets = null)
		{
			_service = new ChecklistsService(_store, _authorization.Object, _access.Object, _uow.Object, _audits.Object, _outbox.Object,
				new Lazy<IProtectedReadService>(() => _read.Object), new Lazy<IProtectedWriteService>(() => _write.Object), _scanner.Object, clock, assignments, assets);
		}
		[Test]
		public async Task Assignments_gate_start_and_active_run_edits_without_changing_witness_ownership()
		{
			var setup = await Scheduled(); var assignments = new Mock<IChecklistAssignmentService>(); UseAssignmentService(setup.Clock, assignments.Object);
			setup.Input.AssignmentType = 1; setup.Input.AssignmentId = "author";
			await _service.SaveScheduleAsync(_actor, setup.Input);
			await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime); setup.Clock.Now = setup.Clock.Now.AddHours(1);
			var occurrence = (await _store.ListAsync<ChecklistOccurrence>(77)).OrderBy(o => o.PeriodStartUtc).First();
			Func<Task> denied = () => _service.StartOccurrenceAsync(_actor, occurrence.Id); (await denied.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(403);
			assignments.Setup(a => a.CanPerformAsync(_actor, It.IsAny<ChecklistSchedule>())).ReturnsAsync(true);
			var run = await _service.StartOccurrenceAsync(_actor, occurrence.Id); var view = await _service.GetRunAsync(_actor, run);
			assignments.Setup(a => a.CanPerformAsync(_actor, It.IsAny<ChecklistSchedule>())).ReturnsAsync(false);
			Func<Task> revoked = () => _service.SaveRunAsync(_actor, run, Answers(view.Form), true);
			(await revoked.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(403);
			(await _store.GetAsync<ChecklistCompletion>(77, run)).State.Should().Be(0);
			_events.Should().Contain(e => e.Trigger == WorkflowTriggerEventType.ChecklistScheduleChanged);
			JsonConvert.SerializeObject(_events).Should().NotContain("SYNTHETIC-SENSITIVE");
		}
		[Test]
		public async Task Calendar_uses_stable_virtual_ids_tenant_scope_and_redacted_titles_without_copying_notes()
		{
			var setup = await Scheduled(); await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime);
			var from = setup.Clock.Now.UtcDateTime; var entries = await _service.CalendarAsync(_actor, from, from.AddDays(8));
			entries.Should().HaveCount(7).And.OnlyContain(e => e.Id == "checklist:" + e.OccurrenceId && !e.IsRedacted);
			JsonConvert.SerializeObject(entries).Should().NotContain("SYNTHETIC-SENSITIVE-NOTES");
			(await _service.CalendarAsync(new ChecklistActor { DepartmentId = 88, UserId = "foreign" }, from, from.AddDays(8))).Should().BeEmpty();
			_read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult { RedactedFields = { "checklistschedules.content" } }));
			(await _service.CalendarAsync(_actor, from, from.AddDays(8))).Should().OnlyContain(e => e.IsRedacted && e.Title == "REDACTED");
			_authorization.Setup(a => a.TargetAsync(_actor, It.IsAny<ChecklistTargetType>(), It.IsAny<string>())).ThrowsAsync(new ChecklistException(404, "Target is unavailable."));
			(await _service.CalendarAsync(_actor, from, from.AddDays(8))).Should().BeEmpty();
			Func<Task> excessive = () => _service.CalendarAsync(_actor, from, from.AddDays(94)); (await excessive.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(400);
		}
		[Test]
		public async Task Calendar_pages_past_500_occurrences_and_excludes_cancelled_and_out_of_range_rows()
		{
			var setup = await Scheduled(); await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime);
			var first = (await _store.ListAsync<ChecklistOccurrence>(77)).First();
			for (var i = 0; i < 503; i++) { var row = JsonConvert.DeserializeObject<ChecklistOccurrence>(JsonConvert.SerializeObject(first)); row.Id = Guid.NewGuid().ToString(); await _store.WriteAsync(row, true); }
			var from = setup.Clock.Now.UtcDateTime;
			(await _service.CalendarAsync(_actor, from, from.AddDays(8))).Should().HaveCount(510);
			first.State = 6; await _store.WriteAsync(first, false);
			(await _service.CalendarAsync(_actor, from, from.AddDays(8))).Should().HaveCount(509);
			(await _service.CalendarAsync(_actor, from.AddDays(20), from.AddDays(21))).Should().BeEmpty();
		}
		[Test]
		public async Task Asset_templates_and_existing_schedules_degrade_when_the_optional_source_is_absent()
		{
			var template = ChecklistTemplateCatalog.All.First(t => t.SuggestedTargetType == ChecklistTargetType.InventoryAsset);
			ChecklistForm.FromTemplate(template).TargetType.Should().Be(ChecklistTargetType.Department);
			ChecklistForm.FromTemplate(template, true).TargetType.Should().Be(ChecklistTargetType.InventoryAsset);
			(await _service.AssetTargetsAvailableAsync(_actor)).Should().BeFalse();
			Func<Task> unavailable = () => _service.SaveDefinitionAsync(_actor, null, 0, ChecklistForm.FromTemplate(template, true));
			await unavailable.Should().ThrowAsync<ChecklistException>();
			var setup = await Scheduled(); var schedule = await _store.GetAsync<ChecklistSchedule>(77, setup.Input.Id);
			schedule.TargetType = 5; schedule.TargetId = Guid.NewGuid().ToString(); await _store.WriteAsync(schedule, false);
			(await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime)).Generated.Should().Be(0);
			(await _store.GetAsync<ChecklistSchedule>(77, schedule.Id)).IsSuspended.Should().BeTrue();
		}
		[TestCase("08:00,09:00"), TestCase("25:00"), TestCase("8:00"), TestCase("12:30:00")]
		public async Task Digest_time_requires_one_valid_24_hour_time(string value)
		{
			Func<Task> save = () => _service.SaveReminderSettingsAsync(_actor, new ChecklistReminderSettingsInput { FixedDigestTime = value });
			(await save.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(400);
		}
		private async Task<ChecklistOccurrence> TimedOccurrence(DateTime now, int minute, string zone = "UTC")
		{
			var row = await ReminderOccurrence(); row.PeriodStartUtc = now.AddHours(-1); row.WindowEndUtc = now.AddHours(8); await _store.WriteAsync(row, false);
			var schedule = await _store.GetAsync<ChecklistSchedule>(77, row.ScheduleId); schedule.ActiveFromUtc = now.AddDays(-1); await _store.WriteAsync(schedule, false);
			var settings = (await _store.ListAsync<DepartmentChecklistSettings>(77)).Single(); settings.RemindersActiveFromUtc = settings.DigestActiveFromUtc = now.AddHours(-2);
			settings.LastDigestSweepUtc = now.AddMinutes(-5); settings.NotifyBeforeMinutes = 0; settings.FixedDigestMinute = minute; await _store.WriteAsync(settings, false); return row;
		}
		[TestCase(2026, 3, 8, 7, 0, 150), TestCase(2026, 11, 1, 5, 30, 90)]
		public async Task Fixed_digest_handles_DST_gaps_folds_and_replays_once_per_local_day(int year, int month, int day, int hour, int minute, int localMinute)
		{
			var now = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc); await TimedOccurrence(now, localMinute);
			var harness = Reminders(); harness.Departments.Setup(s => s.GetDepartmentByIdAsync(77, true)).ReturnsAsync(new Department { DepartmentId = 77, ManagingUserId = "author", TimeZone = "America/New_York" });
			(await harness.Service.SweepAsync(now)).HandedOff.Should().Be(1);
			(await harness.Service.SweepAsync(now.AddHours(1))).HandedOff.Should().Be(0);
			harness.Notices.Should().ContainSingle(n => n.Kind == (int)ChecklistReminderKind.FixedTime && n.PeriodKey == "fixed:" + now.ToString("yyyyMMdd"));
			_read.Invocations.Clear(); await harness.Service.SweepAsync(now.AddMinutes(5)); _read.Invocations.Should().BeEmpty();
		}
		[Test]
		public async Task Fixed_digest_reconciles_an_asset_move_before_delivery_to_current_unit_crew()
		{
			var now = new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc); var row = await TimedOccurrence(now, 540);
			var assets = new Mock<IChecklistAssetSource>(); assets.Setup(a => a.IsAvailableAsync(77)).ReturnsAsync(true); var unitId = 12;
			row.TargetType = 5; row.TargetId = Guid.NewGuid().ToString(); await _store.WriteAsync(row, false);
			assets.Setup(a => a.RoutingAsync(77, row.TargetId)).ReturnsAsync(() => new ChecklistAssetTarget { DepartmentId = 77, Id = row.TargetId, UnitId = unitId });
			assets.Setup(a => a.CanReceiveReminderAsync(77, It.IsAny<string>(), row.TargetId)).ReturnsAsync(true);
			var harness = Reminders(assets: assets.Object);
			foreach (var id in new[] { 12, 13 }) { var user = id == 12 ? "crew" : "author"; harness.Units.Setup(u => u.GetUnitByIdAsync(id)).ReturnsAsync(new Unit { UnitId = id, DepartmentId = 77 }); harness.Units.Setup(u => u.GetActiveRolesForUnitAsync(id)).ReturnsAsync(new List<UnitActiveRole> { new UnitActiveRole { UnitId = id, DepartmentId = 77, UserId = user } }); }
			harness.BeforeClaim = () => unitId = 13;
			(await harness.Service.SweepAsync(now)).Suppressed.Should().Be(1);
			(await harness.Service.SweepAsync(now.AddMinutes(5))).HandedOff.Should().Be(1);
			((string)harness.Communication.Invocations.Single().Arguments[0]).Should().Be("author");
			(await harness.Service.SweepAsync(now.AddMinutes(10))).HandedOff.Should().Be(0);
			assets.Verify(a => a.GetAsync(It.IsAny<ChecklistActor>(), It.IsAny<string>()), Times.Never);
		}
		[Test]
		public async Task Shift_digest_uses_current_unit_staff_and_fixed_time_overrides_shift_triggers()
		{
			var now = new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc); var row = await TimedOccurrence(now, 600);
			row.TargetType = 1; row.TargetId = "12"; await _store.WriteAsync(row, false);
			_store.ShiftStarts.Add(new ChecklistShiftStart { UnitId = 12, WorkshiftDayId = Guid.NewGuid().ToString(), StartUtc = now });
			var harness = Reminders(); harness.Units.Setup(u => u.GetUnitByIdAsync(12)).ReturnsAsync(new Unit { UnitId = 12, DepartmentId = 77 }); harness.Units.Setup(u => u.GetActiveRolesForUnitAsync(12)).ReturnsAsync(new List<UnitActiveRole> { new UnitActiveRole { UnitId = 12, DepartmentId = 77, UserId = "crew" } });
			(await harness.Service.SweepAsync(now)).HandedOff.Should().Be(0);
			var settings = (await _store.ListAsync<DepartmentChecklistSettings>(77)).Single(); settings.FixedDigestMinute = null; await _store.WriteAsync(settings, false);
			(await harness.Service.SweepAsync(now)).HandedOff.Should().Be(1);
			harness.Notices.Should().ContainSingle(n => n.Kind == 3 && n.RecipientUserId == "crew");
			(await harness.Service.SweepAsync(now.AddMinutes(5))).HandedOff.Should().Be(0);
		}
		[Test]
		public async Task Access_observer_records_a_complete_off_on_interval_without_worker_sweeps_or_decryption()
		{
			var setup = await Scheduled(); await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime);
			var observer = new ChecklistAccessMutationObserver(_store, Mock.Of<IDepartmentSettingsRepository>(), setup.Clock);
			await observer.BeforeChangeAsync(77, default); await observer.AfterChangeAsync((_, _) => Task.FromResult(false), default);
			(await _store.GetAsync<ChecklistSchedule>(77, setup.Input.Id)).IsSuspended.Should().BeTrue();
			setup.Clock.Now = setup.Clock.Now.AddDays(3);
			await observer.BeforeChangeAsync(77, default); await observer.AfterChangeAsync((_, _) => Task.FromResult(true), default);
			var resumed = await _store.GetAsync<ChecklistSchedule>(77, setup.Input.Id); resumed.IsSuspended.Should().BeFalse(); resumed.ActiveFromUtc.Should().Be(setup.Clock.Now.UtcDateTime);
			_read.Invocations.Clear(); (await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime)).Missed.Should().Be(0); _read.Invocations.Should().BeEmpty();
			(await _store.ListAsync<ChecklistOccurrence>(77)).Where(o => o.PeriodStartUtc < resumed.ActiveFromUtc).Should().OnlyContain(o => o.State == 6);
		}
	}
}
