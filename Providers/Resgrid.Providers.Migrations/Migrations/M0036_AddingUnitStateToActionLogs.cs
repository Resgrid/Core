using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(36)]
	public class M0036_AddingUnitStateToActionLogs : Migration
	{
		public override void Up()
		{
			Alter.Table("ActionLogs").AddColumn("UnitStateId").AsInt32().Nullable();
			Alter.Table("ActionLogs").AddColumn("UnitName").AsString().Nullable();
		}

		public override void Down()
		{

		}
	}
}
