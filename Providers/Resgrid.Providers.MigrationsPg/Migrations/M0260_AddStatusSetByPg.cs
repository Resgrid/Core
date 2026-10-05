using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Records who submitted a unit state or personnel status and from where (see the SQL Server M0260). citext like the
	/// other user-id columns; no foreign key.
	/// </summary>
	[Migration(260)]
	public class M0260_AddStatusSetByPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE unitstates ADD COLUMN IF NOT EXISTS setbyuserid citext NULL;");
			Execute.Sql("ALTER TABLE unitstates ADD COLUMN IF NOT EXISTS setbyorigin integer NULL;");
			Execute.Sql("ALTER TABLE actionlogs ADD COLUMN IF NOT EXISTS setbyuserid citext NULL;");
			Execute.Sql("ALTER TABLE actionlogs ADD COLUMN IF NOT EXISTS setbyorigin integer NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE actionlogs DROP COLUMN IF EXISTS setbyorigin;");
			Execute.Sql("ALTER TABLE actionlogs DROP COLUMN IF EXISTS setbyuserid;");
			Execute.Sql("ALTER TABLE unitstates DROP COLUMN IF EXISTS setbyorigin;");
			Execute.Sql("ALTER TABLE unitstates DROP COLUMN IF EXISTS setbyuserid;");
		}
	}
}
