using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(6)]
	public class M0006_AddingFormDataToCalls : Migration
	{
		public override void Up()
		{
			Alter.Table("Calls".ToLower()).AddColumn("CallFormData".ToLower()).AsCustom("citext").Nullable();
			Alter.Table("Calls".ToLower()).AddColumn("ContactId".ToLower()).AsInt32().Nullable();
		}

		public override void Down()
		{
			
		}
	}
}
