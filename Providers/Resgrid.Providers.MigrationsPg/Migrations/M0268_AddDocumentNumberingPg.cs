using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Configurable document numbers (setting 117 and setting 72's DocumentPatterns). Work orders, invoices, bids and daily time
	/// reports keep their int numbers (unique, ordered, on the API) and gain displaynumber, the number as issued; existing rows
	/// are backfilled with the text they have always shown, so a custom pattern that reads the same carries on after them.
	/// documentnumbersequences counts the custom patterns' sequences, one row per department, kind and scope. No FK. Text
	/// columns are citext, so keys and numbers compare case-insensitively as they do on SQL Server.
	/// </summary>
	[Migration(268)]
	public class M0268_AddDocumentNumberingPg : Migration
	{
		private const string Table = "documentnumbersequences";

		public override void Up()
		{
			if (!Schema.Table(Table).Exists())
			{
				Create.Table(Table)
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("kind").AsCustom("citext").NotNullable()
					.WithColumn("scopekey").AsCustom("citext").NotNullable()
					.WithColumn("lastsequence").AsInt32().NotNullable()
					.WithColumn("floorsequence").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("floorseton").AsDateTime2().Nullable()
					.WithColumn("floorsetbyuserid").AsCustom("citext").Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable();

				Create.PrimaryKey("pk_documentnumbersequences").OnTable(Table).Columns("departmentid", "kind", "scopekey");
			}

			foreach (var table in new[] { "workorders", "invoices", "bids", "deploymenttimereports" })
				Execute.Sql($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS displaynumber citext NULL;");

			// lpad would cut a longer sequence to six digits, so it only pads the short ones.
			Execute.Sql("UPDATE workorders SET displaynumber = 'WO-' || numberyear::text || '-' || " +
				"CASE WHEN numbersequence >= 1000000 THEN numbersequence::text ELSE lpad(numbersequence::text, 6, '0') END WHERE displaynumber IS NULL;");
			Execute.Sql("UPDATE invoices SET displaynumber = invoicenumber::text WHERE displaynumber IS NULL;");
			Execute.Sql("UPDATE bids SET displaynumber = bidnumber::text WHERE displaynumber IS NULL;");
			Execute.Sql("UPDATE deploymenttimereports SET displaynumber = reportnumber::text WHERE displaynumber IS NULL;");
		}

		public override void Down()
		{
			foreach (var table in new[] { "workorders", "invoices", "bids", "deploymenttimereports" })
				Execute.Sql($"ALTER TABLE {table} DROP COLUMN IF EXISTS displaynumber;");

			if (Schema.Table(Table).Exists())
				Delete.Table(Table);
		}
	}
}
