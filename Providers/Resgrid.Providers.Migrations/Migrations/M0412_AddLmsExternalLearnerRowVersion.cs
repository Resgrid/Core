using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Security review follow-up (2026-11-25): LmsExternalLearners.RowVersion. Every write of a learner row is a compare-and-set on it, so a
	/// stale whole-row save can never write back an old security stamp, password hash or status. Existence-guarded for safe retry; Down
	/// removes exactly what Up added.
	/// </summary>
	[Migration(412)]
	public class M0412_AddLmsExternalLearnerRowVersion : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsExternalLearners', 'U') IS NOT NULL AND COL_LENGTH('dbo.LmsExternalLearners', 'RowVersion') IS NULL
	ALTER TABLE [dbo].[LmsExternalLearners] ADD [RowVersion] bigint NOT NULL CONSTRAINT [DF_LmsExternalLearners_RowVersion] DEFAULT (0);");
		}

		public override void Down()
		{
			Execute.Sql(@"
IF OBJECT_ID('DF_LmsExternalLearners_RowVersion', 'D') IS NOT NULL ALTER TABLE [dbo].[LmsExternalLearners] DROP CONSTRAINT [DF_LmsExternalLearners_RowVersion];
IF COL_LENGTH('dbo.LmsExternalLearners', 'RowVersion') IS NOT NULL ALTER TABLE [dbo].[LmsExternalLearners] DROP COLUMN [RowVersion];");
		}
	}
}
