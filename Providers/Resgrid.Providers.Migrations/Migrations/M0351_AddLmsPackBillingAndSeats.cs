using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// LMS general availability, billing and seats (protocols-training-knowledge-plan §17.3, §17.4, decision 7, §25.4a items 5 and 7):
	/// <list type="bullet">
	/// <item>the PlanAddons catalog row for PlanAddonTypes 9 (LmsAddonConfig.PlanAddonId; Cost 79 = the first pack's USD price). Its
	/// Stripe graduated price does not exist yet, so ExternalId and TestExternalId stay empty and the Billing API's checkout fails
	/// closed until they are set. The Training.Lms flag stays seeded off (M0346): general availability is the operator switching it
	/// on, not this migration;</item>
	/// <item>LmsMemberSeats: one row per LMS-enabled member per department, unique per (department, member). No foreign keys (an LMS
	/// table referencing nothing baseline, §17.3); UnlinkedDepartmentCleanup removes the rows.</item>
	/// </list>
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(351)]
	public class M0351_AddLmsPackBillingAndSeats : Migration
	{
		public const string LmsAddonId = "7d4e2b91-3c58-4f0a-9e16-5b8a2c4d7f93";

		public override void Up()
		{
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [PlanAddons] WHERE [PlanAddonId] = '" + LmsAddonId + "' OR [AddonType] = 9) " +
				"INSERT INTO [PlanAddons] ([PlanAddonId], [AddonType], [Cost], [ExternalId], [TestExternalId]) " +
				"VALUES ('" + LmsAddonId + "', 9, 79, '', '');");

			if (!Schema.Table("LmsMemberSeats").Exists())
			{
				Create.Table("LmsMemberSeats")
					.WithColumn("LmsMemberSeatId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("EnabledOn").AsDateTime2().NotNullable()
					.WithColumn("EnabledByUserId").AsString(128).Nullable();
			}

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsMemberSeats_Department_User' AND object_id = OBJECT_ID('dbo.LmsMemberSeats')) " +
				"CREATE UNIQUE INDEX [UX_LmsMemberSeats_Department_User] ON [dbo].[LmsMemberSeats] ([DepartmentId], [UserId]);");
		}

		public override void Down()
		{
			if (Schema.Table("LmsMemberSeats").Exists())
				Delete.Table("LmsMemberSeats");

			// The catalog row goes only when nothing was ever bought on it.
			Execute.Sql(
				"DELETE FROM [PlanAddons] WHERE [PlanAddonId] = '" + LmsAddonId + "' " +
				"AND NOT EXISTS (SELECT 1 FROM [PaymentAddons] WHERE [PlanAddonId] = '" + LmsAddonId + "');");
		}
	}
}
