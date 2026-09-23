using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Where a generated hourly unit line's on-scene time came from (see the SQL Server M0230).
	/// </summary>
	[Migration(230)]
	public class M0230_AddInvoiceLineTimeSourcePg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE invoicelineitems ADD COLUMN IF NOT EXISTS timesource integer NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE invoicelineitems DROP COLUMN IF EXISTS timesource;");
		}
	}
}
