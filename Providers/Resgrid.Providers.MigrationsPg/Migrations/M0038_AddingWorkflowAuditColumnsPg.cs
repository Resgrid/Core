using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(38)]
	public class M0038_AddingWorkflowAuditColumnsPg : Migration
	{
		public override void Up()
		{
			// WorkflowCredentials – add UpdatedByUserId
			Alter.Table("WorkflowCredentials".ToLower())
				.AddColumn("UpdatedByUserId".ToLower()).AsCustom("citext").Nullable();

			// WorkflowSteps – add CreatedByUserId, CreatedOn, UpdatedByUserId, UpdatedOn
			Alter.Table("WorkflowSteps".ToLower())
				.AddColumn("CreatedByUserId".ToLower()).AsCustom("citext").Nullable()
				.AddColumn("CreatedOn".ToLower()).AsDateTime().Nullable()
				.AddColumn("UpdatedByUserId".ToLower()).AsCustom("citext").Nullable()
				.AddColumn("UpdatedOn".ToLower()).AsDateTime().Nullable();
		}

		public override void Down()
		{
			Delete.Column("UpdatedByUserId".ToLower()).FromTable("WorkflowCredentials".ToLower());

			Delete.Column("CreatedByUserId".ToLower()).FromTable("WorkflowSteps".ToLower());
			Delete.Column("CreatedOn".ToLower()).FromTable("WorkflowSteps".ToLower());
			Delete.Column("UpdatedByUserId".ToLower()).FromTable("WorkflowSteps".ToLower());
			Delete.Column("UpdatedOn".ToLower()).FromTable("WorkflowSteps".ToLower());
		}
	}
}

