using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(39)]
	public class M0039_AddingWorkflowDailyUsagesPg : Migration
	{
		public override void Up()
		{
			Create.Table("WorkflowDailyUsages".ToLower())
				.WithColumn("WorkflowDailyUsageId".ToLower()).AsCustom("citext").PrimaryKey().NotNullable()
				.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
				.WithColumn("ActionType".ToLower()).AsInt32().NotNullable()
				.WithColumn("UsageDate".ToLower()).AsDateTime().NotNullable()
				.WithColumn("SendCount".ToLower()).AsInt32().NotNullable().WithDefaultValue(0);

			Create.Index("IX_WorkflowDailyUsages_DepartmentId".ToLower())
				.OnTable("WorkflowDailyUsages".ToLower())
				.OnColumn("DepartmentId".ToLower());

			Create.Index("IX_WorkflowDailyUsages_Dept_Action_Date".ToLower())
				.OnTable("WorkflowDailyUsages".ToLower())
				.OnColumn("DepartmentId".ToLower()).Ascending()
				.OnColumn("ActionType".ToLower()).Ascending()
				.OnColumn("UsageDate".ToLower()).Ascending();
		}

		public override void Down()
		{
			Delete.Table("WorkflowDailyUsages".ToLower());
		}
	}
}


