using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0370: trainings gain excludefromai (never indexed for knowledge answers). Off by default.</summary>
	[Migration(370)]
	public class M0370_AddTrainingExcludeFromAiPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE trainings ADD COLUMN IF NOT EXISTS excludefromai boolean NOT NULL DEFAULT false;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE trainings DROP COLUMN IF EXISTS excludefromai;");
		}
	}
}
