using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Adds a unique constraint on workflowdailyusages (departmentid, actiontype, usagedate)
	/// to enable atomic INSERT ... ON CONFLICT DO UPDATE upserts on PostgreSQL.
	/// </summary>
	[Migration(42)]
	public class M0042_AddWorkflowDailyUsagesUniqueConstraintPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("WorkflowDailyUsages".ToLower()).Index("UQ_WorkflowDailyUsages_Dept_Action_Date".ToLower()).Exists())
			{
				Create.Index("UQ_WorkflowDailyUsages_Dept_Action_Date".ToLower())
					.OnTable("WorkflowDailyUsages".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("ActionType".ToLower()).Ascending()
					.OnColumn("UsageDate".ToLower()).Ascending()
					.WithOptions().Unique();
			}
		}

		public override void Down()
		{
			if (Schema.Table("WorkflowDailyUsages".ToLower()).Index("UQ_WorkflowDailyUsages_Dept_Action_Date".ToLower()).Exists())
				Delete.Index("UQ_WorkflowDailyUsages_Dept_Action_Date".ToLower()).OnTable("WorkflowDailyUsages".ToLower());
		}
	}
}

