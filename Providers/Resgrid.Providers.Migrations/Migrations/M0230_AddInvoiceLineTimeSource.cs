using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Where a generated hourly unit line's on-scene time came from (InvoiceLineTimeSources): a unit status the unit linked
	/// to the call, one Resgrid linked or inferred, or the call's whole logged-to-closed window. Shown to the clerk on the
	/// invoice editor and view, never on the customer copy. Null for manual lines and lines written before this migration.
	/// </summary>
	[Migration(230)]
	public class M0230_AddInvoiceLineTimeSource : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('InvoiceLineItems', 'TimeSource') IS NULL ALTER TABLE [InvoiceLineItems] ADD [TimeSource] int NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF COL_LENGTH('InvoiceLineItems', 'TimeSource') IS NOT NULL ALTER TABLE [InvoiceLineItems] DROP COLUMN [TimeSource];");
		}
	}
}
