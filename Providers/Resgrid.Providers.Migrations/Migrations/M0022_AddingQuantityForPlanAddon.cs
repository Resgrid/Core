using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(22)]
	public class M0022_AddingQuantityForPlanAddon : Migration
	{
		public override void Up()
		{
			Alter.Table("PaymentAddons").AddColumn("Quantity").AsInt64().NotNullable().WithDefaultValue(1);
		}

		public override void Down()
		{

		}
	}
}
