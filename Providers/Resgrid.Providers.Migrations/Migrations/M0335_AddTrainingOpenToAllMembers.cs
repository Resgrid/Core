using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Trainings gain OpenToAllMembers. A training is now seen only by its assignees and by whoever manages it (before,
	/// any member of the department could open any training by id). This flag is how a department keeps reference and
	/// optional material readable by everyone without assigning it to everyone. Off by default, so existing trainings
	/// follow the assigned-only rule. Guarded for safe retry.
	/// </summary>
	[Migration(335)]
	public class M0335_AddTrainingOpenToAllMembers : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('Trainings', 'OpenToAllMembers') IS NULL ALTER TABLE [Trainings] ADD [OpenToAllMembers] bit NOT NULL CONSTRAINT [DF_Trainings_OpenToAllMembers] DEFAULT 0;");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('DF_Trainings_OpenToAllMembers', 'D') IS NOT NULL ALTER TABLE [Trainings] DROP CONSTRAINT [DF_Trainings_OpenToAllMembers];");
			Execute.Sql("IF COL_LENGTH('Trainings', 'OpenToAllMembers') IS NOT NULL ALTER TABLE [Trainings] DROP COLUMN [OpenToAllMembers];");
		}
	}
}
