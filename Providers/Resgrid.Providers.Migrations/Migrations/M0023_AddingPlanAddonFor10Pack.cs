using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(23)]
	public class M0023_AddingPlanAddonFor10Pack : Migration
	{
		public override void Up()
		{
			if (Schema.Table("PlanAddons").Constraint("FK_PlanAddons_Plans").Exists())
			{
				Delete.ForeignKey("FK_PlanAddons_Plans").OnTable("PlanAddons");
			}

			Alter.Table("PlanAddons").AlterColumn("PlanId").AsInt32().Nullable();
			Alter.Table("PlanAddons").AddColumn("TestExternalId").AsString(256).Nullable();
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "6f4c5f8b-584d-4291-8a7d-29bf97ae6aa9", AddonType = 1, Cost = 35, ExternalId = "price_0N7MM5qJFDZJcnkVZy4Z51IC", TestExternalId = "price_0NLHvaqJFDZJcnkVS3DHnRA8" });
		}

		public override void Down()
		{

		}
	}
}
