using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T5 standards (round 6C): the lesson columns of the Scorm, Cmi5 and LtiLink lesson kinds on LmsLessons (M0349): the content
	/// package played, the LTI tool launched, the lesson's own target link URI and custom parameters. All nullable; guarded per column for a
	/// safe retry; Down drops them.
	/// </summary>
	[Migration(394)]
	public class M0394_AddLmsLessonStandardsColumns : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsLessons', 'U') IS NOT NULL AND COL_LENGTH('dbo.LmsLessons', 'LmsContentPackageId') IS NULL ALTER TABLE [dbo].[LmsLessons] ADD [LmsContentPackageId] int NULL;");
			Execute.Sql("IF OBJECT_ID('dbo.LmsLessons', 'U') IS NOT NULL AND COL_LENGTH('dbo.LmsLessons', 'LmsLtiToolId') IS NULL ALTER TABLE [dbo].[LmsLessons] ADD [LmsLtiToolId] int NULL;");
			Execute.Sql("IF OBJECT_ID('dbo.LmsLessons', 'U') IS NOT NULL AND COL_LENGTH('dbo.LmsLessons', 'LtiTargetLinkUri') IS NULL ALTER TABLE [dbo].[LmsLessons] ADD [LtiTargetLinkUri] nvarchar(2000) NULL;");
			Execute.Sql("IF OBJECT_ID('dbo.LmsLessons', 'U') IS NOT NULL AND COL_LENGTH('dbo.LmsLessons', 'LtiCustomParameters') IS NULL ALTER TABLE [dbo].[LmsLessons] ADD [LtiCustomParameters] nvarchar(4000) NULL;");
		}

		public override void Down()
		{
			foreach (var column in new[] { "LtiCustomParameters", "LtiTargetLinkUri", "LmsLtiToolId", "LmsContentPackageId" })
				Execute.Sql($"IF COL_LENGTH('dbo.LmsLessons', '{column}') IS NOT NULL ALTER TABLE [dbo].[LmsLessons] DROP COLUMN [{column}];");
		}
	}
}
