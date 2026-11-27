using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// The Workforce Pro dunning grace (owner 2026-11-26: "a 7 day grace period like LMS"): WorkforceProAddonStates, what the gate last saw of
	/// a department's add-on (packs, paid-through date, renewal off, when it ended), one row per department. The Billing API reports only
	/// running intervals, so this tells a failed renewal (seven days of grace, WorkforceProAddonConfig.DunningGraceDays) from one that was
	/// cancelled or refunded; the LMS's LmsAddonStates (M0352) shape, without its lapse periods. No foreign keys; UnlinkedDepartmentCleanup
	/// removes the row. Existence-guarded for safe retry.
	/// </summary>
	[Migration(423)]
	public class M0423_AddWorkforceProAddonStates : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("WorkforceProAddonStates").Exists())
			{
				Create.Table("WorkforceProAddonStates")
					.WithColumn("WorkforceProAddonStateId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Packs").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PaidThroughUtc").AsDateTime2().Nullable()
					.WithColumn("RenewalCancelled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("LastActiveOn").AsDateTime2().Nullable()
					.WithColumn("LapsedSinceUtc").AsDateTime2().Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().NotNullable();
			}

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_WorkforceProAddonStates_Department' AND object_id = OBJECT_ID('dbo.WorkforceProAddonStates')) " +
				"CREATE UNIQUE INDEX [UX_WorkforceProAddonStates_Department] ON [dbo].[WorkforceProAddonStates] ([DepartmentId]);");
		}

		public override void Down()
		{
			if (Schema.Table("WorkforceProAddonStates").Exists())
				Delete.Table("WorkforceProAddonStates");
		}
	}
}
