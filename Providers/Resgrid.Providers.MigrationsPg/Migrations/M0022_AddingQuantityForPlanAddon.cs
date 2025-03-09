using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(22)]
	public class M0022_AddingQuantityForPlanAddon : Migration
	{
		public override void Up()
		{
			Alter.Table("PaymentAddons".ToLower()).AddColumn("Quantity".ToLower()).AsInt64().NotNullable().WithDefaultValue(1);
		}

		public override void Down()
		{

		}
	}
}
