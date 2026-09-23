using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Records how a unit state's or personnel status's destination was decided (see the SQL Server M0228).
	/// </summary>
	[Migration(228)]
	public class M0228_AddStatusDestinationSourcePg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE unitstates ADD COLUMN IF NOT EXISTS destinationsource integer NULL;");
			Execute.Sql("ALTER TABLE actionlogs ADD COLUMN IF NOT EXISTS destinationsource integer NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE actionlogs DROP COLUMN IF EXISTS destinationsource;");
			Execute.Sql("ALTER TABLE unitstates DROP COLUMN IF EXISTS destinationsource;");
		}
	}
}
