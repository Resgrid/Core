using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(7)]
	public class M0007_AddingDispatchOnToCalls : Migration
	{
		public override void Up()
		{
			Alter.Table("Calls".ToLower()).AddColumn("DispatchOn".ToLower()).AsDateTime2().Nullable();
			Alter.Table("Calls".ToLower()).AddColumn("HasBeenDispatched".ToLower()).AsBoolean().Nullable();
		}

		public override void Down()
		{
			
		}
	}
}
