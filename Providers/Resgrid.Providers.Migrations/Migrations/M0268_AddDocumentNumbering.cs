using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Configurable document numbers (setting 117 and setting 72's DocumentPatterns). Work orders, invoices, bids and daily time
	/// reports keep their int numbers (unique, ordered, on the API) and gain DisplayNumber, the number as issued; existing rows
	/// are backfilled with the text they have always shown, so a custom pattern that reads the same carries on after them.
	/// DocumentNumberSequences counts the custom patterns' sequences, one row per department, kind and scope. No FK; the SQL
	/// Server department purge and the readiness/business-ops cleanup delete the rows explicitly.
	/// </summary>
	[Migration(268)]
	public class M0268_AddDocumentNumbering : Migration
	{
		private const string Table = "DocumentNumberSequences";

		public override void Up()
		{
			if (!Schema.Table(Table).Exists())
			{
				Create.Table(Table)
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Kind").AsString(32).NotNullable()
					.WithColumn("ScopeKey").AsString(100).NotNullable()
					.WithColumn("LastSequence").AsInt32().NotNullable()
					.WithColumn("FloorSequence").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("FloorSetOn").AsDateTime2().Nullable()
					.WithColumn("FloorSetByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable();

				Create.PrimaryKey("PK_DocumentNumberSequences").OnTable(Table).Columns("DepartmentId", "Kind", "ScopeKey");
			}

			foreach (var table in new[] { "WorkOrders", "Invoices", "Bids", "DeploymentTimeReports" })
				Execute.Sql($"IF COL_LENGTH('{table}', 'DisplayNumber') IS NULL ALTER TABLE [{table}] ADD [DisplayNumber] nvarchar(50) NULL;");

			// Separate batches: the columns must exist before the statements that fill them are compiled.
			Execute.Sql("UPDATE [WorkOrders] SET [DisplayNumber] = 'WO-' + CAST([NumberYear] AS nvarchar(10)) + '-' + " +
				"CASE WHEN [NumberSequence] >= 1000000 THEN CAST([NumberSequence] AS nvarchar(10)) ELSE RIGHT('000000' + CAST([NumberSequence] AS nvarchar(10)), 6) END " +
				"WHERE [DisplayNumber] IS NULL;");
			Execute.Sql("UPDATE [Invoices] SET [DisplayNumber] = CAST([InvoiceNumber] AS nvarchar(20)) WHERE [DisplayNumber] IS NULL;");
			Execute.Sql("UPDATE [Bids] SET [DisplayNumber] = CAST([BidNumber] AS nvarchar(20)) WHERE [DisplayNumber] IS NULL;");
			Execute.Sql("UPDATE [DeploymentTimeReports] SET [DisplayNumber] = CAST([ReportNumber] AS nvarchar(20)) WHERE [DisplayNumber] IS NULL;");
		}

		public override void Down()
		{
			foreach (var table in new[] { "WorkOrders", "Invoices", "Bids", "DeploymentTimeReports" })
				Execute.Sql($"IF COL_LENGTH('{table}', 'DisplayNumber') IS NOT NULL ALTER TABLE [{table}] DROP COLUMN [DisplayNumber];");

			if (Schema.Table(Table).Exists())
				Delete.Table(Table);
		}
	}
}
