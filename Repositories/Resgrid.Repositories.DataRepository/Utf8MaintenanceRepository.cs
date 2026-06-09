using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Backend-aware (SQL Server / PostgreSQL) maintenance access for the nightly UTF-8 data cleanup
	/// worker. Builds metadata and row queries dynamically from INFORMATION_SCHEMA so it covers every
	/// text column without a hand-maintained list.
	/// </summary>
	public class Utf8MaintenanceRepository : IUtf8MaintenanceRepository
	{
		private const string ProgressTableName = "utf8cleanupprogress";

		// Tables the sweep should never touch (its own bookkeeping + the migration history table).
		private static readonly HashSet<string> ExcludedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			ProgressTableName,
			"versioninfo"
		};

		private readonly IConnectionProvider _connectionProvider;

		public Utf8MaintenanceRepository(IConnectionProvider connectionProvider)
		{
			_connectionProvider = connectionProvider;
		}

		private static bool IsPostgres => DataConfig.DatabaseType == DatabaseTypes.Postgres;

		private static string Quote(string identifier)
		{
			return IsPostgres ? "\"" + identifier + "\"" : "[" + identifier + "]";
		}

		private static string QualifiedTable(Utf8TextColumnTarget target)
		{
			return string.IsNullOrEmpty(target.Schema)
				? Quote(target.TableName)
				: Quote(target.Schema) + "." + Quote(target.TableName);
		}

		private static string ProgressTable => (IsPostgres ? "public." : "dbo.") + ProgressTableName;

		public async Task<List<Utf8TextColumnTarget>> GetTextColumnTargetsAsync(CancellationToken cancellationToken)
		{
			try
			{
				string columnsSql;
				string pkSql;

				if (IsPostgres)
				{
					columnsSql = @"
						SELECT c.table_schema AS TableSchema, c.table_name AS TableName, c.column_name AS ColumnName
						FROM information_schema.columns c
						INNER JOIN information_schema.tables t
							ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE'
						WHERE (c.data_type IN ('character', 'character varying', 'text') OR c.udt_name = 'citext')
							AND c.table_schema NOT IN ('pg_catalog', 'information_schema')";

					pkSql = @"
						SELECT tc.table_schema AS TableSchema, tc.table_name AS TableName, kcu.column_name AS ColumnName
						FROM information_schema.table_constraints tc
						INNER JOIN information_schema.key_column_usage kcu
							ON tc.constraint_name = kcu.constraint_name
							AND tc.table_schema = kcu.table_schema
							AND tc.table_name = kcu.table_name
						WHERE tc.constraint_type = 'PRIMARY KEY'
							AND tc.table_schema NOT IN ('pg_catalog', 'information_schema')";
				}
				else
				{
					columnsSql = @"
						SELECT c.TABLE_SCHEMA AS TableSchema, c.TABLE_NAME AS TableName, c.COLUMN_NAME AS ColumnName
						FROM INFORMATION_SCHEMA.COLUMNS c
						INNER JOIN INFORMATION_SCHEMA.TABLES t
							ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_TYPE = 'BASE TABLE'
						WHERE c.DATA_TYPE IN ('char', 'varchar', 'nchar', 'nvarchar', 'text', 'ntext')";

					pkSql = @"
						SELECT tc.TABLE_SCHEMA AS TableSchema, tc.TABLE_NAME AS TableName, kcu.COLUMN_NAME AS ColumnName
						FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
						INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
							ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
							AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
							AND tc.TABLE_NAME = kcu.TABLE_NAME
						WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'";
				}

				using (DbConnection conn = _connectionProvider.Create())
				{
					await conn.OpenAsync(cancellationToken);

					var columns = (await conn.QueryAsync<MetaColumn>(new CommandDefinition(columnsSql, cancellationToken: cancellationToken))).ToList();
					var pkRows = (await conn.QueryAsync<MetaColumn>(new CommandDefinition(pkSql, cancellationToken: cancellationToken))).ToList();

					// Keep only tables with EXACTLY one primary-key column (needed for keyset paging).
					var singlePk = pkRows
						.GroupBy(p => p.TableSchema + "." + p.TableName)
						.Where(g => g.Count() == 1)
						.ToDictionary(g => g.Key, g => g.First().ColumnName, StringComparer.OrdinalIgnoreCase);

					var targets = new List<Utf8TextColumnTarget>();

					foreach (var group in columns.GroupBy(c => c.TableSchema + "." + c.TableName))
					{
						var first = group.First();

						if (ExcludedTables.Contains(first.TableName))
							continue;

						if (!singlePk.TryGetValue(group.Key, out var pkColumn))
							continue; // no single-column PK -> cannot page safely

						// Never clean the primary-key column itself.
						var textColumns = group
							.Select(c => c.ColumnName)
							.Where(name => !string.Equals(name, pkColumn, StringComparison.OrdinalIgnoreCase))
							.ToList();

						if (textColumns.Count == 0)
							continue;

						targets.Add(new Utf8TextColumnTarget
						{
							Schema = first.TableSchema,
							TableName = first.TableName,
							PrimaryKeyColumn = pkColumn,
							TextColumns = textColumns
						});
					}

					return targets;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<Utf8RowBatch> GetRowBatchAsync(Utf8TextColumnTarget target, string lastKey, int batchSize, CancellationToken cancellationToken)
		{
			try
			{
				var table = QualifiedTable(target);
				var pk = Quote(target.PrimaryKeyColumn);
				var columnList = string.Join(", ", target.TextColumns.Select(Quote));
				var hasCursor = !string.IsNullOrEmpty(lastKey);
				var whereClause = hasCursor ? "WHERE " + pk + " > @lastKey " : string.Empty;

				string sql = IsPostgres
					? $"SELECT {pk} AS rg_pk, {columnList} FROM {table} {whereClause}ORDER BY {pk} LIMIT @batchSize"
					: $"SELECT TOP (@batchSize) {pk} AS rg_pk, {columnList} FROM {table} {whereClause}ORDER BY {pk}";

				var dynamicParameters = new DynamicParameters();
				dynamicParameters.Add("batchSize", batchSize);
				if (hasCursor)
					dynamicParameters.Add("lastKey", lastKey);

				using (DbConnection conn = _connectionProvider.Create())
				{
					await conn.OpenAsync(cancellationToken);

					var rows = await conn.QueryAsync(new CommandDefinition(sql, dynamicParameters, cancellationToken: cancellationToken));

					var batch = new Utf8RowBatch();

					foreach (var item in rows)
					{
						var dict = (IDictionary<string, object>)item;
						var key = Convert.ToString(dict["rg_pk"], CultureInfo.InvariantCulture);

						var row = new Utf8TextRow { Key = key };

						foreach (var column in target.TextColumns)
						{
							dict.TryGetValue(column, out var value);
							row.Columns[column] = value as string;
						}

						batch.Rows.Add(row);
						batch.LastKey = key;
					}

					return batch;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Utf8 cleanup failed reading batch for " + target.Key);
				throw;
			}
		}

		public async Task<int> UpdateRowsAsync(Utf8TextColumnTarget target, IReadOnlyList<Utf8TextRow> rows, CancellationToken cancellationToken)
		{
			if (rows == null || rows.Count == 0)
				return 0;

			try
			{
				var table = QualifiedTable(target);
				var pk = Quote(target.PrimaryKeyColumn);
				var updated = 0;

				using (DbConnection conn = _connectionProvider.Create())
				{
					await conn.OpenAsync(cancellationToken);

					using (var transaction = await conn.BeginTransactionAsync(cancellationToken))
					{
						foreach (var row in rows)
						{
							if (row.Columns.Count == 0)
								continue;

							var setClauses = new List<string>(row.Columns.Count);
							var dynamicParameters = new DynamicParameters();
							var index = 0;

							foreach (var column in row.Columns)
							{
								var paramName = "c" + index++;
								setClauses.Add(Quote(column.Key) + " = @" + paramName);
								dynamicParameters.Add(paramName, (object)column.Value ?? DBNull.Value);
							}

							dynamicParameters.Add("rg_key", row.Key);

							var sql = $"UPDATE {table} SET {string.Join(", ", setClauses)} WHERE {pk} = @rg_key";

							updated += await conn.ExecuteAsync(new CommandDefinition(sql, dynamicParameters, transaction, cancellationToken: cancellationToken));
						}

						await transaction.CommitAsync(cancellationToken);
					}
				}

				return updated;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Utf8 cleanup failed updating rows for " + target.Key);
				throw;
			}
		}

		public async Task<Utf8CleanupProgress> GetProgressAsync(string tableKey, CancellationToken cancellationToken)
		{
			try
			{
				var sql = $@"
					SELECT tablename AS TableName, lastprocessedkey AS LastProcessedKey, lastcompletedutc AS LastCompletedUtc,
						rowsscanned AS RowsScanned, rowsfixed AS RowsFixed, updatedonutc AS UpdatedOnUtc
					FROM {ProgressTable}
					WHERE tablename = @tablename";

				var dynamicParameters = new DynamicParameters();
				dynamicParameters.Add("tablename", tableKey);

				using (DbConnection conn = _connectionProvider.Create())
				{
					await conn.OpenAsync(cancellationToken);

					return await conn.QueryFirstOrDefaultAsync<Utf8CleanupProgress>(new CommandDefinition(sql, dynamicParameters, cancellationToken: cancellationToken));
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task SaveProgressAsync(Utf8CleanupProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				var updateSql = $@"
					UPDATE {ProgressTable}
					SET lastprocessedkey = @LastProcessedKey, lastcompletedutc = @LastCompletedUtc,
						rowsscanned = @RowsScanned, rowsfixed = @RowsFixed, updatedonutc = @UpdatedOnUtc
					WHERE tablename = @TableName";

				var insertSql = $@"
					INSERT INTO {ProgressTable} (tablename, lastprocessedkey, lastcompletedutc, rowsscanned, rowsfixed, updatedonutc)
					VALUES (@TableName, @LastProcessedKey, @LastCompletedUtc, @RowsScanned, @RowsFixed, @UpdatedOnUtc)";

				using (DbConnection conn = _connectionProvider.Create())
				{
					await conn.OpenAsync(cancellationToken);

					var affected = await conn.ExecuteAsync(new CommandDefinition(updateSql, progress, cancellationToken: cancellationToken));

					if (affected == 0)
						await conn.ExecuteAsync(new CommandDefinition(insertSql, progress, cancellationToken: cancellationToken));
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		private class MetaColumn
		{
			public string TableSchema { get; set; }
			public string TableName { get; set; }
			public string ColumnName { get; set; }
		}
	}
}
