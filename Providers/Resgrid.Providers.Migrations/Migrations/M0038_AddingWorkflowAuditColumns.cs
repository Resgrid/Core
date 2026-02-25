using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(38)]
	public class M0038_AddingWorkflowAuditColumns : Migration
	{
		public override void Up()
		{
			// WorkflowCredentials – add UpdatedByUserId
			Alter.Table("WorkflowCredentials")
				.AddColumn("UpdatedByUserId").AsString(128).Nullable();

			// WorkflowSteps – add CreatedByUserId, CreatedOn, UpdatedByUserId, UpdatedOn
			Alter.Table("WorkflowSteps")
				.AddColumn("CreatedByUserId").AsString(128).Nullable()
				.AddColumn("CreatedOn").AsDateTime().Nullable()
				.AddColumn("UpdatedByUserId").AsString(128).Nullable()
				.AddColumn("UpdatedOn").AsDateTime().Nullable();
		}

		public override void Down()
		{
			Delete.Column("UpdatedByUserId").FromTable("WorkflowCredentials");

			Delete.Column("CreatedByUserId").FromTable("WorkflowSteps");
			Delete.Column("CreatedOn").FromTable("WorkflowSteps");
			Delete.Column("UpdatedByUserId").FromTable("WorkflowSteps");
			Delete.Column("UpdatedOn").FromTable("WorkflowSteps");
		}
	}
}

