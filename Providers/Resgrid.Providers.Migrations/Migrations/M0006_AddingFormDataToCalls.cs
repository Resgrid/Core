using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(6)]
	public class M0006_AddingFormDataToCalls : Migration
	{
		public override void Up()
		{
			Alter.Table("Calls").AddColumn("CallFormData").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Calls").AddColumn("ContactId").AsInt32().Nullable();
		}

		public override void Down()
		{
			
		}
	}
}
