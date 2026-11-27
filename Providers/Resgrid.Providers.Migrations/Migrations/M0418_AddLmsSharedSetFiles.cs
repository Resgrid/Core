using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 10 A3 follow-up (Training T5 course sets): LmsMediaAssets.SharedFromDepartmentId and LmsContentPackages.SharedFromDepartmentId
	/// (int NULL). A subscriber's copy of a publisher's hosted video or SCORM/cmi5 package is its own row over the publisher's stored objects;
	/// this names the department whose objects it shares, so worker 88 never archives it for the subscriber's own lapse and a store purge never
	/// deletes objects another department still names. Null for a department's own uploads. Placeholder number (renumbered at merge).
	/// Existence-guarded; Down removes exactly what Up added.
	/// </summary>
	[Migration(418)]
	public class M0418_AddLmsSharedSetFiles : Migration
	{
		private static readonly string[] Tables = { "LmsMediaAssets", "LmsContentPackages" };

		public override void Up()
		{
			foreach (var table in Tables)
				Execute.Sql($@"
IF OBJECT_ID('dbo.{table}', 'U') IS NOT NULL AND COL_LENGTH('dbo.{table}', 'SharedFromDepartmentId') IS NULL
	ALTER TABLE [dbo].[{table}] ADD [SharedFromDepartmentId] int NULL;");
		}

		public override void Down()
		{
			foreach (var table in Tables)
				Execute.Sql($@"
IF COL_LENGTH('dbo.{table}', 'SharedFromDepartmentId') IS NOT NULL ALTER TABLE [dbo].[{table}] DROP COLUMN [SharedFromDepartmentId];");
		}
	}
}
