using System;
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
		[Test, Order(120)]
		public async Task Assignment_digest_and_access_metadata_survive_transactions_without_rewriting_protected_content()
		{
			_runner.GetRequiredService<IVersionLoader>().LoadVersionInfo(); var runner = _runner.GetRequiredService<IMigrationRunner>(); runner.MigrateUp();
			var connections = Connections(); using var uow = new UnitOfWork(connections); var store = Repository(connections, uow); var now = new DateTime(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);
			var schedule = (await store.ActiveSchedulesAsync(77, "")).First();
			await uow.CreateOrGetConnectionAsync(); await store.LockAccessFenceAsync(); await store.LockDepartmentAsync(77);
			schedule.AssignmentType = 1; schedule.AssignmentId = "author"; schedule.Content = "SYNTHETIC-BOUND-CIPHERTEXT"; await store.WriteAsync(schedule, false);
			var settings = (await store.ListAsync<DepartmentChecklistSettings>(77)).Single(); settings.FixedDigestMinute = 90; settings.DigestActiveFromUtc = now; await store.WriteAsync(settings, false);
			await store.AdvanceDigestSweepAsync(77, now); await store.ApplyAccessStateAsync(77, false, now); uow.CommitChanges();
			(await store.GetAsync<ChecklistSchedule>(77, schedule.Id)).IsSuspended.Should().BeTrue();
			await uow.CreateOrGetConnectionAsync(); await store.LockAccessFenceAsync(); await store.LockDepartmentAsync(77); await store.ApplyAccessStateAsync(77, true, now.AddDays(1)); uow.DiscardChanges();
			(await store.GetAsync<ChecklistSchedule>(77, schedule.Id)).IsSuspended.Should().BeTrue("a failed policy transaction rolls back resumption");
			await uow.CreateOrGetConnectionAsync(); await store.LockAccessFenceAsync(); await store.LockDepartmentAsync(77); await store.ApplyAccessStateAsync(77, true, now.AddDays(1)); uow.CommitChanges();
			var resumed = await store.GetAsync<ChecklistSchedule>(77, schedule.Id); resumed.Content.Should().Be(schedule.Content); resumed.AssignmentId.Should().Be("author"); resumed.ActiveFromUtc.Should().Be(now.AddDays(1));
			(await store.ListAsync<DepartmentChecklistSettings>(77)).Single().LastDigestSweepUtc.Should().Be(now);
			var occurrence = (await store.ListAsync<ChecklistOccurrence>(77)).First();
			var queue = new ChecklistReminderRepository(connections, Configuration(), uow, Mock.Of<IQueryFactory>());
			await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
			foreach (var key in new[] { "fixed:20260908", "fixed:20260908", "fixed:20260909" }) await queue.EnqueueAsync(new ChecklistReminder { DepartmentId = 77, OccurrenceId = occurrence.Id, RecipientUserId = "timed-recipient", Kind = 4, PeriodKey = key, CreatedOnUtc = now, NextAttemptUtc = now });
			uow.CommitChanges(); (await queue.ForRecipientAsync(77, "timed-recipient", 0)).Should().HaveCount(2);
			Action down = () => runner.MigrateDown(195); down.Should().Throw<Exception>().WithMessage("*preserved*");
		}
		[Test, Order(121)]
		public async Task Shift_digest_query_enforces_department_unit_ownership_deleted_shifts_and_trigger_bounds()
		{
			await using var db = Connect(_connection); var dateType = _type == Resgrid.Config.DatabaseTypes.Postgres ? "timestamp" : "datetime2";
			await db.ExecuteAsync($"ALTER TABLE {Q("Workshifts")} ADD {Q("Type")} int; ALTER TABLE {Q("WorkshiftDays")} ADD {Q("WorkshiftDayId")} varchar(36); CREATE TABLE {Q("WorkshiftEntities")} ({Q("WorkshiftId")} varchar(36),{Q("BackingId")} varchar(128)); CREATE TABLE {Q("Units")} ({Q("UnitId")} int PRIMARY KEY,{Q("DepartmentId")} int);");
			var id = Guid.NewGuid().ToString(); var day = Guid.NewGuid().ToString(); var from = new DateTime(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc); var until = from.AddHours(1);
			await db.ExecuteAsync($"INSERT INTO {Q("Workshifts")} ({Q("WorkshiftId")},{Q("DepartmentId")},{Q("Type")}) VALUES(@id,77,1); INSERT INTO {Q("Units")} VALUES(12,77),(13,88); INSERT INTO {Q("WorkshiftEntities")} VALUES(@id,'12'),(@id,'13'); INSERT INTO {Q("WorkshiftDays")} ({Q("WorkshiftId")},{Q("Day")},{Q("WorkshiftDayId")}) VALUES(@id,@from,@other),(@id,@until,@day);", new { id, day, from, until, other = Guid.NewGuid().ToString() });
			var connections = Connections(); using var uow = new UnitOfWork(connections); var store = Repository(connections, uow);
			(await store.ShiftStartsAsync(77, from, until)).Should().ContainSingle(s => s.UnitId == 12 && s.WorkshiftDayId == day && s.StartUtc == until);
			(await store.ShiftStartsAsync(88, from, until)).Should().BeEmpty();
			await db.ExecuteAsync($"UPDATE {Q("Workshifts")} SET {Q("DeletedOn")}=@until WHERE {Q("WorkshiftId")}=@id", new { until, id });
			(await store.ShiftStartsAsync(77, from, until)).Should().BeEmpty();
		}
		[Test, Order(125)]
		public async Task Restored_schedule_and_evidence_keep_their_authenticated_row_and_department_binding()
		{
			var crypto = new Resgrid.Services.ProtectedFieldCryptoService(); var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
			try
			{
				var connections = Connections(); using var uow = new UnitOfWork(connections); var store = Repository(connections, uow);
				var schedule = (await store.ActiveSchedulesAsync(77, "")).First();
				await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
				schedule.Content = crypto.EncryptText(key, 1, "SYNTHETIC-PRIVATE-SCHEDULE", 77, "checklistschedules.content", schedule.Id); schedule.IsProtected = true; await store.WriteAsync(schedule, false);
				var occurrence = Row<ChecklistOccurrence>(schedule.ParentId); occurrence.VersionId = schedule.VersionId; occurrence.TargetId = "77"; occurrence.CompletionId = Guid.NewGuid().ToString(); await store.WriteAsync(occurrence, true);
				var completion = Row<ChecklistCompletion>(schedule.ParentId); completion.Id = occurrence.CompletionId; completion.OccurrenceId = occurrence.Id; completion.VersionId = schedule.VersionId; completion.TargetId = "77"; await store.WriteAsync(completion, true);
				var evidence = Row<ChecklistCompletionFile>(completion.Id); evidence.ItemId = Guid.NewGuid().ToString(); evidence.ContentType = "image/png"; evidence.Size = 4; evidence.ScanState = 1; evidence.IsProtected = true;
				evidence.Content = crypto.EncryptText(key, 1, "SYNTHETIC-PRIVATE-FILENAME", 77, "checklistcompletionfiles.content", evidence.Id); evidence.Data = crypto.EncryptBinary(key, 1, new byte[] { 1, 2, 3, 4 }, 77, "checklistcompletionfiles.data", evidence.Id); evidence.Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3, 4 })); await store.WriteAsync(evidence, true); uow.CommitChanges();
				var restored = await store.GetAsync<ChecklistCompletionFile>(77, evidence.Id);
				await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77); await store.DeleteFileAsync(77, evidence.Id); await store.WriteAsync(restored, true); uow.CommitChanges();
				var after = await store.GetAsync<ChecklistCompletionFile>(77, evidence.Id); after.Data.Should().Equal(evidence.Data); after.Content.Should().Be(evidence.Content);
				crypto.DecryptBinary(key, after.Data, 77, "checklistcompletionfiles.data", after.Id).Should().Equal(1, 2, 3, 4);
				crypto.DecryptText(key, (await store.GetAsync<ChecklistSchedule>(77, schedule.Id)).Content, 77, "checklistschedules.content", schedule.Id).Should().Be("SYNTHETIC-PRIVATE-SCHEDULE");
				Action foreign = () => crypto.DecryptBinary(key, after.Data, 88, "checklistcompletionfiles.data", after.Id); foreign.Should().Throw<System.Security.Cryptography.CryptographicException>();
				Action moved = () => crypto.DecryptText(key, schedule.Content, 77, "checklistschedules.content", Guid.NewGuid().ToString()); moved.Should().Throw<System.Security.Cryptography.CryptographicException>();
			}
			finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(key); }
		}
		[Test, Order(130)]
		public async Task Department_cleanup_preserves_holds_rolls_back_with_its_caller_and_erases_the_readiness_subtree_only()
		{
			await using var db = Connect(_connection); await db.OpenAsync();
			var date = _type == Resgrid.Config.DatabaseTypes.Postgres ? "timestamp" : "datetime2";
			await db.ExecuteAsync($"CREATE TABLE {Q("RmsRecordLegalHolds")} ({Q("DepartmentId")} int,{Q("ReleasedOn")} {date}); INSERT INTO {Q("RmsRecordLegalHolds")} VALUES(77,NULL);");
			var foreign = Row<ChecklistDefinition>(); foreign.DepartmentId = 88;
			var connections = Connections(); using var uow = new UnitOfWork(connections); var store = Repository(connections, uow);
			await uow.CreateOrGetConnectionAsync(); await store.WriteAsync(foreign, true); uow.CommitChanges();
			var run = Guid.NewGuid().ToString(); var foreignRun = Guid.NewGuid().ToString(); var log = Guid.NewGuid().ToString(); var foreignLog = Guid.NewGuid().ToString();
			await db.ExecuteAsync($"INSERT INTO {Q("WorkflowRuns")} ({Q("WorkflowRunId")},{Q("DepartmentId")},{Q("TriggerEventType")},{Q("InputPayload")}) VALUES(@run,77,67,'SYNTHETIC-PROTECTED-HISTORY'),(@foreignRun,88,67,'SYNTHETIC-FOREIGN-HISTORY'); INSERT INTO {Q("WorkflowRunLogs")} ({Q("WorkflowRunLogId")},{Q("WorkflowRunId")},{Q("RenderedOutput")}) VALUES(@log,@run,'SYNTHETIC-LOG'),(@foreignLog,@foreignRun,'SYNTHETIC-FOREIGN-LOG');", new { run, foreignRun, log, foreignLog });
			var count = await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("ChecklistDefinitions")} WHERE {Q("DepartmentId")}=77"); count.Should().BePositive();
			await using (var held = await db.BeginTransactionAsync()) { Func<Task> erase = () => ChecklistDepartmentCleanup.DeleteWithinTransactionAsync(db, held, 77, _type); await erase.Should().ThrowAsync<InvalidOperationException>().WithMessage("*legal hold*"); await held.RollbackAsync(); }
			(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("ChecklistDefinitions")} WHERE {Q("DepartmentId")}=77")).Should().Be(count);
			await db.ExecuteAsync($"UPDATE {Q("RmsRecordLegalHolds")} SET {Q("ReleasedOn")}=@now WHERE {Q("DepartmentId")}=77", new { now = DateTime.UtcNow });
			await using (var rollback = await db.BeginTransactionAsync()) { await ChecklistDepartmentCleanup.DeleteWithinTransactionAsync(db, rollback, 77, _type); await rollback.RollbackAsync(); }
			(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("ChecklistDefinitions")} WHERE {Q("DepartmentId")}=77")).Should().Be(count);
			await using (var commit = await db.BeginTransactionAsync()) { await ChecklistDepartmentCleanup.DeleteWithinTransactionAsync(db, commit, 77, _type); await commit.CommitAsync(); }
			foreach (var table in new[] { "ChecklistReminders", "ChecklistCompletionFiles", "ChecklistCompletionItems", "ChecklistCompletions", "ChecklistOccurrences", "ChecklistSchedules", "ChecklistDefinitionVersions", "ChecklistDefinitions", "DepartmentChecklistSettings" })
				(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q(table)} WHERE {Q("DepartmentId")}=77")).Should().Be(0, table);
			(await store.GetAsync<ChecklistDefinition>(88, foreign.Id)).Should().NotBeNull();
			(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("WorkflowRunLogs")} WHERE {Q("WorkflowRunId")}=@run", new { run })).Should().Be(0);
			(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("WorkflowRunLogs")} WHERE {Q("WorkflowRunId")}=@foreignRun", new { foreignRun })).Should().Be(1);
		}
	}
}
