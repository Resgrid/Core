using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 9A: read and confirm. Trainings.RequiresAcknowledgment: a training without a quiz completes only when the member confirms they
	/// read and reviewed it (a yearly handbook or policy review with an end date), not on opening it. Baseline (free). Existing trainings
	/// keep completing on view. Existence-guarded for safe retry; Down removes exactly what Up added.
	/// </summary>
	[Migration(407)]
	public class M0407_AddTrainingAcknowledgment : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.Trainings', 'U') IS NOT NULL AND COL_LENGTH('dbo.Trainings', 'RequiresAcknowledgment') IS NULL
	ALTER TABLE [dbo].[Trainings] ADD [RequiresAcknowledgment] bit NOT NULL CONSTRAINT [DF_Trainings_RequiresAcknowledgment] DEFAULT (0);");
		}

		public override void Down()
		{
			Execute.Sql(@"
IF OBJECT_ID('DF_Trainings_RequiresAcknowledgment', 'D') IS NOT NULL ALTER TABLE [dbo].[Trainings] DROP CONSTRAINT [DF_Trainings_RequiresAcknowledgment];
IF COL_LENGTH('dbo.Trainings', 'RequiresAcknowledgment') IS NOT NULL ALTER TABLE [dbo].[Trainings] DROP COLUMN [RequiresAcknowledgment];");
		}
	}
}
