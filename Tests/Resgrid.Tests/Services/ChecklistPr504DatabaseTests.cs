using System;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.Services
{
	public partial class ChecklistDatabaseTests
	{
		[Test, Order(1)]
		public async Task Reminder_rollback_preserves_unrelated_settings_and_refuses_reminder_configuration()
		{
			var runner = _runner.GetRequiredService<IMigrationRunner>();
			var connections = Connections(); using var uow = new UnitOfWork(connections); var store = Repository(connections, uow);
			var settings = Row<DepartmentChecklistSettings>(); settings.Content = "UNRELATED-SETTINGS";
			await uow.CreateOrGetConnectionAsync();
			await store.WriteAsync(settings, true); uow.CommitChanges();
			runner.MigrateDown(194);
			await using var db = Connect(_connection);
			(await db.QuerySingleAsync<string>($"SELECT {Q("Content")} FROM {Q("DepartmentChecklistSettings")} WHERE {Q("Id")}=@Id", new { settings.Id })).Should().Be("UNRELATED-SETTINGS");
			runner.MigrateUp();
			await db.ExecuteAsync($"UPDATE {Q("DepartmentChecklistSettings")} SET {Q("RemindersActiveFromUtc")}=@Active WHERE {Q("Id")}=@Id", new { Active = new DateTime(2026, 9, 8), settings.Id });
			Action rollback = () => runner.MigrateDown(194);
			rollback.Should().Throw<InvalidOperationException>().WithMessage("*populated reminder storage*");
			await db.ExecuteAsync($"DELETE FROM {Q("DepartmentChecklistSettings")} WHERE {Q("Id")}=@Id", new { settings.Id });
			_runner.GetRequiredService<IVersionLoader>().LoadVersionInfo(); runner.MigrateUp();
		}

		[Test, Order(190)]
		public async Task Access_fence_flag_telemetry_and_workflow_claims_use_the_configured_schema()
		{
			var schema = "review_" + Guid.NewGuid().ToString("N");
			await using var db = Connect(_connection);
			await db.ExecuteAsync($"CREATE SCHEMA {schema}");
			var timestamp = _type == DatabaseTypes.Postgres ? "timestamp" : "datetime2";
			await db.ExecuteAsync($"CREATE TABLE {schema}.{Q("ChecklistAccessFence")} ({Q("Id")} int PRIMARY KEY); INSERT INTO {schema}.{Q("ChecklistAccessFence")} VALUES (1)");
			await db.ExecuteAsync($"CREATE TABLE {schema}.{Q("FeatureFlags")} ({Q("FeatureFlagId")} int PRIMARY KEY, {Q("LastEvaluatedOn")} {timestamp}); INSERT INTO {schema}.{Q("FeatureFlags")} VALUES (1,NULL)");
			await db.ExecuteAsync($"CREATE TABLE {schema}.{Q("WorkflowRuns")} ({Q("WorkflowRunId")} varchar(36) PRIMARY KEY, {Q("WorkflowId")} varchar(36), {Q("DepartmentId")} int, {Q("TriggerEventType")} int, {Q("Status")} int, {Q("AttemptNumber")} int, {Q("InputPayload")} {TextType}, {Q("EventId")} varchar(36), {Q("StartedOn")} {timestamp})");
			var connections = Connections(); using var unit = new UnitOfWork(connections); var config = Configuration(); config.SchemaName = schema;
			var queries = Mock.Of<IQueryFactory>(); var store = new ChecklistRepository(connections, config, unit, queries);
			await unit.CreateOrGetConnectionAsync(); await store.LockAccessFenceAsync(); unit.CommitChanges();
			var flags = new FeatureFlagRepository(connections, config, unit, queries); var now = new DateTime(2026, 9, 8);
			await flags.TouchEvaluationAsync(1, now);
			(await db.QuerySingleAsync<DateTime>($"SELECT {Q("LastEvaluatedOn")} FROM {schema}.{Q("FeatureFlags")}")).Should().Be(now);
			await flags.TouchEvaluationAsync(1, now.AddDays(-1));
			(await db.QuerySingleAsync<DateTime>($"SELECT {Q("LastEvaluatedOn")} FROM {schema}.{Q("FeatureFlags")}")).Should().Be(now);
			var runs = new WorkflowRunRepository(connections, config, unit, queries); var workflowId = Guid.NewGuid().ToString(); var eventId = Guid.NewGuid().ToString();
			foreach (var trigger in ChecklistWorkflowPayload.Triggers)
			{
				var id = Guid.NewGuid().ToString();
				await db.ExecuteAsync($"INSERT INTO {schema}.{Q("WorkflowRuns")} VALUES (@Id,@WorkflowId,77,@Trigger,@Status,0,NULL,@EventId,@StartedOn)", new { Id = id, WorkflowId = workflowId, Trigger = trigger, Status = (int)WorkflowRunStatus.Pending, EventId = eventId, StartedOn = now });
				(await runs.TryStartChecklistRunAsync(id, workflowId, 88, 1, "{}")).Should().BeFalse();
				(await runs.TryStartChecklistRunAsync(id, workflowId, 77, 1, "{}")).Should().BeTrue();
				(await runs.TryStartChecklistRunAsync(id, workflowId, 77, 1, "{}")).Should().BeFalse();
			}
			(await runs.GetByWorkflowsAndEventAsync(77, new[] { workflowId }, eventId)).Select(r => r.TriggerEventType).Should().BeEquivalentTo(ChecklistWorkflowPayload.Triggers);
			(await runs.GetByWorkflowsAndEventAsync(88, new[] { workflowId }, eventId)).Should().BeEmpty();
			(await runs.GetByWorkflowsAndEventAsync(77, new[] { workflowId }, Guid.NewGuid().ToString())).Should().BeEmpty();
		}
	}
}
