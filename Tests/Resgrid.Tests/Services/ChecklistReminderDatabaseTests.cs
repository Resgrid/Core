using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Checklists;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.Services
{
	public partial class ChecklistDatabaseTests
	{
		[Test, Order(110)]
		public async Task Reminder_queue_has_tenant_integrity_deduplication_leases_retry_and_guarded_rollback()
		{
			_runner.GetRequiredService<IVersionLoader>().LoadVersionInfo(); var runner = _runner.GetRequiredService<IMigrationRunner>(); runner.MigrateUp();
			var connections = Connections(); using var first = new UnitOfWork(connections); using var second = new UnitOfWork(connections);
			var store = Repository(connections, first);
			ChecklistReminderRepository Queue(UnitOfWork uow) => new ChecklistReminderRepository(connections, Configuration(), uow, new Mock<IQueryFactory>().Object);
			var queue = Queue(first); var now = new DateTime(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc);
			await first.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
			var definition = Row<ChecklistDefinition>(); await store.WriteAsync(definition, true);
			var version = Row<ChecklistDefinitionVersion>(definition.Id); version.Version = 1; await store.WriteAsync(version, true);
			var occurrence = Row<ChecklistOccurrence>(definition.Id); occurrence.VersionId = version.Id; occurrence.TargetId = "77"; occurrence.CompletionId = Guid.NewGuid().ToString(); await store.WriteAsync(occurrence, true);
			var settings = Row<DepartmentChecklistSettings>(); settings.RemindersEnabled = true; settings.RemindersActiveFromUtc = now; settings.EscalateAfterMinutes = 30; await store.WriteAsync(settings, true);
			ChecklistReminder Notice(string user = "author", int kind = 0) => new ChecklistReminder { DepartmentId = 77, OccurrenceId = occurrence.Id, RecipientUserId = user, Kind = kind, CreatedOnUtc = now, NextAttemptUtc = now };
			var reminder = Notice(); await queue.EnqueueAsync(reminder); await queue.EnqueueAsync(Notice()); await queue.EnqueueAsync(Notice(kind: 1)); await queue.EnqueueAsync(Notice("other")); first.CommitChanges();
			(await queue.DepartmentsAsync(0)).Should().Contain(77); (await queue.DepartmentsAsync(77)).Should().NotContain(77);
			(await store.ListAsync<DepartmentChecklistSettings>(77)).Single(s => s.Id == settings.Id).EscalateAfterMinutes.Should().Be(30);
			(await queue.ForRecipientAsync(77, "author", 0)).Should().HaveCount(2); (await queue.ForRecipientAsync(88, "author", 0)).Should().BeEmpty();
			await first.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(88);
			(await queue.ClaimAsync(88, now, true)).Should().BeEmpty();
			var crossTenant = Notice(); crossTenant.DepartmentId = 88;
			Func<Task> invalid = () => queue.EnqueueAsync(crossTenant); await invalid.Should().ThrowAsync<DbException>(); first.DiscardChanges();
			await first.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
			var claimed = await queue.ClaimAsync(77, now, true); first.CommitChanges();
			claimed.Select(r => r.RecipientUserId).Distinct().Should().ContainSingle(); claimed.Should().OnlyContain(r => r.Attempts == 1 && r.ClaimToken != null);
			await second.CreateOrGetConnectionAsync(); await Repository(connections, second).LockDepartmentAsync(77);
			var other = await Queue(second).ClaimAsync(77, now, true); second.CommitChanges();
			other.Select(r => r.Id).Should().NotIntersectWith(claimed.Select(r => r.Id));
			await second.CreateOrGetConnectionAsync(); await Repository(connections, second).LockDepartmentAsync(77);
			(await Queue(second).ClaimAsync(77, now.AddMinutes(29), true)).Should().BeEmpty();
			var recovered = await Queue(second).ClaimAsync(77, now.AddMinutes(31), true); second.CommitChanges();
			recovered.Should().NotBeEmpty().And.OnlyContain(r => r.Attempts == 2);
			var stale = claimed.Concat(other).Single(r => r.Id == recovered[0].Id);
			await first.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
			Func<Task> staleFinish = () => queue.FinishAsync(stale, ChecklistReminderStatus.HandedOff, now.AddMinutes(31)); await staleFinish.Should().ThrowAsync<InvalidOperationException>(); first.DiscardChanges();
			await second.CreateOrGetConnectionAsync(); await Repository(connections, second).LockDepartmentAsync(77);
			await Queue(second).FinishAsync(recovered[0], ChecklistReminderStatus.Pending, now.AddMinutes(31)); second.CommitChanges();
			var history = (await queue.ForRecipientAsync(77, recovered[0].RecipientUserId, 0)).Single(r => r.Id == recovered[0].Id);
			history.NextAttemptUtc.Should().Be(now.AddMinutes(36)); history.ClaimToken.Should().BeNull(); history.CompletedOnUtc.Should().BeNull();
			await first.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
			var retry = await queue.ClaimAsync(77, now.AddMinutes(36), false); first.CommitChanges();
			retry.Should().ContainSingle();
			await first.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
			await queue.FinishAsync(retry[0], ChecklistReminderStatus.HandedOff, now.AddMinutes(36)); first.CommitChanges();
			(await queue.ForRecipientAsync(77, retry[0].RecipientUserId, 0)).Single(r => r.Id == retry[0].Id).Status.Should().Be(1);
			Action rollback = () => runner.MigrateDown(194); rollback.Should().Throw<Exception>().WithMessage("*preserved*");
			await using var db = Connect(_connection); (await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("ChecklistReminders")} WHERE {Q("DepartmentId")}=77")).Should().Be(3);
		}
	}
}
