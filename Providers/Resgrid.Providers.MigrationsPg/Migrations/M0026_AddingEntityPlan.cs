using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(26)]
	public class M0026_AddingEntityPlan : Migration
	{
		public override void Up()
		{
			Alter.Table("Plans".ToLower()).AddColumn("TestExternalId".ToLower()).AsCustom("citext").Nullable();
			Alter.Table("Payments".ToLower()).AddColumn("Quantity".ToLower()).AsInt64().NotNullable().WithDefaultValue(1);

			Insert.IntoTable("Plans".ToLower()).Row(new { planid = 36, name = "Entity", cost = 0, frequency = 3, externalid = "price_0OLxxPqJFDZJcnkVDRHyMUYb", testexternalid = "price_0OJ0FsqJFDZJcnkVfE87UKr6" });
			Insert.IntoTable("Plans".ToLower()).Row(new { planid = 37, name = "Entity Monthly", cost = 0, frequency = 2, externalid = "price_0OLxxPqJFDZJcnkVfJLISCp4", testexternalid = "price_0OIzLPqJFDZJcnkVRn2tpwFF" });


			Insert.IntoTable("PlanLimits".ToLower()).Row(new { planlimitid = 6, planid = 36, limittype = 6, limitvalue = 10 }); // Entities Per Pack
			Insert.IntoTable("PlanLimits".ToLower()).Row(new { planlimitid = 7, planid = 37, limittype = 6, limitvalue = 10 }); // Entities Per Pack

			Insert.IntoTable("PlanAddons".ToLower()).Row(new { planaddonid = "04b6db64-7dbf-4b3d-a1d1-60bffde004b4", planid = 36, addontype = 1, cost = 30000, externalid = "price_0NMZrMqJFDZJcnkVTpb07W60", testexternalid = "price_0NLHvaqJFDZJcnkVS3DHnRA8" });
			Insert.IntoTable("PlanAddons".ToLower()).Row(new { planaddonid = "8f730f54-2574-445e-875a-5819f8bcda7a", planid = 37, addontype = 1, cost = 3000, externalid = "price_0NMZrMqJFDZJcnkVTpb07W60", testexternalid = "price_0NLHvaqJFDZJcnkVS3DHnRA8" });
		}

		public override void Down()
		{

		}
	}
}
