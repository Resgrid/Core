using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Status options can name the options the apps offer next (see the SQL Server M0263). Null = no restriction.
	/// </summary>
	[Migration(263)]
	public class M0263_AddCustomStateDetailNextStatesPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE customstatedetails ADD COLUMN IF NOT EXISTS nextstatedetailids character varying(1000) NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE customstatedetails DROP COLUMN IF EXISTS nextstatedetailids;");
		}
	}
}
