using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// The Workforce Pro add-on (shifts-scheduling-modernization-plan.md §7, D23; owner 2026-11-26: member wellness and workplace exposure are
	/// Workforce Pro), built dark:
	/// <list type="bullet">
	/// <item>the PlanAddons catalog row for PlanAddonTypes 8 (WorkforceProAddonConfig.PlanAddonId; Cost 119 = the first pack's USD price). Its
	/// Stripe graduated price and Paddle prices do not exist yet, so ExternalId and TestExternalId stay empty and the Billing API's checkout
	/// fails closed until the owner sets them (the LMS row's M0351 → M0420 path);</item>
	/// <item>the Workforce.Pro operator flag, seeded off (like Training.Lms in M0346): while it is off wellness is invisible and its data kept;</item>
	/// <item>WorkforceProSeats: one row per member a manager switched Workforce Pro on for, unique per (department, member). No foreign keys;
	/// UnlinkedDepartmentCleanup removes the rows with the department.</item>
	/// </list>
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(422)]
	public class M0422_AddWorkforceProAddon : Migration
	{
		public const string WorkforceProAddonId = "65516545-bf65-47ce-9e3a-abaf18409d88";
		public const string FlagKey = "Workforce.Pro";

		public override void Up()
		{
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [PlanAddons] WHERE [PlanAddonId] = '" + WorkforceProAddonId + "' OR [AddonType] = 8) " +
				"INSERT INTO [PlanAddons] ([PlanAddonId], [AddonType], [Cost], [ExternalId], [TestExternalId]) " +
				"VALUES ('" + WorkforceProAddonId + "', 8, 119, '', '');");

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = '" + FlagKey + "') " +
				"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) " +
				"VALUES ('" + FlagKey + "', 'Workforce Pro add-on', 'Workforce Pro add-on operator rollout and kill switch. Off: member wellness and workplace exposure (exposure log, exposure cases, injuries, medical clearances, their notices and sweeps) are hidden and their routes return 404; the data is kept. The purchase is PlanAddonTypes.WorkforcePro. Seeded off.', 'Workforce', 0);");

			if (!Schema.Table("WorkforceProSeats").Exists())
			{
				Create.Table("WorkforceProSeats")
					.WithColumn("WorkforceProSeatId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("EnabledOn").AsDateTime2().NotNullable()
					.WithColumn("EnabledByUserId").AsString(128).Nullable();
			}

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_WorkforceProSeats_Department_User' AND object_id = OBJECT_ID('dbo.WorkforceProSeats')) " +
				"CREATE UNIQUE INDEX [UX_WorkforceProSeats_Department_User] ON [dbo].[WorkforceProSeats] ([DepartmentId], [UserId]);");
		}

		public override void Down()
		{
			if (Schema.Table("WorkforceProSeats").Exists())
				Delete.Table("WorkforceProSeats");

			Execute.Sql("DELETE FROM [FeatureFlags] WHERE [FlagKey] = '" + FlagKey + "';");

			// The catalog row goes only when nothing was ever bought on it.
			Execute.Sql(
				"DELETE FROM [PlanAddons] WHERE [PlanAddonId] = '" + WorkforceProAddonId + "' " +
				"AND NOT EXISTS (SELECT 1 FROM [PaymentAddons] WHERE [PlanAddonId] = '" + WorkforceProAddonId + "');");
		}
	}
}
