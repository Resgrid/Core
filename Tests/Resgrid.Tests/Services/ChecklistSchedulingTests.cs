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
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
    public partial class ChecklistWorkflowTests
    {
        private sealed class ScheduleClock : TimeProvider
        {
            public DateTimeOffset Now = new DateTimeOffset(2026, 9, 8, 7, 0, 0, TimeSpan.Zero);
            public override DateTimeOffset GetUtcNow() => Now;
        }
        private async Task<(ChecklistScheduleInput Input, ScheduleClock Clock)> Scheduled()
        {
            var clock = new ScheduleClock();
            _service = new ChecklistsService(_store, _authorization.Object, _access.Object, _uow.Object, _audits.Object, _outbox.Object,
                new Lazy<IProtectedReadService>(() => _read.Object), new Lazy<IProtectedWriteService>(() => _write.Object), _scanner.Object, clock);
            var definition = await _service.SaveDefinitionAsync(_actor, null, 0, Form());
            await _service.PublishAsync(_actor, definition, 1);
            var input = new ChecklistScheduleInput { DefinitionId = definition, Name = "SYNTHETIC-SENSITIVE-SCHEDULE", Notes = "SYNTHETIC-SENSITIVE-NOTES", TargetId = "77", StartDate = clock.Now.Date };
            input.Id = await _service.SaveScheduleAsync(_actor, input); input.Revision = 1;
            return (input, clock);
        }
        [Test]
        public async Task Scheduling_pins_versions_and_generates_once_without_decrypting_or_copying_content()
        {
            var setup = await Scheduled();
            var schedule = (await _service.GetScheduleAsync(_actor, setup.Input.Id)).Schedule;
            var changed = Form(); changed.Name = "New published version";
            await _service.SaveDefinitionAsync(_actor, setup.Input.DefinitionId, 2, changed);
            await _service.PublishAsync(_actor, setup.Input.DefinitionId, 3);
            _read.Invocations.Clear();
            var first = await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime);
            first.Errors.Should().Be(0); first.Generated.Should().Be(7);
            (await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime)).Generated.Should().Be(0);
            _read.Invocations.Should().BeEmpty("the unattended worker must never request decryption");
            var occurrences = await _store.ListAsync<ChecklistOccurrence>(77);
            occurrences.Should().HaveCount(7); occurrences.Select(o => o.PeriodStartUtc).Should().OnlyHaveUniqueItems();
            occurrences.Should().OnlyContain(o => o.Content == null && o.VersionId == schedule.VersionId);
            setup.Clock.Now = setup.Clock.Now.AddHours(1);
            var due = occurrences.OrderBy(o => o.PeriodStartUtc).First();
            var run = await _service.StartOccurrenceAsync(_actor, due.Id);
            (await _service.StartOccurrenceAsync(_actor, due.Id)).Should().Be(run);
            (await _service.GetRunAsync(_actor, run)).Form.Name.Should().Be("Shift readiness");
            (await _store.ListAsync<ChecklistCompletion>(77)).Should().ContainSingle();
            JsonConvert.SerializeObject(_events).Should().NotContain("SYNTHETIC-SENSITIVE");
        }
        [Test]
        public async Task Missed_check_emits_once_and_late_completion_keeps_missed_timestamp()
        {
            var setup = await Scheduled(); await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime);
            setup.Clock.Now = setup.Clock.Now.AddHours(3);
            var sweep = await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime);
            sweep.Errors.Should().Be(0); sweep.Missed.Should().Be(1);
            (await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime)).Missed.Should().Be(0);
            _events.Count(e => e.Trigger == WorkflowTriggerEventType.ChecklistMissed).Should().Be(1);
            var occurrence = (await _store.ListAsync<ChecklistOccurrence>(77)).Single(o => o.State == 4);
            var run = await _service.StartOccurrenceAsync(_actor, occurrence.Id);
            var view = await _service.GetRunAsync(_actor, run);
            await _service.SaveRunAsync(_actor, run, Answers(view.Form), true);
            occurrence = await _store.GetAsync<ChecklistOccurrence>(77, occurrence.Id);
            occurrence.State.Should().Be(2); occurrence.MissedOn.Should().Be(setup.Clock.Now.UtcDateTime);
            (await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime.AddDays(1))).Errors.Should().Be(0);
        }
        [Test]
        public async Task Excused_skip_requires_reason_revision_and_permission_and_does_not_emit_the_reason()
        {
            var setup = await Scheduled(); await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime);
            var row = (await _store.ListAsync<ChecklistOccurrence>(77)).First();
            Func<Task> empty = () => _service.SkipOccurrenceAsync(_actor, row.Id, 1, " "); await empty.Should().ThrowAsync<ChecklistException>();
            Func<Task> stale = () => _service.SkipOccurrenceAsync(_actor, row.Id, 0, "SYNTHETIC-PRIVATE-REASON"); await stale.Should().ThrowAsync<ChecklistException>();
            _authorization.Setup(a => a.CanManageAsync(_actor)).ReturnsAsync(false);
            Func<Task> denied = () => _service.SkipOccurrenceAsync(_actor, row.Id, 1, "SYNTHETIC-PRIVATE-REASON"); await denied.Should().ThrowAsync<ChecklistException>();
            _authorization.Setup(a => a.CanManageAsync(_actor)).ReturnsAsync(true);
            await _service.SkipOccurrenceAsync(_actor, row.Id, 1, "SYNTHETIC-PRIVATE-REASON");
            (await _store.GetAsync<ChecklistOccurrence>(77, row.Id)).State.Should().Be(5);
            JsonConvert.SerializeObject(_events).Should().NotContain("SYNTHETIC-PRIVATE");
            _events.Count(e => e.Trigger == WorkflowTriggerEventType.ChecklistOccurrenceSkipped).Should().Be(1);
            Func<Task> reopen = () => _service.StartOccurrenceAsync(_actor, row.Id); await reopen.Should().ThrowAsync<ChecklistException>();
        }
        [Test]
        public async Task Edits_cancel_future_unstarted_checks_and_preserve_started_check_and_pinned_version()
        {
            var setup = await Scheduled(); await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime);
            setup.Clock.Now = setup.Clock.Now.AddHours(1);
            var first = (await _store.ListAsync<ChecklistOccurrence>(77)).OrderBy(o => o.PeriodStartUtc).First();
            var run = await _service.StartOccurrenceAsync(_actor, first.Id);
            setup.Input.TimesOfDay = "12:00"; await _service.SaveScheduleAsync(_actor, setup.Input);
            (await _store.ListAsync<ChecklistOccurrence>(77)).Count(o => o.State == 6).Should().Be(6);
            (await _store.GetAsync<ChecklistOccurrence>(77, first.Id)).CompletionId.Should().Be(run);
            (await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime)).Generated.Should().Be(7);
            (await _store.ListAsync<ChecklistOccurrence>(77)).Where(o => o.State == 3).Should().OnlyContain(o => o.ScheduleRevision == 2 && o.PeriodStartUtc.Value.Hour == 12);
            Func<Task> stale = () => _service.SaveScheduleAsync(_actor, setup.Input); await stale.Should().ThrowAsync<ChecklistException>();
        }
        [Test]
        public async Task Module_pause_suspends_generation_and_resume_does_not_report_paused_checks_as_missed()
        {
            var setup = await Scheduled(); await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime);
            _access.Setup(a => a.CanUseChecklistsAsync(77)).ReturnsAsync(false);
            (await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime)).Errors.Should().Be(0);
            setup.Clock.Now = setup.Clock.Now.AddDays(10);
            _access.Setup(a => a.CanUseChecklistsAsync(77)).ReturnsAsync(true);
            var sweep = await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime);
            sweep.Errors.Should().Be(0); sweep.Missed.Should().Be(0); sweep.Generated.Should().Be(7);
            (await _store.ListAsync<ChecklistOccurrence>(77)).Count(o => o.State == 6).Should().Be(7);
            _events.Should().NotContain(e => e.Trigger == WorkflowTriggerEventType.ChecklistMissed);
        }
        [Test]
        public async Task Scheduling_worker_rolls_back_on_ADP_failure_and_never_advances_the_cursor()
        {
            var setup = await Scheduled();
            var before = (await _service.GetScheduleAsync(_actor, setup.Input.Id)).Schedule;
            _write.SetReturnsDefault(Task.FromResult(ProtectedWriteResult.Blocked("broker_unavailable")));
            (await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime)).Errors.Should().Be(1);
            (await _store.ListAsync<ChecklistOccurrence>(77)).Should().BeEmpty();
            (await _store.GetAsync<ChecklistSchedule>(77, before.Id)).GeneratedThroughUtc.Should().Be(before.GeneratedThroughUtc);
            _write.SetReturnsDefault(Task.FromResult(ProtectedWriteResult.Allowed()));
            (await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime)).Generated.Should().Be(7);
        }
        [Test]
        public async Task Protected_schedule_requires_catalog_17_and_denies_a_redacted_edit()
        {
            var setup = await Scheduled();
            new ProtectedFieldCatalog().GetAddedBetween(16, 17).Should().ContainSingle(f => f.FieldId == "checklistschedules.content");
            _read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult { RedactedFields = { "checklistschedules.content" } }));
            Func<Task> save = () => _service.SaveScheduleAsync(_actor, setup.Input);
            (await save.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(403);
            (await _store.GetAsync<ChecklistSchedule>(77, setup.Input.Id)).Revision.Should().Be(1);
        }
        [Test]
        public async Task Workshift_day_changes_reconcile_future_checks_without_duplicates_or_reviving_excused_checks()
        {
            var setup = await Scheduled(); setup.Input.Frequency = ChecklistScheduleFrequency.PerShift; setup.Input.WorkshiftId = Guid.NewGuid().ToString();
            await _service.SaveScheduleAsync(_actor, setup.Input);
            var first = setup.Clock.Now.UtcDateTime.AddHours(1); _store.WorkshiftDays.Add(first);
            (await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime)).Generated.Should().Be(1);
            _store.WorkshiftDays[0] = first.AddHours(1);
            (await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime)).Generated.Should().Be(1);
            var rows = await _store.ListAsync<ChecklistOccurrence>(77); rows.Should().HaveCount(2); rows.Single(o => o.PeriodStartUtc == first).State.Should().Be(6);
            _store.WorkshiftDays.Add(first);
            (await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime)).Generated.Should().Be(1);
            (await _store.ListAsync<ChecklistOccurrence>(77)).Should().HaveCount(2);
            var restored = (await _store.ListAsync<ChecklistOccurrence>(77)).Single(o => o.PeriodStartUtc == first);
            await _service.SkipOccurrenceAsync(_actor, restored.Id, restored.Revision, "Shift cancelled by supervisor");
            (await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime)).Generated.Should().Be(0);
            (await _store.GetAsync<ChecklistOccurrence>(77, restored.Id)).State.Should().Be(5);
            setup.Clock.Now = setup.Clock.Now.AddMinutes(5);
            (await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime)).Errors.Should().Be(0);
            (await _store.GetAsync<ChecklistSchedule>(77, setup.Input.Id)).GeneratedThroughUtc.Should().Be(setup.Clock.Now.UtcDateTime.AddDays(7));
        }
        [TestCase(false), TestCase(true)]
        public async Task Editing_a_schedule_preserves_started_and_excused_periods_without_generating_past_checks(bool workshift)
        {
            var setup = await Scheduled();
            if (workshift)
            {
                setup.Input.Frequency = ChecklistScheduleFrequency.PerShift; setup.Input.WorkshiftId = Guid.NewGuid().ToString();
                await _service.SaveScheduleAsync(_actor, setup.Input); setup.Input.Revision++;
                _store.WorkshiftDays.AddRange(new[] { setup.Clock.Now.UtcDateTime.AddHours(1), setup.Clock.Now.UtcDateTime.AddDays(1).AddHours(1) });
            }
            await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime);
            var first = (await _store.ListAsync<ChecklistOccurrence>(77)).OrderBy(o => o.PeriodStartUtc).First();
            var second = (await _store.ListAsync<ChecklistOccurrence>(77)).OrderBy(o => o.PeriodStartUtc).Skip(1).First();
            setup.Clock.Now = setup.Clock.Now.AddHours(1); await _service.StartOccurrenceAsync(_actor, first.Id);
            await _service.SkipOccurrenceAsync(_actor, second.Id, second.Revision, "Supervisor approved");
            await _service.SaveScheduleAsync(_actor, setup.Input); setup.Input.Revision++;
            (await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime)).Errors.Should().Be(0);
            var rows = await _store.ListAsync<ChecklistOccurrence>(77);
            rows.Count(o => o.PeriodStartUtc == first.PeriodStartUtc).Should().Be(1);
            rows.Count(o => o.PeriodStartUtc == second.PeriodStartUtc).Should().Be(1);
            setup.Clock.Now = setup.Clock.Now.AddHours(1);
            await _service.SaveScheduleAsync(_actor, setup.Input);
            (await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime)).Errors.Should().Be(0);
            (await _store.ListAsync<ChecklistOccurrence>(77)).Count(o => o.PeriodStartUtc == first.PeriodStartUtc).Should().Be(1);
        }
        internal sealed partial class MemoryStore
        {
            public List<DateTime> WorkshiftDays { get; } = new List<DateTime>();
            private IEnumerable<T> All<T>() where T : ChecklistRow => _rows.Where(r => r.Key.Item1 == typeof(T)).Select(r => JsonConvert.DeserializeObject<T>(r.Value));
            public Task<List<int>> SchedulingDepartmentsAsync(int afterDepartmentId, CancellationToken ct = default) => Task.FromResult(All<ChecklistSchedule>().Where(s => s.IsActive && s.DepartmentId > afterDepartmentId).Select(s => s.DepartmentId).Distinct().OrderBy(d => d).Take(100).ToList());
            public Task<List<ChecklistSchedule>> ActiveSchedulesAsync(int departmentId, string afterId, CancellationToken ct = default) => Task.FromResult(All<ChecklistSchedule>().Where(s => s.IsActive && s.DepartmentId == departmentId && string.CompareOrdinal(s.Id, afterId) > 0).OrderBy(s => s.Id, StringComparer.Ordinal).Take(100).ToList());
            public Task<List<ChecklistOccurrence>> ScheduledOccurrencesAsync(int departmentId, string scheduleId, DateTime fromUtc, DateTime untilUtc, CancellationToken ct = default) => Task.FromResult(All<ChecklistOccurrence>().Where(s => s.DepartmentId == departmentId && s.ScheduleId == scheduleId && new[] { 0, 1, 3 }.Contains(s.State) && s.WindowEndUtc >= fromUtc && s.WindowEndUtc < untilUtc).OrderBy(s => s.PeriodStartUtc).Take(500).ToList());
            public Task<List<ChecklistOccurrence>> DueOccurrencesAsync(int departmentId, DateTime untilUtc, int skip, CancellationToken ct = default) => Task.FromResult(All<ChecklistOccurrence>().Where(s => s.DepartmentId == departmentId && s.ScheduleId != null && new[] { 0, 1, 3, 4 }.Contains(s.State) && s.PeriodStartUtc <= untilUtc).OrderBy(s => s.PeriodStartUtc).Skip(skip).Take(50).ToList());
            public async Task CancelUnstartedOccurrencesAsync(int departmentId, string scheduleId, DateTime? fromUtc, DateTime nowUtc, CancellationToken ct = default)
            {
                foreach (var row in All<ChecklistOccurrence>().Where(s => s.DepartmentId == departmentId && s.ScheduleId == scheduleId && s.State == 3 && (!fromUtc.HasValue || s.PeriodStartUtc >= fromUtc)).ToArray())
                { row.State = 6; row.Revision++; row.UpdatedOn = nowUtc; await WriteAsync(row, false, ct); }
            }
            public Task<List<ChecklistOccurrence>> OccurrencesInWindowAsync(int departmentId, string scheduleId, DateTime fromUtc, DateTime untilUtc, CancellationToken ct = default) => Task.FromResult(All<ChecklistOccurrence>().Where(o => o.DepartmentId == departmentId && o.ScheduleId == scheduleId && o.PeriodStartUtc >= fromUtc && o.PeriodStartUtc < untilUtc).ToList());
            public Task<bool> WorkshiftExistsAsync(int departmentId, string workshiftId, CancellationToken ct = default) => Task.FromResult(departmentId == 77);
            public Task<List<DateTime>> WorkshiftStartsAsync(int departmentId, string workshiftId, DateTime fromUtc, DateTime untilUtc, CancellationToken ct = default) => Task.FromResult(WorkshiftDays.Where(d => departmentId == 77 && d >= fromUtc && d < untilUtc).ToList());
        }
    }
    [TestFixture]
    public class ChecklistRecurrenceTests
    {
        private static ChecklistSchedule Schedule(ChecklistScheduleFrequency frequency = ChecklistScheduleFrequency.Daily) => new ChecklistSchedule { Frequency = (int)frequency, TimeZoneId = "UTC", StartDate = new DateTime(2020, 1, 1), ClockMinutes = "480", Weekdays = 127, DayOfMonth = 31, MonthOfYear = 2 };
        [TestCase(2024, 2, 29)] [TestCase(2026, 2, 28)] [TestCase(2026, 4, 30)]
        public void Monthly_checks_use_the_last_day_of_short_months(int year, int month, int day)
        {
            var from = new DateTime(year, month, 25, 0, 0, 0, DateTimeKind.Utc);
            ChecklistRecurrence.Expand(Schedule(ChecklistScheduleFrequency.Monthly), from, from.AddDays(7)).Should().Equal(new DateTime(year, month, day, 8, 0, 0, DateTimeKind.Utc));
        }
        [TestCase(ChecklistScheduleFrequency.Quarterly, 5, 1)] [TestCase(ChecklistScheduleFrequency.Quarterly, 6, 0)]
        [TestCase(ChecklistScheduleFrequency.SemiAnnual, 8, 1)] [TestCase(ChecklistScheduleFrequency.SemiAnnual, 5, 0)]
        [TestCase(ChecklistScheduleFrequency.Annual, 2, 1)] [TestCase(ChecklistScheduleFrequency.Annual, 8, 0)]
        public void Longer_recurrences_use_the_anchor_month(ChecklistScheduleFrequency frequency, int month, int count)
        {
            var from = new DateTime(2026, month, 25, 0, 0, 0, DateTimeKind.Utc);
            ChecklistRecurrence.Expand(Schedule(frequency), from, from.AddDays(7)).Should().HaveCount(count);
        }
        [Test]
        public void DST_gap_moves_to_first_valid_minute_and_fold_uses_one_earlier_instant()
        {
            var schedule = Schedule(); schedule.TimeZoneId = "America/New_York"; schedule.ClockMinutes = "150,180";
            var spring = new DateTime(2026, 3, 8, 0, 0, 0, DateTimeKind.Utc);
            ChecklistRecurrence.Expand(schedule, spring, spring.AddDays(1)).Should().Equal(spring.AddHours(7));
            schedule.ClockMinutes = "90"; var fall = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
            ChecklistRecurrence.Expand(schedule, fall, fall.AddDays(1)).Should().Equal(fall.AddHours(5.5));
        }
        [Test]
        public void Weekly_dates_half_open_windows_and_shift_fallback_are_deterministic()
        {
            var schedule = Schedule(ChecklistScheduleFrequency.Weekly); schedule.Weekdays = 1 << (int)DayOfWeek.Tuesday;
            var from = new DateTime(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc);
            ChecklistRecurrence.Expand(schedule, from, from.AddDays(7)).Should().Equal(from);
            schedule.Frequency = (int)ChecklistScheduleFrequency.PerShift; schedule.ClockMinutes = "480,1200";
            ChecklistRecurrence.Expand(schedule, from, from.AddDays(1)).Should().Equal(from, from.AddHours(12));
            schedule.WorkshiftId = Guid.NewGuid().ToString();
            ChecklistRecurrence.Expand(schedule, from, from.AddDays(1)).Should().BeEmpty();
            ChecklistRecurrence.Expand(schedule, from, from.AddDays(1), new[] { from, from, from.AddHours(4), from.AddDays(1) }).Should().Equal(from, from.AddHours(4));
            schedule.EndDate = from.AddDays(-1); ChecklistRecurrence.Expand(schedule, from, from.AddDays(1), new[] { from }).Should().BeEmpty();
        }
        [TestCase("")] [TestCase("25:00")] [TestCase("8:00")] [TestCase("08:00,")] [TestCase("08:00,09:00,10:00,11:00,12:00,13:00,14:00,15:00,16:00")]
        public void Invalid_clock_lists_are_rejected(string value) => ((Action)(() => ChecklistRecurrence.ParseTimes(value))).Should().Throw<ChecklistException>();
    }
}
