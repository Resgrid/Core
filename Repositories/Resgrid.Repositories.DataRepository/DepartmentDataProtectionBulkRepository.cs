using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Bulk data access for the ADP migration engine. See
	/// <see cref="IDepartmentDataProtectionBulkRepository"/> for the contract. All identifiers are
	/// rendered from code-reviewed AdpTableBinding constants; values are always Dapper parameters.
	/// </summary>
	public class DepartmentDataProtectionBulkRepository : IDepartmentDataProtectionBulkRepository
	{
		// ASCII bytes of the rgdpb: binary envelope header, for prefix compares.
		private static readonly byte[] BinaryPrefixBytes = Encoding.ASCII.GetBytes("rgdpb:");

		private readonly IConnectionProvider _connectionProvider;
		private readonly string _schema;
		private readonly bool _isPostgres;

		public DepartmentDataProtectionBulkRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration)
		{
			_connectionProvider = connectionProvider;
			_schema = sqlConfiguration.SchemaName;
			_isPostgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
		}

		public async Task<long> CountRowsAsync(AdpTableBinding binding, int departmentId,
			CancellationToken cancellationToken = default)
		{
			var sql = $"SELECT COUNT_BIG(*) FROM {Table(binding.TableName)} WHERE {Scope(binding)}";
			if (_isPostgres)
				sql = sql.Replace("COUNT_BIG", "COUNT");

			using var connection = _connectionProvider.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteScalarAsync<long>(new Dapper.CommandDefinition(sql,
				new { DepartmentId = departmentId }, cancellationToken: cancellationToken));
		}

		public async Task<IReadOnlyList<AdpBulkFieldRow>> GetBatchAsync(AdpTableBinding binding, int departmentId,
			string afterCursor, int batchSize, CancellationToken cancellationToken = default)
		{
			var columns = SelectColumns(binding);
			var columnList = string.Join(", ", columns.Select(Ident));
			var cursorClause = string.IsNullOrEmpty(afterCursor) ? "" : $" AND {Ident(binding.PkColumn)} > @After";

			var sql = _isPostgres
				? $"SELECT {Ident(binding.PkColumn)} AS pk, {columnList} FROM {Table(binding.TableName)} WHERE {Scope(binding)}{cursorClause} ORDER BY {Ident(binding.PkColumn)} LIMIT @BatchSize"
				: $"SELECT TOP (@BatchSize) {Ident(binding.PkColumn)} AS pk, {columnList} FROM {Table(binding.TableName)} WHERE {Scope(binding)}{cursorClause} ORDER BY {Ident(binding.PkColumn)}";

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("BatchSize", batchSize);
			if (!string.IsNullOrEmpty(afterCursor))
			{
				if (binding.PkIsNumeric)
					parameters.Add("After", long.Parse(afterCursor, CultureInfo.InvariantCulture));
				else
					parameters.Add("After", afterCursor);
			}

			using var connection = _connectionProvider.Create();
			await connection.OpenAsync(cancellationToken);
			var rows = await connection.QueryAsync(new Dapper.CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

			var result = new List<AdpBulkFieldRow>();
			foreach (IDictionary<string, object> raw in rows)
			{
				var row = new AdpBulkFieldRow
				{
					RowKey = Convert.ToString(raw["pk"], CultureInfo.InvariantCulture)
				};

				// PostgreSQL returns lowercase column keys; map back to the binding's casing.
				foreach (var column in columns)
				{
					var key = raw.Keys.FirstOrDefault(k => string.Equals(k, column, StringComparison.OrdinalIgnoreCase));
					row.Values[column] = key == null ? null : raw[key];
				}

				result.Add(row);
			}

			return result;
		}

		public async Task ApplyBatchAsync(AdpTableBinding binding, IReadOnlyList<AdpBulkRowUpdate> updates,
			int departmentDataProtectionMigrationId, string newCursor, long rowsProcessedDelta,
			long rowsAlreadyProtectedDelta, long rowsAnomalousDelta, CancellationToken cancellationToken)
		{
			using var connection = _connectionProvider.Create();
			await connection.OpenAsync(cancellationToken);

			// One transaction for the row writes AND the cursor advance: a crash between batches
			// re-processes at most one batch, and re-processing is a no-op under the
			// double-encryption guard.
			using var transaction = await connection.BeginTransactionAsync(cancellationToken);

			if (updates != null)
			{
				foreach (var update in updates)
				{
					if (update.SetValues.Count == 0)
						continue;

					var setColumns = update.SetValues.Keys.ToList();
					var setClause = string.Join(", ", setColumns.Select((c, i) => $"{Ident(c)} = @v{i}"));

					var parameters = new DynamicParameters();
					for (var i = 0; i < setColumns.Count; i++)
						parameters.Add($"v{i}", update.SetValues[setColumns[i]]);

					if (binding.PkIsNumeric)
						parameters.Add("RowKey", long.Parse(update.RowKey, CultureInfo.InvariantCulture));
					else
						parameters.Add("RowKey", update.RowKey);

					var sql = $"UPDATE {Table(binding.TableName)} SET {setClause} WHERE {Ident(binding.PkColumn)} = @RowKey";
					await connection.ExecuteAsync(new Dapper.CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));
				}
			}

			var migrationSql = _isPostgres
				? $"UPDATE {Table("DepartmentDataProtectionMigrations")} SET cursor = @Cursor, rowsprocessed = rowsprocessed + @Processed, rowsalreadyprotected = rowsalreadyprotected + @AlreadyProtected, rowsanomalous = rowsanomalous + @Anomalous, checkpointedon = @UtcNow WHERE departmentdataprotectionmigrationid = @MigrationId"
				: $"UPDATE {Table("DepartmentDataProtectionMigrations")} SET [Cursor] = @Cursor, [RowsProcessed] = [RowsProcessed] + @Processed, [RowsAlreadyProtected] = [RowsAlreadyProtected] + @AlreadyProtected, [RowsAnomalous] = [RowsAnomalous] + @Anomalous, [CheckpointedOn] = @UtcNow WHERE [DepartmentDataProtectionMigrationId] = @MigrationId";

			await connection.ExecuteAsync(new Dapper.CommandDefinition(migrationSql, new
			{
				Cursor = newCursor,
				Processed = rowsProcessedDelta,
				AlreadyProtected = rowsAlreadyProtectedDelta,
				Anomalous = rowsAnomalousDelta,
				UtcNow = DateTime.UtcNow,
				MigrationId = departmentDataProtectionMigrationId
			}, transaction, cancellationToken: cancellationToken));

			await transaction.CommitAsync(cancellationToken);
		}

		public async Task<long> CountTextResidueAsync(AdpTableBinding binding, int departmentId, bool enveloped,
			CancellationToken cancellationToken = default)
		{
			var textColumns = binding.Columns.Where(c => c.StorageKind == ProtectedFieldStorageKind.Text).ToList();
			if (textColumns.Count == 0)
				return 0;

			// Note: citext makes LIKE case-insensitive on PostgreSQL. Envelopes are always written
			// lowercase, so the enveloped scan can only over-match a value that already starts with
			// "rgdp:" in some casing — which the plaintext scan would equally have to treat as
			// suspect. Acceptable for a residue gate that requires zero.
			var predicates = textColumns.Select(c => enveloped
				? $"({Ident(c.ColumnName)} LIKE 'rgdp:%')"
				: $"({Ident(c.ColumnName)} IS NOT NULL AND {Ident(c.ColumnName)} <> '' AND {Ident(c.ColumnName)} NOT LIKE 'rgdp:%')");

			return await CountWhereAsync(binding, departmentId, string.Join(" OR ", predicates), cancellationToken);
		}

		public async Task<long> CountBinaryResidueAsync(AdpTableBinding binding, int departmentId, bool enveloped,
			CancellationToken cancellationToken = default)
		{
			var binaryColumns = binding.Columns.Where(c => c.StorageKind == ProtectedFieldStorageKind.Binary).ToList();
			if (binaryColumns.Count == 0)
				return 0;

			var prefixHex = "0x" + Convert.ToHexString(BinaryPrefixBytes);
			var predicates = binaryColumns.Select(c =>
			{
				var prefixMatch = _isPostgres
					? $"substring({Ident(c.ColumnName)} from 1 for {BinaryPrefixBytes.Length}) = '\\x{Convert.ToHexString(BinaryPrefixBytes).ToLowerInvariant()}'::bytea"
					: $"SUBSTRING({Ident(c.ColumnName)}, 1, {BinaryPrefixBytes.Length}) = {prefixHex}";

				return enveloped
					? $"({Ident(c.ColumnName)} IS NOT NULL AND {prefixMatch})"
					: $"({Ident(c.ColumnName)} IS NOT NULL AND NOT ({prefixMatch}))";
			});

			return await CountWhereAsync(binding, departmentId, string.Join(" OR ", predicates), cancellationToken);
		}

		public async Task<long> CountSupersededKeyVersionResidueAsync(AdpTableBinding binding, int departmentId,
			int targetKeyVersion, CancellationToken cancellationToken = default)
		{
			if (targetKeyVersion <= 0)
				return 0;

			// Both envelope variants carry an ASCII header of "{prefix}{formatVersion}:{keyVersion}:",
			// so "on the target version" is a literal prefix match and everything else enveloped is
			// residue. Composed from the format constants rather than written out, so a future format
			// version cannot leave this silently matching nothing.
			var textTargetPrefix = $"{ProtectedDataEnvelope.Prefix}{ProtectedDataEnvelope.CurrentVersion}:{targetKeyVersion}:";
			var binaryTargetPrefix = Encoding.ASCII.GetBytes(
				$"{ProtectedDataEnvelope.BinaryPrefix}{ProtectedDataEnvelope.CurrentVersion}:{targetKeyVersion}:");

			var predicates = new List<string>();

			foreach (var column in binding.Columns)
			{
				switch (column.StorageKind)
				{
					case ProtectedFieldStorageKind.Text:
						predicates.Add(TextSupersededPredicate(Ident(column.ColumnName), textTargetPrefix));
						break;

					case ProtectedFieldStorageKind.CompanionColumn:
						predicates.Add(TextSupersededPredicate(Ident(column.CompanionColumn), textTargetPrefix));
						break;

					case ProtectedFieldStorageKind.Binary:
					{
						var envelopeMatch = BinaryPrefixPredicate(Ident(column.ColumnName), BinaryPrefixBytes);
						var targetMatch = BinaryPrefixPredicate(Ident(column.ColumnName), binaryTargetPrefix);
						predicates.Add($"({Ident(column.ColumnName)} IS NOT NULL AND {envelopeMatch} AND NOT ({targetMatch}))");
						break;
					}
				}
			}

			if (predicates.Count == 0)
				return 0;

			return await CountWhereAsync(binding, departmentId, string.Join(" OR ", predicates), cancellationToken);
		}

		/// <summary>Enveloped text that is not on the target key version. LIKE metacharacters cannot
		/// appear in the composed prefix (it is prefix, digits and colons), so it is safe inline.</summary>
		private static string TextSupersededPredicate(string column, string targetPrefix) =>
			$"({column} LIKE 'rgdp:%' AND {column} NOT LIKE '{targetPrefix}%')";

		private string BinaryPrefixPredicate(string column, byte[] prefix) => _isPostgres
			? $"substring({column} from 1 for {prefix.Length}) = '\\x{Convert.ToHexString(prefix).ToLowerInvariant()}'::bytea"
			: $"SUBSTRING({column}, 1, {prefix.Length}) = 0x{Convert.ToHexString(prefix)}";

		public async Task<long> CountCompanionResidueAsync(AdpTableBinding binding, int departmentId, bool enveloped,
			CancellationToken cancellationToken = default)
		{
			var companionColumns = binding.Columns.Where(c => c.StorageKind == ProtectedFieldStorageKind.CompanionColumn).ToList();
			if (companionColumns.Count == 0)
				return 0;

			// Enrollment residue: the typed column still holds a value. Offboarding residue: the
			// companion envelope column still holds a value.
			var predicates = companionColumns.Select(c => enveloped
				? $"({Ident(c.CompanionColumn)} IS NOT NULL)"
				: $"({Ident(c.ColumnName)} IS NOT NULL)");

			return await CountWhereAsync(binding, departmentId, string.Join(" OR ", predicates), cancellationToken);
		}

		private async Task<long> CountWhereAsync(AdpTableBinding binding, int departmentId, string predicate,
			CancellationToken cancellationToken)
		{
			var count = _isPostgres ? "COUNT(*)" : "COUNT_BIG(*)";
			var sql = $"SELECT {count} FROM {Table(binding.TableName)} WHERE {Scope(binding)} AND ({predicate})";

			using var connection = _connectionProvider.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteScalarAsync<long>(new Dapper.CommandDefinition(sql,
				new { DepartmentId = departmentId }, cancellationToken: cancellationToken));
		}

		private List<string> SelectColumns(AdpTableBinding binding)
		{
			var columns = new List<string>();
			foreach (var spec in binding.Columns)
			{
				columns.Add(spec.ColumnName);
				if (spec.StorageKind == ProtectedFieldStorageKind.CompanionColumn)
					columns.Add(spec.CompanionColumn);
			}

			if (!string.IsNullOrEmpty(binding.ProtectedMarkerColumn))
				columns.Add(binding.ProtectedMarkerColumn);

			return columns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		private string Scope(AdpTableBinding binding)
		{
			if (!string.IsNullOrEmpty(binding.DepartmentColumn))
				return $"{Ident(binding.DepartmentColumn)} = @DepartmentId";

			return $"{Ident(binding.ParentFkColumn)} IN (SELECT {Ident(binding.ParentPkColumn)} FROM {Table(binding.ParentTable)} WHERE {Ident("DepartmentId")} = @DepartmentId)";
		}

		private string Table(string name) =>
			_isPostgres ? $"{_schema}.{name.ToLowerInvariant()}" : $"{_schema}.[{name}]";

		private string Ident(string name) =>
			_isPostgres ? name.ToLowerInvariant() : $"[{name}]";
	}
}
