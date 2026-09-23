using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Records how a unit state's or personnel status's destination was decided (StatusDestinationSources): chosen by the
	/// client, or attributed by the server when the status arrived without one (carried forward from the previous status's
	/// still-open call, the one open call the unit/person is dispatched to, or the unit a person is riding). Null on rows
	/// written before this migration.
	/// </summary>
	[Migration(228)]
	public class M0228_AddStatusDestinationSource : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('UnitStates', 'DestinationSource') IS NULL ALTER TABLE [UnitStates] ADD [DestinationSource] int NULL;");
			Execute.Sql("IF COL_LENGTH('ActionLogs', 'DestinationSource') IS NULL ALTER TABLE [ActionLogs] ADD [DestinationSource] int NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF COL_LENGTH('ActionLogs', 'DestinationSource') IS NOT NULL ALTER TABLE [ActionLogs] DROP COLUMN [DestinationSource];");
			Execute.Sql("IF COL_LENGTH('UnitStates', 'DestinationSource') IS NOT NULL ALTER TABLE [UnitStates] DROP COLUMN [DestinationSource];");
		}
	}
}
