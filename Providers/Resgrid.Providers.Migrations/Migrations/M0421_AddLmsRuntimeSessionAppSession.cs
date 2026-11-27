using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 10 A8 (placeholder number, renumbered at merge): LmsRuntimeSessions.AppSessionId, the in-app launch session (a row of the same
	/// table, Kind AppSession) that minted a content session or an LTI hint; null for a web launch. A filtered index finds the newest session an
	/// app session minted (the launch page's state). No foreign key (§17.3). Existence-guarded for safe retry; Down removes exactly what Up added.
	/// </summary>
	[Migration(421)]
	public class M0421_AddLmsRuntimeSessionAppSession : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsRuntimeSessions', 'U') IS NOT NULL AND COL_LENGTH('dbo.LmsRuntimeSessions', 'AppSessionId') IS NULL
	ALTER TABLE [dbo].[LmsRuntimeSessions] ADD [AppSessionId] int NULL;");
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsRuntimeSessions', 'U') IS NOT NULL AND COL_LENGTH('dbo.LmsRuntimeSessions', 'AppSessionId') IS NOT NULL
	AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsRuntimeSessions_AppSession' AND object_id = OBJECT_ID('dbo.LmsRuntimeSessions'))
	EXEC('CREATE INDEX [IX_LmsRuntimeSessions_AppSession] ON [dbo].[LmsRuntimeSessions] ([AppSessionId], [LmsRuntimeSessionId]) WHERE [AppSessionId] IS NOT NULL');");
		}

		public override void Down()
		{
			Execute.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsRuntimeSessions_AppSession' AND object_id = OBJECT_ID('dbo.LmsRuntimeSessions'))
	DROP INDEX [IX_LmsRuntimeSessions_AppSession] ON [dbo].[LmsRuntimeSessions];
IF COL_LENGTH('dbo.LmsRuntimeSessions', 'AppSessionId') IS NOT NULL ALTER TABLE [dbo].[LmsRuntimeSessions] DROP COLUMN [AppSessionId];");
		}
	}
}
