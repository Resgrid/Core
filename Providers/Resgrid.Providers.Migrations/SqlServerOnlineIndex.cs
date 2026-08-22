using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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
	/// <para>
	/// Every caller passes compile-time literals, so nothing user-supplied reaches this. The identifier
	/// checks below are not an injection defence; they exist because the DDL is emitted inside a T-SQL
	/// string literal, where a stray quote or bracket would silently produce a statement that only fails
	/// when the migration runs. Failing here turns that into a build-time error instead.
	/// </para>
	/// </summary>
	public static class SqlServerOnlineIndex
	{
		// EngineEdition 3 = Enterprise/Developer/Evaluation, 5 = Azure SQL Database,
		// 8 = Azure SQL Managed Instance. No other edition can build an index online.
		private const string SupportsOnline = "CONVERT(int, SERVERPROPERTY('EngineEdition')) IN (3, 5, 8)";

		private static readonly Regex IdentifierPattern =
			new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

		// A bracketed column, optionally with a sort direction: "[UserId]" or "[LastActiveOn] DESC".
		private static readonly Regex ColumnPattern =
			new Regex(@"^\[[A-Za-z_][A-Za-z0-9_]*\](\s+(ASC|DESC))?$",
				RegexOptions.Compiled | RegexOptions.IgnoreCase);

		/// <param name="columns">Column list exactly as it appears in the DDL, including ASC/DESC.</param>
		/// <param name="filter">Optional filtered-index predicate, without the WHERE keyword.</param>
		/// <param name="sortInTempDb">Keeps the intermediate sort out of the user database.</param>
		/// <exception cref="ArgumentException">
		/// A name or column that is not a plain bracketed identifier, or an empty column list.
		/// </exception>
		public static string Create(string indexName, string tableName, IEnumerable<string> columns,
			bool unique = false, string filter = null, bool sortInTempDb = false)
		{
			RequireIdentifier(indexName, nameof(indexName));
			RequireIdentifier(tableName, nameof(tableName));

			var columnList = (columns ?? Enumerable.Empty<string>()).ToList();
			if (columnList.Count == 0)
				throw new ArgumentException("An index needs at least one column.", nameof(columns));

			foreach (var column in columnList)
			{
				if (column == null || !ColumnPattern.IsMatch(column.Trim()))
					throw new ArgumentException(
						$"'{column}' is not a bracketed column with an optional ASC or DESC.", nameof(columns));
			}

			var uniqueClause = unique ? "UNIQUE " : string.Empty;
			var filterClause = string.IsNullOrWhiteSpace(filter) ? string.Empty : $" WHERE {filter}";
			var onlineOptions = sortInTempDb ? "ONLINE = ON, SORT_IN_TEMPDB = ON" : "ONLINE = ON";
			var offlineOptions = sortInTempDb ? "SORT_IN_TEMPDB = ON" : string.Empty;

			// The predicate is free-form, so it cannot be pattern-checked the way a name can. Doubling its
			// quotes is what makes a filter such as "[Status] = 'Active'" survive being nested in a literal
			// and come back out intact; without it the generated statement would simply be malformed.
			var createIndex = Quote($"CREATE {uniqueClause}INDEX [{indexName}] ON [{tableName}] " +
				$"({string.Join(", ", columnList.Select(column => column.Trim()))}){filterClause}");

			return $@"IF NOT EXISTS (SELECT 1 FROM sys.indexes
	WHERE [name] = N'{indexName}' AND [object_id] = OBJECT_ID(N'[{tableName}]'))
BEGIN
	DECLARE @indexOptions nvarchar(100) =
		CASE WHEN {SupportsOnline} THEN N'{onlineOptions}' ELSE N'{offlineOptions}' END;
	DECLARE @createIndex nvarchar(max) =
		N'{createIndex}' +
		CASE WHEN LEN(@indexOptions) > 0 THEN N' WITH (' + @indexOptions + N')' ELSE N'' END + N';';
	EXEC sp_executesql @createIndex;
END";
		}

		private static void RequireIdentifier(string value, string parameterName)
		{
			if (string.IsNullOrWhiteSpace(value) || !IdentifierPattern.IsMatch(value))
				throw new ArgumentException(
					$"'{value}' is not a plain SQL Server identifier.", parameterName);
		}

		/// <summary>Escapes a fragment for embedding in a single-quoted T-SQL literal.</summary>
		private static string Quote(string value) => value.Replace("'", "''");
	}
}
