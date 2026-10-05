using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Records who submitted a unit state or personnel status and from where (StatusSetOrigins: the website, one of the
	/// apps, SMS, voice, chat, or a dispatch/schedule/maintenance automation), so a report author can tell a crew's own
	/// status from one a dispatcher or an incident commander set for them. No foreign key: the column is provenance and
	/// must survive the member leaving. Null on rows written before this migration.
	/// </summary>
	[Migration(260)]
	public class M0260_AddStatusSetBy : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('UnitStates', 'SetByUserId') IS NULL ALTER TABLE [UnitStates] ADD [SetByUserId] nvarchar(128) NULL;");
			Execute.Sql("IF COL_LENGTH('UnitStates', 'SetByOrigin') IS NULL ALTER TABLE [UnitStates] ADD [SetByOrigin] int NULL;");
			Execute.Sql("IF COL_LENGTH('ActionLogs', 'SetByUserId') IS NULL ALTER TABLE [ActionLogs] ADD [SetByUserId] nvarchar(128) NULL;");
			Execute.Sql("IF COL_LENGTH('ActionLogs', 'SetByOrigin') IS NULL ALTER TABLE [ActionLogs] ADD [SetByOrigin] int NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF COL_LENGTH('ActionLogs', 'SetByOrigin') IS NOT NULL ALTER TABLE [ActionLogs] DROP COLUMN [SetByOrigin];");
			Execute.Sql("IF COL_LENGTH('ActionLogs', 'SetByUserId') IS NOT NULL ALTER TABLE [ActionLogs] DROP COLUMN [SetByUserId];");
			Execute.Sql("IF COL_LENGTH('UnitStates', 'SetByOrigin') IS NOT NULL ALTER TABLE [UnitStates] DROP COLUMN [SetByOrigin];");
			Execute.Sql("IF COL_LENGTH('UnitStates', 'SetByUserId') IS NOT NULL ALTER TABLE [UnitStates] DROP COLUMN [SetByUserId];");
		}
	}
}
