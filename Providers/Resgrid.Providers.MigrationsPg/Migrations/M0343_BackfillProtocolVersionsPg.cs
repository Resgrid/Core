using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0343_BackfillProtocolVersions (R-P2): every protocol without versions gets a published 1.0 from its
	/// current text (copied server side; the hash is SHA-256 over the UTF-8 text computed in C#, the same algorithm as
	/// ProtocolContentHash), P0 snapshots with other text become Legacy versions 0.n, and callprotocols rows get their
	/// protocolversionid (by content hash, or 1.0 for rows recorded before hashes). Only text and integer parameters are bound,
	/// so no timestamp ever crosses Npgsql's kind rules. One transaction; a second run changes nothing.
	/// </summary>
	[Migration(343)]
	public class M0343_BackfillProtocolVersionsPg : Migration
	{
		internal const string FirstVersionSummary = "Version 1.0: the protocol's text when protocol versions were introduced.";
		internal const string LegacySummary = "Text a call was dispatched with before protocol versions were introduced.";

		public override void Up()
		{
			Execute.WithConnection((connection, transaction) =>
			{
				var protocols = new List<(int Id, string Text)>();
				using (var read = Command(connection, transaction,
					"SELECT p.dispatchprotocolid, p.protocoltext::text FROM public.dispatchprotocols p " +
					"WHERE NOT EXISTS (SELECT 1 FROM public.protocolversions v WHERE v.dispatchprotocolid = p.dispatchprotocolid) ORDER BY p.dispatchprotocolid"))
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
INSERT INTO public.protocolversions (departmentid, dispatchprotocolid, major, minor, status, effectiveon, contentjson, contenthtml, contenthash,
	changesummary, requiresacknowledgment, createdon, createdbyuserid, publishedon, publishedbyuserid, rowversion)
SELECT p.departmentid, p.dispatchprotocolid, 1, 0, 3, COALESCE(p.updatedon, p.createdon), @Json, p.protocoltext::text, @Hash,
	@Summary, false, p.createdon, p.createdbyuserid, COALESCE(p.updatedon, p.createdon), p.updatedbyuserid, 0
FROM public.dispatchprotocols p WHERE p.dispatchprotocolid = @Id
RETURNING protocolversionid"))
					{
						Add(insert, "Json", ContentJson(text));
						Add(insert, "Hash", hash);
						Add(insert, "Summary", FirstVersionSummary);
						Add(insert, "Id", id);
						versionId = Convert.ToInt32(insert.ExecuteScalar());
					}

					using (var update = Command(connection, transaction,
						"UPDATE public.dispatchprotocols SET status = 1, currentversionid = @VersionId WHERE dispatchprotocolid = @Id"))
					{
						Add(update, "VersionId", versionId);
						Add(update, "Id", id);
						update.ExecuteNonQuery();
					}

					var snapshots = new List<(int SnapshotId, string Hash, string Text)>();
					using (var read = Command(connection, transaction,
						"SELECT dispatchprotocolsnapshotid, contenthash, protocoltext FROM public.dispatchprotocolsnapshots WHERE dispatchprotocolid = @Id " +
						"ORDER BY firstusedon, dispatchprotocolsnapshotid"))
					{
						Add(read, "Id", id);
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
INSERT INTO public.protocolversions (departmentid, dispatchprotocolid, major, minor, status, effectiveon, contentjson, contenthtml, contenthash,
	changesummary, requiresacknowledgment, createdon, rowversion)
SELECT s.departmentid, s.dispatchprotocolid, 0, @Minor, 6, s.firstusedon, @Json, s.protocoltext, s.contenthash, @Summary, false, s.firstusedon, 0
FROM public.dispatchprotocolsnapshots s WHERE s.dispatchprotocolsnapshotid = @SnapshotId");
						Add(legacy, "Minor", minor);
						Add(legacy, "Json", ContentJson(snapshot.Text));
						Add(legacy, "Summary", LegacySummary);
						Add(legacy, "SnapshotId", snapshot.SnapshotId);
						legacy.ExecuteNonQuery();
					}

					MapCallRows(connection, transaction, id, versionId);
				}

				var protocolIds = new List<int>();
				using (var unmapped = Command(connection, transaction,
					"SELECT DISTINCT dispatchprotocolid FROM public.callprotocols WHERE protocolversionid IS NULL"))
				using (var reader = unmapped.ExecuteReader())
				{
					while (reader.Read())
						protocolIds.Add(reader.GetInt32(0));
				}

				foreach (var protocolId in protocolIds)
				{
					int? first = null;
					using (var find = Command(connection, transaction,
						"SELECT protocolversionid FROM public.protocolversions WHERE dispatchprotocolid = @Id AND major = 1 AND minor = 0 AND changesummary = @Summary::citext ORDER BY protocolversionid LIMIT 1"))
					{
						Add(find, "Id", protocolId);
						Add(find, "Summary", FirstVersionSummary);
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
			Execute.Sql(@"
DO $$
BEGIN
	IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'callprotocols' AND column_name = 'protocolversionid') THEN
		UPDATE public.callprotocols SET protocolversionid = NULL;
	END IF;
END $$;");
			Execute.Sql($@"
DO $$
BEGIN
	IF to_regclass('public.protocolversions') IS NOT NULL THEN
		UPDATE public.dispatchprotocols SET status = 0, currentversionid = NULL
			WHERE currentversionid IN (SELECT protocolversionid FROM public.protocolversions WHERE changesummary = '{first}');
		DELETE FROM public.protocolversions v WHERE v.changesummary IN ('{first}', '{legacy}')
			AND NOT EXISTS (SELECT 1 FROM public.protocolversionapprovals a WHERE a.protocolversionid = v.protocolversionid);
	END IF;
END $$;");
		}

		private static void MapCallRows(IDbConnection connection, IDbTransaction transaction, int protocolId, int firstVersionId)
		{
			using (var byHash = Command(connection, transaction, @"
UPDATE public.callprotocols c SET protocolversionid = (
	SELECT x.protocolversionid FROM public.protocolversions x
	WHERE x.dispatchprotocolid = c.dispatchprotocolid AND x.contenthash = c.contenthash ORDER BY x.protocolversionid LIMIT 1)
WHERE c.dispatchprotocolid = @Id AND c.protocolversionid IS NULL AND c.contenthash IS NOT NULL
	AND EXISTS (SELECT 1 FROM public.protocolversions x WHERE x.dispatchprotocolid = c.dispatchprotocolid AND x.contenthash = c.contenthash)"))
			{
				Add(byHash, "Id", protocolId);
				byHash.ExecuteNonQuery();
			}

			using var noHash = Command(connection, transaction,
				"UPDATE public.callprotocols SET protocolversionid = @VersionId WHERE dispatchprotocolid = @Id AND protocolversionid IS NULL AND contenthash IS NULL");
			Add(noHash, "VersionId", firstVersionId);
			Add(noHash, "Id", protocolId);
			noHash.ExecuteNonQuery();
		}

		internal static string Hash(string storedText) =>
			Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(storedText ?? string.Empty))).ToLowerInvariant();

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
