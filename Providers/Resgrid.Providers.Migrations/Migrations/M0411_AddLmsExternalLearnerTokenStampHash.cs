using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Security review M1 (2026-11-25): LmsExternalLearnerTokens.StampHash, the hash of the learner's security stamp a token was issued under.
	/// An e-mailed token (verification, invitation, password reset) is refused once the stamp has changed. Existence-guarded for safe retry;
	/// Down removes exactly what Up added.
	/// </summary>
	[Migration(411)]
	public class M0411_AddLmsExternalLearnerTokenStampHash : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsExternalLearnerTokens', 'U') IS NOT NULL AND COL_LENGTH('dbo.LmsExternalLearnerTokens', 'StampHash') IS NULL ALTER TABLE [dbo].[LmsExternalLearnerTokens] ADD [StampHash] nvarchar(64) NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF COL_LENGTH('dbo.LmsExternalLearnerTokens', 'StampHash') IS NOT NULL ALTER TABLE [dbo].[LmsExternalLearnerTokens] DROP COLUMN [StampHash];");
		}
	}
}
