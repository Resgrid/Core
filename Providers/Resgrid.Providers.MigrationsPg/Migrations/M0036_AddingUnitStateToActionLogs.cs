using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.MigrationsPg
{
	[Migration(36)]
	public class M0036_AddingUnitStateToActionLogs : Migration
	{
		public override void Up()
		{
			Alter.Table("ActionLogs".ToLower()).AddColumn("UnitStateId".ToLower()).AsInt32().Nullable();
			Alter.Table("ActionLogs".ToLower()).AddColumn("UnitName".ToLower()).AsCustom("citext").Nullable();
		}

		public override void Down()
		{

		}
	}
}
