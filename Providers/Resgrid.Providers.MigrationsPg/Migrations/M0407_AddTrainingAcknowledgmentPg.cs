using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0407_AddTrainingAcknowledgment: trainings.requiresacknowledgment. Guarded for safe retry.</summary>
	[Migration(407)]
	public class M0407_AddTrainingAcknowledgmentPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE IF EXISTS public.trainings ADD COLUMN IF NOT EXISTS requiresacknowledgment boolean NOT NULL DEFAULT false;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE IF EXISTS public.trainings DROP COLUMN IF EXISTS requiresacknowledgment;");
		}
	}
}
