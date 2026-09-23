using System;
using System.Data;
using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Marks every unit state and personnel status that already carries a destination as Explicit (see the SQL Server M0229):
	/// primary-key ranges, each its own autocommitted statement, resumable on a re-run.
	/// </summary>
	[Migration(229, TransactionBehavior.None)]
	public class M0229_BackfillExplicitStatusDestinationsPg : Migration
	{
		private const int RangeSize = 100000;

		public override void Up()
		{
			Execute.WithConnection((connection, transaction) =>
			{
				Backfill(connection, transaction, "unitstates", "unitstateid");
				Backfill(connection, transaction, "actionlogs", "actionlogid");
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
				bounds.CommandText = $"SELECT MIN({key}), MAX({key}) FROM {table} WHERE destinationid > 0 AND destinationsource IS NULL";
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
				update.CommandText = $"UPDATE {table} SET destinationsource = 1 WHERE {key} >= @from AND {key} < @to AND destinationid > 0 AND destinationsource IS NULL";
				AddParameter(update, "from", from);
				AddParameter(update, "to", from + RangeSize);
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
