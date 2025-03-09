using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(12)]
	public class M0012_UpdatingUnitCallTypes : Migration
	{
		public override void Up()
		{
			Alter.Table("UnitTypes".ToLower()).AddColumn("MapIconType".ToLower()).AsInt32().Nullable();
			Alter.Table("CallTypes".ToLower()).AddColumn("MapIconType".ToLower()).AsInt32().Nullable();
		}

		public override void Down()
		{

		}
	}
}
