using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(8)]
	public class M0008_AddingAddonsInvoices : Migration
	{
		public override void Up()
		{
			// Adding in Invoice limits

			// Enterprise Plus Plan Invoice Limits
			Insert.IntoTable("PlanLimits".ToLower()).Row(new { planlimitid = 5, planid = 1, limittype = 5, limitvalue = 100000 });

			// Finish Adding in Invoice limits

			Create.Table("PlanAddons".ToLower())
			   .WithColumn("PlanAddonId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("PlanId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("AddonType".ToLower()).AsInt32().NotNullable()
			   .WithColumn("Cost".ToLower()).AsDecimal().NotNullable()
			   .WithColumn("ExternalId".ToLower()).AsCustom("citext");

			Create.ForeignKey("FK_PlanAddons_Plans")
				.FromTable("PlanAddons".ToLower()).ForeignColumn("PlanId".ToLower())
				.ToTable("Plans".ToLower()).PrimaryColumn("PlanId".ToLower());

			// Standard Plan PTT Addon
			Insert.IntoTable("PlanAddons".ToLower()).Row(new { planaddonid = "456ed5d4-57e1-4882-b433-1d3cc239103d", planid = 1, addontype = 1, cost = 0, externalid = "" });

			Create.Table("PaymentAddons".ToLower())
			   .WithColumn("PaymentAddonId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("PlanAddonId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("PurchaseOn".ToLower()).AsDateTime2().NotNullable()
			   .WithColumn("EffectiveOn".ToLower()).AsDateTime2().NotNullable()
			   .WithColumn("EndingOn".ToLower()).AsDateTime2().NotNullable()
			   .WithColumn("Amount".ToLower()).AsDecimal().NotNullable()
			   .WithColumn("Description".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("TransactionId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("SubscriptionId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("Data".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("IsCancelled".ToLower()).AsBoolean().Nullable()
			   .WithColumn("CancelledOn".ToLower()).AsDateTime2().Nullable()
			   .WithColumn("CancelledData".ToLower()).AsCustom("citext").Nullable();

			Create.ForeignKey("FK_PaymentAddons_Departments")
				.FromTable("PaymentAddons".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());

			Create.ForeignKey("FK_PaymentAddons_PlanAddons")
				.FromTable("PaymentAddons".ToLower()).ForeignColumn("PlanAddonId".ToLower())
				.ToTable("PlanAddons".ToLower()).PrimaryColumn("PlanAddonId".ToLower());
		}

		public override void Down()
		{

		}
	}
}
