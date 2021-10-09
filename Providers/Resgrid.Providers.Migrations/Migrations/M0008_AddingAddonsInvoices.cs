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

			// Professional Plan Invoice Limits
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 4, LimitType = 5, LimitValue = 25 });
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 14, LimitType = 5, LimitValue = 25 });
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 26, LimitType = 5, LimitValue = 25 });
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 27, LimitType = 5, LimitValue = 25 });

			// Ultimate Plan Invoice Limits
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 5, LimitType = 5, LimitValue = 100 });
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 15, LimitType = 5, LimitValue = 100 });
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 28, LimitType = 5, LimitValue = 100 });
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 29, LimitType = 5, LimitValue = 100 });

			// Enterprise Plan Invoice Limits
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 10, LimitType = 5, LimitValue = 500 });
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 16, LimitType = 5, LimitValue = 500 });
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 30, LimitType = 5, LimitValue = 500 });
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 31, LimitType = 5, LimitValue = 500 });

			// Enterprise Plus Plan Invoice Limits
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 17, LimitType = 5, LimitValue = 1000 });
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 18, LimitType = 5, LimitValue = 1000 });
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 32, LimitType = 5, LimitValue = 1000 });
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 33, LimitType = 5, LimitValue = 1000 });

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
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "456ed5d4-57e1-4882-b433-1d3cc239103d", PlanId = 2, AddonType = 1, Cost = 720, ExternalId = "price_0JM1UOqJFDZJcnkVqapi0eSP" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "fcf1e43d-6a6a-4838-9ff1-583fad000a8e", PlanId = 12, AddonType = 1, Cost = 60, ExternalId = "price_0JM1UOqJFDZJcnkVUh1Ea1vw" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "92f8f336-e119-49db-b9e4-0a3d6b8c46ee", PlanId = 22, AddonType = 1, Cost = 720, ExternalId = "price_0JM1UOqJFDZJcnkVqapi0eSP" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "a61f422a-e070-4d1c-a651-ccbd9a753a89", PlanId = 23, AddonType = 1, Cost = 60, ExternalId = "price_0JM1UOqJFDZJcnkVUh1Ea1vw" });

			// Premium Plan PTT Addon
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "64b27c13-bcb8-4182-af81-6a0cf0cd2510", PlanId = 3, AddonType = 1, Cost = 1260, ExternalId = "price_0JM1X1qJFDZJcnkVgqfTR9ab" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "57d312ff-39c8-488b-b47e-e02d2c59dd82", PlanId = 13, AddonType = 1, Cost = 105, ExternalId = "price_0JM1X1qJFDZJcnkVPiMqSDA2" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "354ecd46-a2b0-469b-92f8-d5a655d048ee", PlanId = 24, AddonType = 1, Cost = 1260, ExternalId = "price_0JM1X1qJFDZJcnkVgqfTR9ab" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "3f81d7b5-fec6-4699-8654-8b15253c33ea", PlanId = 25, AddonType = 1, Cost = 105, ExternalId = "price_0JM1X1qJFDZJcnkVPiMqSDA2" });

			// Professional Plan PTT Addon
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "089a59f4-6ea6-4041-a403-1eeff1648aca", PlanId = 4, AddonType = 1, Cost = 1800, ExternalId = "price_0JM1ZVqJFDZJcnkVj7pDGZdb" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "757162dc-c739-4603-9dd1-0b252cf90bc6", PlanId = 14, AddonType = 1, Cost = 150, ExternalId = "price_0JM1ZVqJFDZJcnkVAqWN59jw" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "c95768b1-1c41-4be3-be0b-9703dbcd144c", PlanId = 26, AddonType = 1, Cost = 1800, ExternalId = "price_0JM1ZVqJFDZJcnkVj7pDGZdb" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "7b380fd6-c135-47c2-a4db-742b934238fb", PlanId = 27, AddonType = 1, Cost = 150, ExternalId = "price_0JM1ZVqJFDZJcnkVAqWN59jw" });

			// Ultimate Plan PTT Addon
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "b2e03d86-c1d6-451b-aa14-5f4fda3afeea", PlanId = 5, AddonType = 1, Cost = 3600, ExternalId = "price_0JM1boqJFDZJcnkVY4XA6lpp" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "5af985b0-9fb0-43cc-8b25-5f744e600082", PlanId = 15, AddonType = 1, Cost = 300, ExternalId = "price_0JM1boqJFDZJcnkVD02ZjzS9" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "c059b24d-55fb-4757-b94b-d9a803e3c38f", PlanId = 28, AddonType = 1, Cost = 3600, ExternalId = "price_0JM1boqJFDZJcnkVY4XA6lpp" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "a7311eb0-ca12-405f-b83c-c0303f821817", PlanId = 29, AddonType = 1, Cost = 300, ExternalId = "price_0JM1boqJFDZJcnkVD02ZjzS9" });

			// Enterprise Plan PTT Addon
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "f01ac2c4-cdc5-413b-9b9c-5c8c97473bbe", PlanId = 10, AddonType = 1, Cost = 9000, ExternalId = "price_0JM1dvqJFDZJcnkVFv8Ovv1q" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "b24c129c-1491-45d6-8512-8d54e5e132ed", PlanId = 16, AddonType = 1, Cost = 750, ExternalId = "price_0JM1dvqJFDZJcnkVS40BO336" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "8f265e59-4938-4f68-a9f1-d81cc00374f7", PlanId = 30, AddonType = 1, Cost = 9000, ExternalId = "price_0JM1dvqJFDZJcnkVFv8Ovv1q" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "b9fce848-c1a0-498d-bf06-74a5f1c180be", PlanId = 31, AddonType = 1, Cost = 750, ExternalId = "price_0JM1dvqJFDZJcnkVS40BO336" });

			// Enterprise Plus Plan PTT Addon
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "8ff7d107-6c7a-4ae1-ab2d-77413d53fdd5", PlanId = 17, AddonType = 1, Cost = 18000, ExternalId = "price_0JM1g6qJFDZJcnkVUAxrZ0Io" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "67bfdeb9-7fb0-4bb9-bd7c-f3bb0a4c2f71", PlanId = 18, AddonType = 1, Cost = 1500, ExternalId = "price_0JM1g6qJFDZJcnkVOjx14SR9" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "0541b4ec-11dc-457c-96a6-69c4a2f4562f", PlanId = 32, AddonType = 1, Cost = 18000, ExternalId = "price_0JM1g6qJFDZJcnkVUAxrZ0Io" });
			Insert.IntoTable("PlanAddons").Row(new { PlanAddonId = "fab72435-1bc0-46e5-b197-18c3441706cd", PlanId = 33, AddonType = 1, Cost = 1500, ExternalId = "price_0JM1g6qJFDZJcnkVOjx14SR9" });

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
