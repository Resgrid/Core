using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0335: trainings gain opentoallmembers, so reference and optional material can stay readable by
	/// every member now that a training is otherwise seen only by its assignees and managers. Off by default.
	/// </summary>
	[Migration(335)]
	public class M0335_AddTrainingOpenToAllMembersPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE trainings ADD COLUMN IF NOT EXISTS opentoallmembers boolean NOT NULL DEFAULT false;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE trainings DROP COLUMN IF EXISTS opentoallmembers;");
		}
	}
}
