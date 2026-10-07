using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Units become soft deleted (see the SQL Server M0262). citext like the other user-id columns; no foreign key.
	/// </summary>
	[Migration(262)]
	public class M0262_AddUnitSoftDeletePg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE units ADD COLUMN IF NOT EXISTS isdeleted boolean NOT NULL DEFAULT false;");
			Execute.Sql("ALTER TABLE units ADD COLUMN IF NOT EXISTS deletedon timestamp without time zone NULL;");
			Execute.Sql("ALTER TABLE units ADD COLUMN IF NOT EXISTS deletedbyuserid citext NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE units DROP COLUMN IF EXISTS deletedbyuserid;");
			Execute.Sql("ALTER TABLE units DROP COLUMN IF EXISTS deletedon;");
			Execute.Sql("ALTER TABLE units DROP COLUMN IF EXISTS isdeleted;");
		}
	}
}
