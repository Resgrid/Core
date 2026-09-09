using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Resgrid.Model.Checklists;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.Services
{
    public partial class ChecklistDatabaseTests
    {
        [Test, Order(100)]
        public async Task Scheduling_migration_enforces_unique_periods_and_tenant_references_and_preserves_history_on_rollback()
        {
            var runner = _runner.GetRequiredService<IMigrationRunner>(); runner.MigrateUp();
            var connections = Connections(); using var uow = new UnitOfWork(connections); var store = Repository(connections, uow);
            await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
            var definition = Row<ChecklistDefinition>(); await store.WriteAsync(definition, true);
            var version = Row<ChecklistDefinitionVersion>(definition.Id); version.Version = 1; await store.WriteAsync(version, true);
            var now = new DateTime(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc);
            var schedule = Row<ChecklistSchedule>(definition.Id); schedule.VersionId = version.Id; schedule.TargetId = "77"; schedule.Frequency = 2;
            schedule.TimeZoneId = "UTC"; schedule.ClockMinutes = "480"; schedule.StartDate = now.Date; schedule.DayOfMonth = 1; schedule.MonthOfYear = 1;
            schedule.Weekdays = 127; schedule.WindowMinutes = 60; schedule.IsActive = true; schedule.ActiveFromUtc = now; schedule.GeneratedThroughUtc = now.AddDays(7); schedule.LastSweepUtc = now;
            await store.WriteAsync(schedule, true);
            ChecklistOccurrence Occurrence(string scheduleId = null) { var row = Row<ChecklistOccurrence>(definition.Id); row.VersionId = version.Id; row.CompletionId = Guid.NewGuid().ToString(); row.TargetId = "77"; row.ScheduleId = scheduleId; row.ScheduleRevision = scheduleId == null ? null : 1; row.PeriodStartUtc = scheduleId == null ? null : now; row.WindowEndUtc = scheduleId == null ? null : now.AddHours(1); row.State = 3; return row; }
            var occurrence = Occurrence(schedule.Id); await store.WriteAsync(occurrence, true);
            await store.WriteAsync(Occurrence(), true); await store.WriteAsync(Occurrence(), true); // Multiple on-demand NULL periods are valid on both engines.
            uow.CommitChanges();
            (await store.SchedulingDepartmentsAsync(0)).Should().Contain(77);
            (await store.ActiveSchedulesAsync(77, "")).Should().ContainSingle(s => s.Id == schedule.Id);
            (await store.ActiveSchedulesAsync(88, "")).Should().BeEmpty();
            (await store.GetAsync<ChecklistSchedule>(88, schedule.Id)).Should().BeNull();
            (await store.ScheduledOccurrencesAsync(77, schedule.Id, now, now.AddHours(2))).Should().ContainSingle();
            (await store.ScheduledOccurrencesAsync(77, schedule.Id, now.AddHours(2), now.AddHours(3))).Should().BeEmpty();
            (await store.DueOccurrencesAsync(77, now, 0)).Should().ContainSingle(o => o.Id == occurrence.Id);
            (await store.OccurrencesInWindowAsync(77, schedule.Id, now, now.AddDays(1))).Should().ContainSingle();
            (await store.OccurrencesInWindowAsync(88, schedule.Id, now, now.AddDays(1))).Should().BeEmpty();
            await uow.CreateOrGetConnectionAsync();
            Func<Task> duplicate = () => store.WriteAsync(Occurrence(schedule.Id), true);
            await duplicate.Should().ThrowAsync<DbException>(); uow.DiscardChanges();
            await uow.CreateOrGetConnectionAsync();
            var crossTenant = Occurrence(schedule.Id); crossTenant.DepartmentId = 88;
            Func<Task> foreign = () => store.WriteAsync(crossTenant, true); await foreign.Should().ThrowAsync<DbException>(); uow.DiscardChanges();
            await uow.CreateOrGetConnectionAsync();
            await store.CancelUnstartedOccurrencesAsync(88, schedule.Id, null, now); // Cannot cancel another department's work.
            uow.CommitChanges(); (await store.GetAsync<ChecklistOccurrence>(77, occurrence.Id)).State.Should().Be(3);
            await uow.CreateOrGetConnectionAsync(); await store.CancelUnstartedOccurrencesAsync(77, schedule.Id, null, now); uow.CommitChanges();
            (await store.GetAsync<ChecklistOccurrence>(77, occurrence.Id)).State.Should().Be(6);
            Action down = () => runner.MigrateDown(193); down.Should().Throw<Exception>();
            _runner.GetRequiredService<IVersionLoader>().LoadVersionInfo(); runner.MigrateUp();
            (await store.GetAsync<ChecklistSchedule>(77, schedule.Id)).Content.Should().Be("{}");
            (await store.GetAsync<ChecklistOccurrence>(77, occurrence.Id)).ScheduleRevision.Should().Be(1);
        }
        [Test, Order(101)]
        public async Task Scheduling_reads_only_selected_department_workshift_starts_with_half_open_bounds()
        {
            await using var db = Connect(_connection);
            await db.ExecuteAsync($"CREATE TABLE {Q("Workshifts")} ({Q("WorkshiftId")} varchar(36) PRIMARY KEY,{Q("DepartmentId")} int,{Q("DeletedOn")} timestamp NULL); CREATE TABLE {Q("WorkshiftDays")} ({Q("WorkshiftId")} varchar(36),{Q("Day")} timestamp);".Replace("timestamp", _type == Resgrid.Config.DatabaseTypes.Postgres ? "timestamp" : "datetime2"));
            var id = Guid.NewGuid().ToString(); var from = new DateTime(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc);
            await db.ExecuteAsync($"INSERT INTO {Q("Workshifts")} VALUES(@id,77,NULL); INSERT INTO {Q("WorkshiftDays")} VALUES(@id,@from),(@id,@until)", new { id, from, until = from.AddDays(1) });
            var connections = Connections(); using var uow = new UnitOfWork(connections); var store = Repository(connections, uow);
            (await store.WorkshiftExistsAsync(77, id)).Should().BeTrue(); (await store.WorkshiftExistsAsync(88, id)).Should().BeFalse();
            (await store.WorkshiftStartsAsync(77, id, from, from.AddDays(1))).Should().Equal(from);
            (await store.WorkshiftStartsAsync(88, id, from, from.AddDays(1))).Should().BeEmpty();
            await db.ExecuteAsync($"UPDATE {Q("Workshifts")} SET {Q("DeletedOn")}=@from WHERE {Q("WorkshiftId")}=@id", new { from, id });
            (await store.WorkshiftStartsAsync(77, id, from, from.AddDays(1))).Should().BeEmpty();
        }
    }
}
