using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(1)]
	public class M0001_InitialMigration : Migration
	{
		public override void Up()
		{
			Execute.EmbeddedScript("Resgrid.Providers.Migrations.Sql.M0001_InitialMigration.sql");
		}

		public override void Down()
		{
			
		}
	}
}
