using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(39)]
	public class M0039_AddingWorkflowDailyUsages : Migration
	{
		public override void Up()
		{
			Create.Table("WorkflowDailyUsages")
				.WithColumn("WorkflowDailyUsageId").AsString(128).PrimaryKey().NotNullable()
				.WithColumn("DepartmentId").AsInt32().NotNullable().Indexed()
				.WithColumn("ActionType").AsInt32().NotNullable()
				.WithColumn("UsageDate").AsDateTime().NotNullable()
				.WithColumn("SendCount").AsInt32().NotNullable().WithDefaultValue(0);

			Create.Index("IX_WorkflowDailyUsages_Dept_Action_Date")
				.OnTable("WorkflowDailyUsages")
				.OnColumn("DepartmentId").Ascending()
				.OnColumn("ActionType").Ascending()
				.OnColumn("UsageDate").Ascending();
		}

		public override void Down()
		{
			Delete.Table("WorkflowDailyUsages");
		}
	}
}

