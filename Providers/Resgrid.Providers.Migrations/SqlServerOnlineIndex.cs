using System.Collections.Generic;

namespace Resgrid.Providers.Migrations
{
	/// <summary>
	/// Builds CREATE INDEX statements that ask for ONLINE only where online index builds exist.
	/// <para>
	/// Standard, Web, Express and Personal reject <c>WITH (ONLINE = ON)</c> outright. Because these
	/// migrations run with <see cref="FluentMigrator.TransactionBehavior.None"/>, that rejection aborts
	/// the migration part-way through and leaves the schema half-applied, so the operator has to clear
	/// it before retrying. Emitting the same index without ONLINE keeps the schema identical on every
	/// edition; the only difference is that the build holds its lock for the duration instead of just
	/// at the start and end.
	/// </para>
	/// <para>
	/// The edition is resolved inside the batch at execution time rather than when the assembly is
	/// built, so one build runs unchanged against every deployment. The existence check is in the same
	/// batch for the same reason: FluentMigrator evaluates <c>Schema.Index.Exists</c> while collecting
	/// expressions, which for a table created by the same migration is before that table exists.
	/// </para>
	/// </summary>
	public static class SqlServerOnlineIndex
	{
		// EngineEdition 3 = Enterprise/Developer/Evaluation, 5 = Azure SQL Database,
		// 8 = Azure SQL Managed Instance. No other edition can build an index online.
		private const string SupportsOnline = "CONVERT(int, SERVERPROPERTY('EngineEdition')) IN (3, 5, 8)";

		/// <param name="columns">Column list exactly as it appears in the DDL, including ASC/DESC.</param>
		/// <param name="filter">Optional filtered-index predicate, without the WHERE keyword.</param>
		/// <param name="sortInTempDb">Keeps the intermediate sort out of the user database.</param>
		public static string Create(string indexName, string tableName, IEnumerable<string> columns,
			bool unique = false, string filter = null, bool sortInTempDb = false)
		{
			var columnList = string.Join(", ", columns);
			var uniqueClause = unique ? "UNIQUE " : string.Empty;
			var filterClause = string.IsNullOrWhiteSpace(filter) ? string.Empty : $" WHERE {filter}";
			var onlineOptions = sortInTempDb ? "ONLINE = ON, SORT_IN_TEMPDB = ON" : "ONLINE = ON";
			var offlineOptions = sortInTempDb ? "SORT_IN_TEMPDB = ON" : string.Empty;

			return $@"IF NOT EXISTS (SELECT 1 FROM sys.indexes
	WHERE [name] = N'{indexName}' AND [object_id] = OBJECT_ID(N'[{tableName}]'))
BEGIN
	DECLARE @indexOptions nvarchar(100) =
		CASE WHEN {SupportsOnline} THEN N'{onlineOptions}' ELSE N'{offlineOptions}' END;
	DECLARE @createIndex nvarchar(max) =
		N'CREATE {uniqueClause}INDEX [{indexName}] ON [{tableName}] ({columnList}){filterClause}' +
		CASE WHEN LEN(@indexOptions) > 0 THEN N' WITH (' + @indexOptions + N')' ELSE N'' END + N';';
	EXEC sp_executesql @createIndex;
END";
		}
	}
}
