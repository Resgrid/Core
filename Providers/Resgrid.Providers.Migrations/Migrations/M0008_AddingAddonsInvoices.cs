using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(8)]
	public class M0008_AddingAddonsInvoices : Migration
	{
		public override void Up()
		{
			// Adding in Invoice limits

			// Enterprise Plus Plan Invoice Limits
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 1, LimitType = 5, LimitValue = 100000 });

			// Finish Adding in Invoice limits

			Create.Table("PlanAddons")
			   .WithColumn("PlanAddonId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("PlanId").AsInt32().NotNullable()
			   .WithColumn("AddonType").AsInt32().NotNullable()
			   .WithColumn("Cost").AsDecimal().NotNullable()
			   .WithColumn("ExternalId").AsString(256);

			Create.ForeignKey("FK_PlanAddons_Plans")
				.FromTable("PlanAddons").ForeignColumn("PlanId")
				.ToTable("Plans").PrimaryColumn("PlanId");

			// Standard Plan PTT Addon
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "456ed5d4-57e1-4882-b433-1d3cc239103d", PlanId = 1, AddonType = 1, Cost = 0, ExternalId = "" });

			Create.Table("PaymentAddons")
			   .WithColumn("PaymentAddonId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("PlanAddonId").AsString(128).NotNullable()
			   .WithColumn("PurchaseOn").AsDateTime2().NotNullable()
			   .WithColumn("EffectiveOn").AsDateTime2().NotNullable()
			   .WithColumn("EndingOn").AsDateTime2().NotNullable()
			   .WithColumn("Amount").AsDecimal().NotNullable()
			   .WithColumn("Description").AsString(Int32.MaxValue).NotNullable()
			   .WithColumn("TransactionId").AsString(Int32.MaxValue).NotNullable()
			   .WithColumn("SubscriptionId").AsString(Int32.MaxValue).NotNullable()
			   .WithColumn("Data").AsString(Int32.MaxValue).NotNullable()
			   .WithColumn("IsCancelled").AsBoolean().Nullable()
			   .WithColumn("CancelledOn").AsDateTime2().Nullable()
			   .WithColumn("CancelledData").AsString(Int32.MaxValue).Nullable();

			Create.ForeignKey("FK_PaymentAddons_Departments")
				.FromTable("PaymentAddons").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.ForeignKey("FK_PaymentAddons_PlanAddons")
				.FromTable("PaymentAddons").ForeignColumn("PlanAddonId")
				.ToTable("PlanAddons").PrimaryColumn("PlanAddonId");
		}

		public override void Down()
		{

		}
	}
}
