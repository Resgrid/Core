using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(1)]
	public class M0001_InitialMigrationPg : Migration
	{
		public override void Up()
		{
			Execute.EmbeddedScript("Resgrid.Providers.MigrationsPg.Sql.M0001_InitialMigration.sql");
		}

		public override void Down()
		{
			
		}
	}
}
