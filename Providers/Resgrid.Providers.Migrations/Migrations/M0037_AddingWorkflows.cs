using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(37)]
	public class M0037_AddingWorkflows : Migration
	{
		public override void Up()
		{
			// Workflows
			Create.Table("Workflows")
				.WithColumn("WorkflowId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("Name").AsString(250).NotNullable()
				.WithColumn("Description").AsString(1000).Nullable()
				.WithColumn("TriggerEventType").AsInt32().NotNullable()
				.WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("MaxRetryCount").AsInt32().NotNullable().WithDefaultValue(3)
				.WithColumn("RetryBackoffBaseSeconds").AsInt32().NotNullable().WithDefaultValue(5)
				.WithColumn("CreatedByUserId").AsString(128).NotNullable()
				.WithColumn("CreatedOn").AsDateTime().NotNullable()
				.WithColumn("UpdatedOn").AsDateTime().Nullable();

			Create.Index("IX_Workflows_DepartmentId").OnTable("Workflows").OnColumn("DepartmentId");
			Create.Index("IX_Workflows_TriggerEventType").OnTable("Workflows").OnColumn("TriggerEventType");

			// WorkflowCredentials
			Create.Table("WorkflowCredentials")
				.WithColumn("WorkflowCredentialId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("Name").AsString(250).NotNullable()
				.WithColumn("CredentialType").AsInt32().NotNullable()
				.WithColumn("EncryptedData").AsString(int.MaxValue).NotNullable()
				.WithColumn("CreatedByUserId").AsString(128).NotNullable()
				.WithColumn("CreatedOn").AsDateTime().NotNullable()
				.WithColumn("UpdatedOn").AsDateTime().Nullable();

			Create.Index("IX_WorkflowCredentials_DepartmentId").OnTable("WorkflowCredentials").OnColumn("DepartmentId");

			// WorkflowSteps
			Create.Table("WorkflowSteps")
				.WithColumn("WorkflowStepId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("WorkflowId").AsString(128).NotNullable()
				.WithColumn("ActionType").AsInt32().NotNullable()
				.WithColumn("StepOrder").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("OutputTemplate").AsString(int.MaxValue).NotNullable()
				.WithColumn("ActionConfig").AsString(int.MaxValue).Nullable()
				.WithColumn("WorkflowCredentialId").AsString(128).Nullable()
				.WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true);

			Create.ForeignKey("FK_WorkflowSteps_Workflows")
				.FromTable("WorkflowSteps").ForeignColumn("WorkflowId")
				.ToTable("Workflows").PrimaryColumn("WorkflowId");

			Create.ForeignKey("FK_WorkflowSteps_WorkflowCredentials")
				.FromTable("WorkflowSteps").ForeignColumn("WorkflowCredentialId")
				.ToTable("WorkflowCredentials").PrimaryColumn("WorkflowCredentialId");

			Create.Index("IX_WorkflowSteps_WorkflowId").OnTable("WorkflowSteps").OnColumn("WorkflowId");

			// WorkflowRuns
			Create.Table("WorkflowRuns")
				.WithColumn("WorkflowRunId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("WorkflowId").AsString(128).NotNullable()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("TriggerEventType").AsInt32().NotNullable()
				.WithColumn("InputPayload").AsString(int.MaxValue).Nullable()
				.WithColumn("StartedOn").AsDateTime().NotNullable()
				.WithColumn("CompletedOn").AsDateTime().Nullable()
				.WithColumn("ErrorMessage").AsString(4000).Nullable()
				.WithColumn("AttemptNumber").AsInt32().NotNullable().WithDefaultValue(1)
				.WithColumn("QueuedOn").AsDateTime().NotNullable();

			Create.ForeignKey("FK_WorkflowRuns_Workflows")
				.FromTable("WorkflowRuns").ForeignColumn("WorkflowId")
				.ToTable("Workflows").PrimaryColumn("WorkflowId");

			Create.Index("IX_WorkflowRuns_DepartmentId").OnTable("WorkflowRuns").OnColumn("DepartmentId");
			Create.Index("IX_WorkflowRuns_WorkflowId").OnTable("WorkflowRuns").OnColumn("WorkflowId");
			Create.Index("IX_WorkflowRuns_Status").OnTable("WorkflowRuns").OnColumn("Status");
			Create.Index("IX_WorkflowRuns_StartedOn").OnTable("WorkflowRuns").OnColumn("StartedOn");

			// WorkflowRunLogs
			Create.Table("WorkflowRunLogs")
				.WithColumn("WorkflowRunLogId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("WorkflowRunId").AsString(128).NotNullable()
				.WithColumn("WorkflowStepId").AsString(128).NotNullable()
				.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("RenderedOutput").AsString(int.MaxValue).Nullable()
				.WithColumn("ActionResult").AsString(4000).Nullable()
				.WithColumn("ErrorMessage").AsString(4000).Nullable()
				.WithColumn("StartedOn").AsDateTime().NotNullable()
				.WithColumn("CompletedOn").AsDateTime().Nullable()
				.WithColumn("DurationMs").AsInt64().Nullable();

			Create.ForeignKey("FK_WorkflowRunLogs_WorkflowRuns")
				.FromTable("WorkflowRunLogs").ForeignColumn("WorkflowRunId")
				.ToTable("WorkflowRuns").PrimaryColumn("WorkflowRunId");

			Create.Index("IX_WorkflowRunLogs_WorkflowRunId").OnTable("WorkflowRunLogs").OnColumn("WorkflowRunId");
		}

		public override void Down()
		{
			Delete.Table("WorkflowRunLogs");
			Delete.Table("WorkflowRuns");
			Delete.Table("WorkflowSteps");
			Delete.Table("WorkflowCredentials");
			Delete.Table("Workflows");
		}
	}
}

