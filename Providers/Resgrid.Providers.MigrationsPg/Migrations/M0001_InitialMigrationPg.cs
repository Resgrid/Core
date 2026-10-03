using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(1)]
	public class M0001_InitialMigrationPg : Migration
	{
		public override void Up()
		{
			// The schema uses citext for user ids, names and e-mail; a fresh database failed here without it (GitHub #536).
			// citext is a trusted extension (PostgreSQL 13+), so the database owner can create it.
			Execute.Sql("CREATE EXTENSION IF NOT EXISTS citext;");
			Execute.EmbeddedScript("Resgrid.Providers.MigrationsPg.Sql.M0001_InitialMigration.sql");
		}

		public override void Down()
		{
			
		}
	}
}
