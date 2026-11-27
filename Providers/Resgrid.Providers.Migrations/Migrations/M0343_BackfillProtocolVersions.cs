using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Protocols P1, version 1.0 backfill (R-P2). For every protocol without versions:
	/// <list type="number">
	/// <item>its current text becomes a published version 1.0 (ContentHtml copied server side, so it is byte for byte the stored
	/// text; ContentHash is <c>ProtocolContentHash</c>: SHA-256 over the UTF-8 text, lower-case hex), and the protocol becomes
	/// Published with that version current;</item>
	/// <item>each P0 call snapshot (DispatchProtocolSnapshots) whose hash differs becomes a Legacy version 0.n, oldest first,
	/// visible only from the calls that cite it;</item>
	/// <item>its CallProtocols rows get ProtocolVersionId: the version with the row's content hash, or 1.0 for rows recorded
	/// before content hashes existed. A row whose hash matches no snapshot or version keeps a null id.</item>
	/// </list>
	/// The hash is computed in C# (SQL Server's HASHBYTES hashes UTF-16). One transaction; a second run finds every protocol
	/// versioned and only retries still-unmapped call rows, which changes nothing.
	/// </summary>
	[Migration(343)]
	public class M0343_BackfillProtocolVersions : Migration
	{
		internal const string FirstVersionSummary = "Version 1.0: the protocol's text when protocol versions were introduced.";
		internal const string LegacySummary = "Text a call was dispatched with before protocol versions were introduced.";

		public override void Up()
		{
			Execute.WithConnection((connection, transaction) =>
			{
				var protocols = new List<(int Id, string Text)>();
				using (var read = Command(connection, transaction,
					"SELECT p.[DispatchProtocolId], p.[ProtocolText] FROM [dbo].[DispatchProtocols] p " +
					"WHERE NOT EXISTS (SELECT 1 FROM [dbo].[ProtocolVersions] v WHERE v.[DispatchProtocolId] = p.[DispatchProtocolId]) ORDER BY p.[DispatchProtocolId]"))
				using (var reader = read.ExecuteReader())
				{
					while (reader.Read())
						protocols.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
				}

				foreach (var (id, text) in protocols)
				{
					var hash = Hash(text);
					int versionId;
					using (var insert = Command(connection, transaction, @"
INSERT INTO [dbo].[ProtocolVersions] ([DepartmentId], [DispatchProtocolId], [Major], [Minor], [Status], [EffectiveOn], [ContentJson], [ContentHtml], [ContentHash],
	[ChangeSummary], [RequiresAcknowledgment], [CreatedOn], [CreatedByUserId], [PublishedOn], [PublishedByUserId], [RowVersion])
OUTPUT INSERTED.[ProtocolVersionId]
SELECT p.[DepartmentId], p.[DispatchProtocolId], 1, 0, 3, COALESCE(p.[UpdatedOn], p.[CreatedOn]), @Json, p.[ProtocolText], @Hash,
	@Summary, 0, p.[CreatedOn], p.[CreatedByUserId], COALESCE(p.[UpdatedOn], p.[CreatedOn]), p.[UpdatedByUserId], 0
FROM [dbo].[DispatchProtocols] p WHERE p.[DispatchProtocolId] = @Id"))
					{
						Add(insert, "@Json", ContentJson(text));
						Add(insert, "@Hash", hash);
						Add(insert, "@Summary", FirstVersionSummary);
						Add(insert, "@Id", id);
						versionId = Convert.ToInt32(insert.ExecuteScalar());
					}

					using (var update = Command(connection, transaction,
						"UPDATE [dbo].[DispatchProtocols] SET [Status] = 1, [CurrentVersionId] = @VersionId WHERE [DispatchProtocolId] = @Id"))
					{
						Add(update, "@VersionId", versionId);
						Add(update, "@Id", id);
						update.ExecuteNonQuery();
					}

					// P0 snapshots of other text, oldest first, become legacy versions 0.1, 0.2...
					var snapshots = new List<(int SnapshotId, string Hash, string Text)>();
					using (var read = Command(connection, transaction,
						"SELECT [DispatchProtocolSnapshotId], [ContentHash], [ProtocolText] FROM [dbo].[DispatchProtocolSnapshots] WHERE [DispatchProtocolId] = @Id " +
						"ORDER BY [FirstUsedOn], [DispatchProtocolSnapshotId]"))
					{
						Add(read, "@Id", id);
						using var reader = read.ExecuteReader();
						while (reader.Read())
							snapshots.Add((reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
					}

					var minor = 0;
					foreach (var snapshot in snapshots)
					{
						if (string.Equals(snapshot.Hash, hash, StringComparison.Ordinal))
							continue;

						minor++;
						using var legacy = Command(connection, transaction, @"
INSERT INTO [dbo].[ProtocolVersions] ([DepartmentId], [DispatchProtocolId], [Major], [Minor], [Status], [EffectiveOn], [ContentJson], [ContentHtml], [ContentHash],
	[ChangeSummary], [RequiresAcknowledgment], [CreatedOn], [RowVersion])
SELECT s.[DepartmentId], s.[DispatchProtocolId], 0, @Minor, 6, s.[FirstUsedOn], @Json, s.[ProtocolText], s.[ContentHash], @Summary, 0, s.[FirstUsedOn], 0
FROM [dbo].[DispatchProtocolSnapshots] s WHERE s.[DispatchProtocolSnapshotId] = @SnapshotId");
						Add(legacy, "@Minor", minor);
						Add(legacy, "@Json", ContentJson(snapshot.Text));
						Add(legacy, "@Summary", LegacySummary);
						Add(legacy, "@SnapshotId", snapshot.SnapshotId);
						legacy.ExecuteNonQuery();
					}

					MapCallRows(connection, transaction, id, versionId);
				}

				// A second run (or rows written between runs) only retries the rows still without a version.
				using var unmapped = Command(connection, transaction,
					"SELECT DISTINCT [DispatchProtocolId] FROM [dbo].[CallProtocols] WHERE [ProtocolVersionId] IS NULL");
				var protocolIds = new List<int>();
				using (var reader = unmapped.ExecuteReader())
				{
					while (reader.Read())
						protocolIds.Add(reader.GetInt32(0));
				}

				foreach (var protocolId in protocolIds)
				{
					int? first = null;
					using (var find = Command(connection, transaction,
						"SELECT TOP 1 [ProtocolVersionId] FROM [dbo].[ProtocolVersions] WHERE [DispatchProtocolId] = @Id AND [Major] = 1 AND [Minor] = 0 AND [ChangeSummary] = @Summary ORDER BY [ProtocolVersionId]"))
					{
						Add(find, "@Id", protocolId);
						Add(find, "@Summary", FirstVersionSummary);
						var value = find.ExecuteScalar();
						if (value != null && value != DBNull.Value)
							first = Convert.ToInt32(value);
					}

					if (first.HasValue)
						MapCallRows(connection, transaction, protocolId, first.Value);
				}
			});
		}

		public override void Down()
		{
			var first = FirstVersionSummary.Replace("'", "''");
			var legacy = LegacySummary.Replace("'", "''");
			Execute.Sql("IF COL_LENGTH('dbo.CallProtocols', 'ProtocolVersionId') IS NOT NULL UPDATE [dbo].[CallProtocols] SET [ProtocolVersionId] = NULL;");
			Execute.Sql($@"
IF OBJECT_ID('dbo.ProtocolVersions', 'U') IS NOT NULL
BEGIN
	UPDATE [dbo].[DispatchProtocols] SET [Status] = 0, [CurrentVersionId] = NULL
		WHERE [CurrentVersionId] IN (SELECT [ProtocolVersionId] FROM [dbo].[ProtocolVersions] WHERE [ChangeSummary] = N'{first}');
	DELETE FROM [dbo].[ProtocolVersions] WHERE [ChangeSummary] IN (N'{first}', N'{legacy}')
		AND NOT EXISTS (SELECT 1 FROM [dbo].[ProtocolVersionApprovals] a WHERE a.[ProtocolVersionId] = [ProtocolVersions].[ProtocolVersionId]);
END");
		}

		private static void MapCallRows(IDbConnection connection, IDbTransaction transaction, int protocolId, int firstVersionId)
		{
			// Rows with a content hash cite the version with that hash (1.0 itself, or a legacy version).
			using (var byHash = Command(connection, transaction, @"
UPDATE c SET c.[ProtocolVersionId] = v.[ProtocolVersionId]
FROM [dbo].[CallProtocols] c
CROSS APPLY (SELECT TOP 1 x.[ProtocolVersionId] FROM [dbo].[ProtocolVersions] x
	WHERE x.[DispatchProtocolId] = c.[DispatchProtocolId] AND x.[ContentHash] = c.[ContentHash] ORDER BY x.[ProtocolVersionId]) v
WHERE c.[DispatchProtocolId] = @Id AND c.[ProtocolVersionId] IS NULL AND c.[ContentHash] IS NOT NULL"))
			{
				Add(byHash, "@Id", protocolId);
				byHash.ExecuteNonQuery();
			}

			// Rows recorded before content hashes existed cite version 1.0, the text as P1 found it.
			using var noHash = Command(connection, transaction,
				"UPDATE [dbo].[CallProtocols] SET [ProtocolVersionId] = @VersionId WHERE [DispatchProtocolId] = @Id AND [ProtocolVersionId] IS NULL AND [ContentHash] IS NULL");
			Add(noHash, "@VersionId", firstVersionId);
			Add(noHash, "@Id", protocolId);
			noHash.ExecuteNonQuery();
		}

		/// <summary>ProtocolContentHash version 1: SHA-256 over the UTF-8 bytes of the stored text (null as empty), lower-case hex.</summary>
		internal static string Hash(string storedText) =>
			Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(storedText ?? string.Empty))).ToLowerInvariant();

		/// <summary>The schema 1 block document over the stored text (one html block; anchors are derived when shown).</summary>
		internal static string ContentJson(string storedText) =>
			"{\"schema\":1,\"blocks\":[{\"type\":\"html\",\"key\":\"b1\",\"html\":" + System.Text.Json.JsonSerializer.Serialize(storedText ?? string.Empty) + "}],\"anchors\":[]}";

		private static IDbCommand Command(IDbConnection connection, IDbTransaction transaction, string sql)
		{
			var command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandTimeout = 600;
			command.CommandText = sql;
			return command;
		}

		private static void Add(IDbCommand command, string name, object value)
		{
			var parameter = command.CreateParameter();
			parameter.ParameterName = name;
			parameter.Value = value ?? DBNull.Value;
			if (value is string)
				parameter.DbType = DbType.String;
			command.Parameters.Add(parameter);
		}
	}
}
