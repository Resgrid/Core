using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Trainings gain ExcludeFromAi (K1, decision 14: the per-item "no AI" flag that DispatchProtocols already has), so a training holding
	/// licensed material whose license forbids processing is never indexed for knowledge answers. Off by default. Guarded for safe retry.
	/// </summary>
	[Migration(370)]
	public class M0370_AddTrainingExcludeFromAi : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('Trainings', 'ExcludeFromAi') IS NULL ALTER TABLE [Trainings] ADD [ExcludeFromAi] bit NOT NULL CONSTRAINT [DF_Trainings_ExcludeFromAi] DEFAULT 0;");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('DF_Trainings_ExcludeFromAi', 'D') IS NOT NULL ALTER TABLE [Trainings] DROP CONSTRAINT [DF_Trainings_ExcludeFromAi];");
			Execute.Sql("IF COL_LENGTH('Trainings', 'ExcludeFromAi') IS NOT NULL ALTER TABLE [Trainings] DROP COLUMN [ExcludeFromAi];");
		}
	}
}
