using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(193)]
	public class M0193_ReadinessHistoryProtection : Migration
	{
		private static string N(string value) => value;
		private static string Q(string value) => "[" + N(value) + "]";
		public override void Up()
		{
			if (!Schema.Table(N("DomainEventOutbox")).Column(N("ReadinessRoutingJson")).Exists())
				Alter.Table(N("DomainEventOutbox")).AddColumn(N("ReadinessRoutingJson")).AsString(int.MaxValue).Nullable();
			foreach (var field in new[] { ("WorkflowRuns", "ErrorMessage"), ("WorkflowRunLogs", "ErrorMessage"), ("WorkflowRunLogs", "ActionResult") })
				Alter.Column(N(field.Item2)).OnTable(N(field.Item1)).AsString(int.MaxValue).Nullable();
			Execute.WithConnection((connection, transaction) =>
			{
				long cursor = 0;
				while (true)
				{
					var rows = new List<(long Id, string Payload, string Aggregate)>();
					using (var select = connection.CreateCommand())
					{
						select.Transaction = transaction;
						select.CommandText = $"SELECT TOP (200) {Q("DomainEventOutboxId")}, {Q("PayloadJson")}, {Q("AggregateId")} FROM {Q("DomainEventOutbox")} WHERE {Q("ProducerSubsystem")} = 'Checklists' AND {Q("ReadinessRoutingJson")} IS NULL AND {Q("DomainEventOutboxId")} > @cursor ORDER BY {Q("DomainEventOutboxId")}";
						var parameter = select.CreateParameter(); parameter.ParameterName = "@cursor"; parameter.Value = cursor; select.Parameters.Add(parameter);
						using var reader = select.ExecuteReader();
						while (rows.Count < 200 && reader.Read()) rows.Add((Convert.ToInt64(reader[0]), Convert.ToString(reader[1]), Convert.ToString(reader[2])));
					}
					if (rows.Count == 0) break;
					foreach (var row in rows)
					{
						// The historical payload stays intact. Only reviewed structural facts enter the routing copy.
						var safe = Routing(row.Payload, row.Aggregate);
						using var update = connection.CreateCommand(); update.Transaction = transaction;
						update.CommandText = $"UPDATE {Q("DomainEventOutbox")} SET {Q("ReadinessRoutingJson")} = @safe WHERE {Q("DomainEventOutboxId")} = @id";
						foreach (var value in new[] { ("@safe", (object)safe), ("@id", (object)row.Id) }) { var parameter = update.CreateParameter(); parameter.ParameterName = value.Item1; parameter.Value = value.Item2; update.Parameters.Add(parameter); }
						update.ExecuteNonQuery(); cursor = row.Id;
					}
				}
			});
		}
		private static string Routing(string json, string aggregate)
		{
			try
			{
				using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); var root = document.RootElement;
				var safe = new Dictionary<string, object>();
				foreach (var name in new[] { "CompletionId", "DefinitionId", "VersionId", "ItemId" })
					if (root.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String && Guid.TryParse(field.GetString(), out var id)) safe[name] = id.ToString("D");
				if (!safe.ContainsKey("CompletionId") && Guid.TryParse(aggregate, out var completion)) safe["CompletionId"] = completion.ToString("D");
				var type = root.TryGetProperty("TargetType", out var targetType) && targetType.TryGetInt32(out var parsed) ? parsed : -1;
				if (type >= 0) safe["TargetType"] = type;
				var redacted = new List<string> { "Score", "Passed" };
				if (root.TryGetProperty("TargetId", out var target) && target.ValueKind == JsonValueKind.String)
				{
					var text = target.GetString();
					var structural = type >= 0 && type <= 2 && int.TryParse(text, out var numeric) && numeric > 0 || type == 5 && Guid.TryParse(text, out _);
					safe["TargetId"] = structural ? text : "REDACTED";
					if (!structural) redacted.Add("TargetId");
				}
				safe["Score"] = "REDACTED"; safe["Passed"] = "REDACTED"; safe["is_redacted"] = true; safe["redacted_fields"] = redacted; safe["catalog_version"] = 16;
				return JsonSerializer.Serialize(safe);
			}
			catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException)
			{ throw new InvalidOperationException("A historical checklist event requires repair before its safe routing payload can be created."); }
		}
		public override void Down()
		{
			Execute.WithConnection((connection, transaction) =>
			{
				foreach (var field in new[] { ("AuditLogs", "Data"), ("DomainEventOutbox", "PayloadJson"), ("DomainEventOutbox", "LastError"), ("WorkflowRuns", "InputPayload"), ("WorkflowRuns", "ErrorMessage"), ("WorkflowRunLogs", "RenderedOutput"), ("WorkflowRunLogs", "ErrorMessage"), ("WorkflowRunLogs", "ActionResult") })
				{
					using var command = connection.CreateCommand(); command.Transaction = transaction;
					command.CommandText = $"SELECT COUNT(*) FROM {Q(field.Item1)} WHERE {Q(field.Item2)} LIKE 'rgdp:%'";
					if (Convert.ToInt64(command.ExecuteScalar()) > 0) throw new InvalidOperationException("Complete ADP offboarding before rolling back readiness history protection.");
				}
			});
			// Keep widened columns: narrowing may truncate legitimate historical content.
			Delete.Column(N("ReadinessRoutingJson")).FromTable(N("DomainEventOutbox"));
		}
	}
}
