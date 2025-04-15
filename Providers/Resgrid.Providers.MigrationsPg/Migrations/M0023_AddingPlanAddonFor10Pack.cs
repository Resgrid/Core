using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(23)]
	public class M0023_AddingPlanAddonFor10Pack : Migration
	{
		public override void Up()
		{
			if (Schema.Table("PlanAddons".ToLower()).Constraint("FK_PlanAddons_Plans").Exists())
			{
				Delete.ForeignKey("FK_PlanAddons_Plans").OnTable("PlanAddons".ToLower());
			}

			Alter.Table("PlanAddons".ToLower()).AlterColumn("PlanId".ToLower()).AsInt32().Nullable();
			Alter.Table("PlanAddons".ToLower()).AddColumn("TestExternalId".ToLower()).AsCustom("citext").Nullable();
			Insert.IntoTable("PlanAddons".ToLower()).Row(new { planaddonid = "6f4c5f8b-584d-4291-8a7d-29bf97ae6aa9", addontype = 1, cost = 35, externalid = "price_0N7MM5qJFDZJcnkVZy4Z51IC", testexternalid = "price_0NLHvaqJFDZJcnkVS3DHnRA8" });
		}

		public override void Down()
		{

		}
	}
}
