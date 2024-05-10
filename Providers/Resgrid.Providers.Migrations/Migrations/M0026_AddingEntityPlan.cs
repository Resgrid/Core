using FluentMigrator;
using FluentMigrator.SqlServer;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(26)]
	public class M0026_AddingEntityPlan : Migration
	{
		public override void Up()
		{
			Alter.Table("Plans").AddColumn("TestExternalId").AsString(256).Nullable();
			Alter.Table("Payments").AddColumn("Quantity").AsInt64().NotNullable().WithDefaultValue(1);

			Insert.IntoTable("Plans").WithIdentityInsert().Row(new { PlanId = 36, Name = "Entity", Cost = 0, Frequency = 3, ExternalId = "price_0OLxxPqJFDZJcnkVDRHyMUYb", TestExternalId = "price_0OJ0FsqJFDZJcnkVfE87UKr6" });
			Insert.IntoTable("Plans").WithIdentityInsert().Row(new { PlanId = 37, Name = "Entity Monthly", Cost = 0, Frequency = 2, ExternalId = "price_0OLxxPqJFDZJcnkVfJLISCp4", TestExternalId = "price_0OIzLPqJFDZJcnkVRn2tpwFF" });


			Insert.IntoTable("PlanLimits").Row(new { PlanId = 36, LimitType = 6, LimitValue = 10 }); // Entities Per Pack
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 37, LimitType = 6, LimitValue = 10 }); // Entities Per Pack

			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "04b6db64-7dbf-4b3d-a1d1-60bffde004b4", PlanId = 36, AddonType = 1, Cost = 30000, ExternalId = "price_0NMZrMqJFDZJcnkVTpb07W60", TestExternalId = "price_0NLHvaqJFDZJcnkVS3DHnRA8" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "8f730f54-2574-445e-875a-5819f8bcda7a", PlanId = 37, AddonType = 1, Cost = 3000, ExternalId = "price_0NMZrMqJFDZJcnkVTpb07W60", TestExternalId = "price_0NLHvaqJFDZJcnkVS3DHnRA8" });
		}

		public override void Down()
		{

		}
	}
}
