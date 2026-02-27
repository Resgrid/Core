using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Adds a unique constraint on WorkflowDailyUsages (DepartmentId, ActionType, UsageDate)
	/// to enable atomic MERGE-based upserts on SQL Server.
	/// </summary>
	[Migration(42)]
	public class M0042_AddWorkflowDailyUsagesUniqueConstraint : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("WorkflowDailyUsages").Index("UQ_WorkflowDailyUsages_Dept_Action_Date").Exists())
			{
				Create.Index("UQ_WorkflowDailyUsages_Dept_Action_Date")
					.OnTable("WorkflowDailyUsages")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("ActionType").Ascending()
					.OnColumn("UsageDate").Ascending()
					.WithOptions().Unique();
			}
		}

		public override void Down()
		{
			if (Schema.Table("WorkflowDailyUsages").Index("UQ_WorkflowDailyUsages_Dept_Action_Date").Exists())
				Delete.Index("UQ_WorkflowDailyUsages_Dept_Action_Date").OnTable("WorkflowDailyUsages");
		}
	}
}

