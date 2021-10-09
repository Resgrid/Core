using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(7)]
	public class M0007_AddingDispatchOnToCalls : Migration
	{
		public override void Up()
		{
			Alter.Table("Calls").AddColumn("DispatchOn").AsDateTime2().Nullable();
			Alter.Table("Calls").AddColumn("HasBeenDispatched").AsBoolean().Nullable();
		}

		public override void Down()
		{
			
		}
	}
}
