using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// ADP catalog review §8, option (a), 2026-10-17: the checklist, work-order and inventory tables that pack operational
	/// metadata together with free text about people get a <c>ProtectedContent</c> column. It holds only the private
	/// members and is the row's ADP slot; <c>Content</c> keeps the plaintext metadata. Existing rows are split here with a
	/// frozen copy of the rules (<c>SplitContent</c> and the module <c>PrivateMembers</c> maps on 2026-10-17), so an
	/// enrollment sweep never leaves a private member behind in plaintext Content. No department in any environment had
	/// enrolled, so every Content is plaintext; a sealed Content is left untouched and one that is not a JSON object moves whole (fail closed). Batches walk the Id in
	/// order, each its own autocommitted statement (TransactionBehavior.None), and only rows still unsplit are touched, so
	/// a re-run resumes. Down merges the private members back and refuses if any of them is sealed. Next physical number
	/// at authoring (disk was at M0273).
	/// </summary>
	[Migration(274, TransactionBehavior.None)]
	public class M0274_SplitModuleProtectedContent : Migration
	{
		private const int BatchSize = 500;

		public override void Up()
		{
			foreach (var table in Rules.Keys)
				// Checked when the statement runs, not when the migration is built: a fresh database creates the tables in this same batch.
				Execute.Sql($"IF COL_LENGTH('{table}', 'Content') IS NOT NULL AND COL_LENGTH('{table}', 'ProtectedContent') IS NULL ALTER TABLE [{table}] ADD [ProtectedContent] nvarchar(max) NULL;");

			Execute.WithConnection((connection, transaction) =>
			{
				foreach (var rule in Rules)
					Split(connection, transaction, rule.Key, rule.Value);
			});
		}

		public override void Down()
		{
			Execute.WithConnection((connection, transaction) =>
			{
				// Every table is checked before any is merged: batches autocommit, so a refusal on a later table would
				// otherwise leave the earlier ones merged into plaintext Content with ProtectedContent still in place.
				foreach (var table in Rules.Keys)
					EnsureNoSealed(connection, transaction, table);
				foreach (var table in Rules.Keys)
					MergeBack(connection, transaction, table);
			});

			foreach (var table in Rules.Keys)
				Execute.Sql($"IF COL_LENGTH('{table}', 'ProtectedContent') IS NOT NULL ALTER TABLE [{table}] DROP COLUMN [ProtectedContent];");
		}

		/// <summary>Frozen 2026-10-17. Paths as SplitContent reads them: "Note", "Snapshot.Description", "Lines[].Note".</summary>
		public static readonly IReadOnlyDictionary<string, string[]> Rules = new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			["ChecklistOccurrences"] = new[] { "Reason" },
			["ChecklistCompletions"] = new[] { "Note", "WitnessAttestation" },
			["ChecklistCompletionItems"] = new[] { "Value", "Note", "NotApplicableReason" },
			["ChecklistSchedules"] = new[] { "Notes" },
			["WorkOrders"] = new[] { "Fields.Description", "Fields.Resolution", "Fields.Cause", "Fields.VerificationEvidence", "Fields.QualifiedPersonnel", "Fields.VendorDetails", "Fields.Approval.Reason" },
			["WorkOrderActivities"] = new[] { "Note", "Snapshot.Description", "Snapshot.Resolution", "Snapshot.Cause", "Snapshot.VerificationEvidence", "Snapshot.QualifiedPersonnel", "Snapshot.VendorDetails", "Snapshot.Approval.Reason" },
			["WorkOrderLabors"] = new[] { "RatePerHour", "Note" },
			["WorkOrderParts"] = new[] { "VoidReason" },
			["WorkOrderVendorCharges"] = new[] { "Description" },
			["WorkOrderPartMovements"] = new[] { "Reason" },
			["WorkOrderRecurrenceVersions"] = new[] { "Reason", "Template.Content.Description", "Template.Content.Resolution", "Template.Content.Cause", "Template.Content.VerificationEvidence", "Template.Content.QualifiedPersonnel", "Template.Content.VendorDetails", "Template.Content.Approval.Reason" },
			["WorkOrderMeterReadings"] = new[] { "Note" },
			["WorkOrderRecurrenceChanges"] = new[] { "Note" },
			["InventoryTransactions"] = new[] { "Note", "Attestation", "LegacySource.Note" },
			["InventoryOperations"] = new[] { "Attestation", "PendingCommand.Lines[].Note", "PendingIssues[].Note", "PendingReturn.Note" },
			["InventoryIssuances"] = new[] { "Note" },
			["InventoryPurchaseOrders"] = new[] { "Note" },
			["InventoryPurchaseOrderItems"] = new[] { "Note" },
			["InventoryCounts"] = new[] { "Note" },
		};

		private static bool HasColumn(IDbConnection connection, IDbTransaction transaction, string table)
		{
			using var exists = connection.CreateCommand();
			exists.Transaction = transaction;
			exists.CommandText = $"SELECT CASE WHEN COL_LENGTH('{table}', 'Content') IS NOT NULL AND COL_LENGTH('{table}', 'ProtectedContent') IS NOT NULL THEN 1 ELSE 0 END";
			return Convert.ToInt32(exists.ExecuteScalar()) == 1;
		}

		private static void Split(IDbConnection connection, IDbTransaction transaction, string table, string[] paths)
		{
			if (!HasColumn(connection, transaction, table))
				return;
			var after = "";
			while (true)
			{
				var batch = new List<(string Id, string Content)>();
				using (var select = connection.CreateCommand())
				{
					select.Transaction = transaction;
					select.CommandTimeout = 300;
					select.CommandText = $"SELECT TOP ({BatchSize}) [Id], [Content] FROM [{table}] WHERE [Id] > @after AND [ProtectedContent] IS NULL AND [Content] IS NOT NULL AND [Content] NOT LIKE 'rgdp%' ORDER BY [Id]";
					AddParameter(select, "@after", after);
					using var reader = select.ExecuteReader();
					while (reader.Read())
						batch.Add((reader.GetString(0), reader.GetString(1)));
				}
				if (batch.Count == 0)
					return;

				foreach (var (id, content) in batch)
					SplitRow(connection, transaction, table, paths, id, content);
				after = batch[batch.Count - 1].Id;
			}
		}

		private const int RowAttempts = 5;

		/// <summary>
		/// Splits one row. The UPDATE matches only the Content that was split, so a row an app instance rewrote in between is
		/// never overwritten with a split of the stale value: it is read again and its current Content split. Version 274
		/// runs once, so a row skipped here would stay unsplit for good; one that keeps changing fails the migration, which
		/// then resumes on the next run.
		/// </summary>
		public static void SplitRow(IDbConnection connection, IDbTransaction transaction, string table, string[] paths, string id, string content)
		{
			for (var attempt = 1; content != null; attempt++)
			{
				var (metadata, hidden) = SplitJson(content, paths);
				if (hidden == null)
					return;
				using (var update = connection.CreateCommand())
				{
					update.Transaction = transaction;
					update.CommandTimeout = 300;
					// Binary collation so the match is exact.
					update.CommandText = $"UPDATE [{table}] SET [Content] = @content, [ProtectedContent] = @hidden WHERE [Id] = @id AND [ProtectedContent] IS NULL AND [Content] COLLATE Latin1_General_BIN2 = @original";
					AddParameter(update, "@content", metadata);
					AddParameter(update, "@hidden", hidden);
					AddParameter(update, "@id", id);
					AddParameter(update, "@original", content);
					if (update.ExecuteNonQuery() > 0)
						return;
				}
				if (attempt >= RowAttempts)
					throw new InvalidOperationException($"{table} row {id} kept changing while it was being split; run the migration again.");
				// Gone, already split by the application, sealed or emptied: nothing is left to split.
				using var reread = connection.CreateCommand();
				reread.Transaction = transaction;
				reread.CommandTimeout = 300;
				reread.CommandText = $"SELECT [Content] FROM [{table}] WHERE [Id] = @id AND [ProtectedContent] IS NULL AND [Content] NOT LIKE 'rgdp%'";
				AddParameter(reread, "@id", id);
				content = reread.ExecuteScalar() as string;
			}
		}

		private static void EnsureNoSealed(IDbConnection connection, IDbTransaction transaction, string table)
		{
			if (!HasColumn(connection, transaction, table))
				return;
			using var sealedRows = connection.CreateCommand();
			sealedRows.Transaction = transaction;
			sealedRows.CommandTimeout = 300;
			sealedRows.CommandText = $"SELECT COUNT(*) FROM [{table}] WHERE [ProtectedContent] LIKE 'rgdp%'";
			if (Convert.ToInt64(sealedRows.ExecuteScalar()) > 0)
				throw new InvalidOperationException($"{table} holds sealed ProtectedContent; it cannot be merged back into Content.");
		}

		private static void MergeBack(IDbConnection connection, IDbTransaction transaction, string table)
		{
			if (!HasColumn(connection, transaction, table))
				return;
			var after = "";
			while (true)
			{
				var batch = new List<(string Id, string Content, string Hidden)>();
				using (var select = connection.CreateCommand())
				{
					select.Transaction = transaction;
					select.CommandTimeout = 300;
					select.CommandText = $"SELECT TOP ({BatchSize}) [Id], [Content], [ProtectedContent] FROM [{table}] WHERE [Id] > @after AND [ProtectedContent] IS NOT NULL ORDER BY [Id]";
					AddParameter(select, "@after", after);
					using var reader = select.ExecuteReader();
					while (reader.Read())
						batch.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2)));
				}
				if (batch.Count == 0)
					return;

				foreach (var (id, content, hidden) in batch)
				{
					using var update = connection.CreateCommand();
					update.Transaction = transaction;
					update.CommandTimeout = 300;
					update.CommandText = $"UPDATE [{table}] SET [Content] = @content, [ProtectedContent] = NULL WHERE [Id] = @id";
					AddParameter(update, "@content", MergeJson(content, hidden));
					AddParameter(update, "@id", id);
					update.ExecuteNonQuery();
				}
				after = batch[batch.Count - 1].Id;
			}
		}

		private static readonly JsonSerializerOptions Output = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

		public static (string Content, string Hidden) SplitJson(string content, string[] paths)
		{
			if (string.IsNullOrWhiteSpace(content) || content.StartsWith("rgdp:", StringComparison.Ordinal) || content.StartsWith("rgdpb:", StringComparison.Ordinal))
				return (content, null);
			// Content that is not a JSON object cannot be told apart, so all of it is private (fail closed, as at run time).
			if (!(Parse(content) is JsonObject document))
				return (null, content);
			var hidden = new JsonObject();
			foreach (var path in paths)
				Move(document, hidden, path.Split('.'), 0);
			return Prune(hidden) ? (document.ToJsonString(Output), hidden.ToJsonString(Output)) : (content, null);
		}

		public static string MergeJson(string content, string hidden)
		{
			if (string.IsNullOrEmpty(hidden))
				return content;
			if (!(Parse(hidden) is JsonObject members))
				return string.IsNullOrEmpty(content) ? hidden : content;
			var document = Parse(content) as JsonObject ?? new JsonObject();
			Fill(document, members);
			return document.ToJsonString(Output);
		}

		private static JsonNode Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json) || json.TrimStart()[0] != '{')
				return null;
			try { return JsonNode.Parse(json); }
			catch (JsonException) { return null; }
		}

		private static void Move(JsonObject source, JsonObject target, string[] segments, int index)
		{
			var segment = segments[index];
			var last = index == segments.Length - 1;
			if (segment.EndsWith("[]", StringComparison.Ordinal))
			{
				var name = segment.Substring(0, segment.Length - 2);
				if (last || !(source[name] is JsonArray items))
					return;
				if (!(target[name] is JsonArray copy))
				{
					copy = new JsonArray(items.Select(_ => (JsonNode)null).ToArray());
					target[name] = copy;
				}
				for (var i = 0; i < items.Count && i < copy.Count; i++)
				{
					if (!(items[i] is JsonObject element))
						continue;
					if (!(copy[i] is JsonObject slot))
					{
						slot = new JsonObject();
						copy[i] = slot;
					}
					Move(element, slot, segments, index + 1);
				}
				return;
			}

			if (last)
			{
				if (!source.TryGetPropertyValue(segment, out var value))
					return;
				source.Remove(segment);
				if (value != null)
					target[segment] = value;
				return;
			}

			if (!(source[segment] is JsonObject child))
				return;
			if (!(target[segment] is JsonObject childTarget))
			{
				childTarget = new JsonObject();
				target[segment] = childTarget;
			}
			Move(child, childTarget, segments, index + 1);
		}

		private static void Fill(JsonObject document, JsonObject members)
		{
			foreach (var (name, value) in members.ToList())
			{
				switch (value)
				{
					case JsonObject nested:
						if (document[name] is JsonObject existing)
							Fill(existing, nested);
						else if (!document.ContainsKey(name))
							document[name] = nested.DeepClone();
						break;
					case JsonArray hiddenItems:
						if (document[name] is JsonArray items)
						{
							for (var i = 0; i < hiddenItems.Count && i < items.Count; i++)
								if (hiddenItems[i] is JsonObject hiddenElement && items[i] is JsonObject element)
									Fill(element, hiddenElement);
						}
						else if (!document.ContainsKey(name))
							document[name] = hiddenItems.DeepClone();
						break;
					default:
						if (!document.ContainsKey(name))
							document[name] = value?.DeepClone();
						break;
				}
			}
		}

		private static bool Prune(JsonNode node)
		{
			switch (node)
			{
				case JsonObject value:
					foreach (var (name, child) in value.ToList())
						if (!Prune(child))
							value.Remove(name);
					return value.Count > 0;
				case JsonArray value:
					var any = false;
					for (var i = 0; i < value.Count; i++)
					{
						if (Prune(value[i]))
							any = true;
						else
							value[i] = null;
					}
					return any;
				default:
					return node != null;
			}
		}

		private static void AddParameter(IDbCommand command, string name, string value)
		{
			var parameter = command.CreateParameter();
			parameter.ParameterName = name;
			parameter.DbType = DbType.String;
			parameter.Value = (object)value ?? DBNull.Value;
			command.Parameters.Add(parameter);
		}
	}
}
