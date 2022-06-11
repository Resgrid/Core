using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(12)]
	public class M0012_UpdatingUnitCallTypes : Migration
	{
		public override void Up()
		{
			Alter.Table("UnitTypes").AddColumn("MapIconType").AsInt32().Nullable();
			Alter.Table("CallTypes").AddColumn("MapIconType").AsInt32().Nullable();
		}

		public override void Down()
		{

		}
	}
}
