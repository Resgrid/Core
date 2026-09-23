using System;
using System.Data;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Every unit state and personnel status that already carries a destination was written before server-side call
	/// attribution existed, so its destination is one the sender chose: mark it Explicit (StatusDestinationSources 1).
	/// UnitStates and ActionLogs are among the largest tables, so the update walks the primary key in ranges, each range its
	/// own short autocommitted statement (TransactionBehavior.None), and only touches rows still null — a re-run after a
	/// timeout or partial apply resumes where it stopped.
	/// </summary>
	[Migration(229, TransactionBehavior.None)]
	public class M0229_BackfillExplicitStatusDestinations : Migration
	{
		private const int RangeSize = 100000;

		public override void Up()
		{
			Execute.WithConnection((connection, transaction) =>
			{
				Backfill(connection, transaction, "UnitStates", "UnitStateId");
				Backfill(connection, transaction, "ActionLogs", "ActionLogId");
			});
		}

		public override void Down()
		{
			// Nothing to undo: M0228's Down drops the column.
		}

		private static void Backfill(IDbConnection connection, IDbTransaction transaction, string table, string key)
		{
			long min, max;
			using (var bounds = connection.CreateCommand())
			{
				bounds.Transaction = transaction;
				bounds.CommandTimeout = 300;
				// Unfiltered: the key bounds come off the clustered index, while the filtered form scans the table (no index covers
				// DestinationSource) on every run; each range update applies the filter.
				bounds.CommandText = $"SELECT MIN([{key}]), MAX([{key}]) FROM [{table}]";
				using var reader = bounds.ExecuteReader();
				if (!reader.Read() || reader.IsDBNull(0))
					return;

				min = Convert.ToInt64(reader.GetValue(0));
				max = Convert.ToInt64(reader.GetValue(1));
			}

			for (var from = min; from <= max; from += RangeSize)
			{
				using var update = connection.CreateCommand();
				update.Transaction = transaction;
				update.CommandTimeout = 300;
				update.CommandText = $"UPDATE [{table}] SET [DestinationSource] = 1 WHERE [{key}] >= @from AND [{key}] < @to AND [DestinationId] > 0 AND [DestinationSource] IS NULL";
				AddParameter(update, "@from", from);
				AddParameter(update, "@to", from + RangeSize);
				update.ExecuteNonQuery();
			}
		}

		private static void AddParameter(IDbCommand command, string name, long value)
		{
			var parameter = command.CreateParameter();
			parameter.ParameterName = name;
			parameter.DbType = DbType.Int64;
			parameter.Value = value;
			command.Parameters.Add(parameter);
		}
	}
}
