using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(37)]
	public class M0037_AddingWorkflowsPg : Migration
	{
		public override void Up()
		{
			Create.Table("Workflows".ToLower())
				.WithColumn("WorkflowId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
				.WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("Description".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("TriggerEventType".ToLower()).AsInt32().NotNullable()
				.WithColumn("IsEnabled".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("MaxRetryCount".ToLower()).AsInt32().NotNullable().WithDefaultValue(3)
				.WithColumn("RetryBackoffBaseSeconds".ToLower()).AsInt32().NotNullable().WithDefaultValue(5)
				.WithColumn("CreatedByUserId".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("CreatedOn".ToLower()).AsDateTime().NotNullable()
				.WithColumn("UpdatedOn".ToLower()).AsDateTime().Nullable();

			Create.Index("IX_Workflows_DepartmentId".ToLower()).OnTable("Workflows".ToLower()).OnColumn("DepartmentId".ToLower());
			Create.Index("IX_Workflows_TriggerEventType".ToLower()).OnTable("Workflows".ToLower()).OnColumn("TriggerEventType".ToLower());

			Create.Table("WorkflowCredentials".ToLower())
				.WithColumn("WorkflowCredentialId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
				.WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("CredentialType".ToLower()).AsInt32().NotNullable()
				.WithColumn("EncryptedData".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("CreatedByUserId".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("CreatedOn".ToLower()).AsDateTime().NotNullable()
				.WithColumn("UpdatedOn".ToLower()).AsDateTime().Nullable();

			Create.Index("IX_WorkflowCredentials_DepartmentId".ToLower()).OnTable("WorkflowCredentials".ToLower()).OnColumn("DepartmentId".ToLower());

			Create.Table("WorkflowSteps".ToLower())
				.WithColumn("WorkflowStepId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("WorkflowId".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("ActionType".ToLower()).AsInt32().NotNullable()
				.WithColumn("StepOrder".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("OutputTemplate".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("ActionConfig".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("WorkflowCredentialId".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("IsEnabled".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true);

			Create.ForeignKey("FK_WorkflowSteps_Workflows".ToLower())
				.FromTable("WorkflowSteps".ToLower()).ForeignColumn("WorkflowId".ToLower())
				.ToTable("Workflows".ToLower()).PrimaryColumn("WorkflowId".ToLower());

			Create.ForeignKey("FK_WorkflowSteps_WorkflowCredentials".ToLower())
				.FromTable("WorkflowSteps".ToLower()).ForeignColumn("WorkflowCredentialId".ToLower())
				.ToTable("WorkflowCredentials".ToLower()).PrimaryColumn("WorkflowCredentialId".ToLower());

			Create.Index("IX_WorkflowSteps_WorkflowId".ToLower()).OnTable("WorkflowSteps".ToLower()).OnColumn("WorkflowId".ToLower());

			Create.Table("WorkflowRuns".ToLower())
				.WithColumn("WorkflowRunId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("WorkflowId".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
				.WithColumn("Status".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("TriggerEventType".ToLower()).AsInt32().NotNullable()
				.WithColumn("InputPayload".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("StartedOn".ToLower()).AsDateTime().NotNullable()
				.WithColumn("CompletedOn".ToLower()).AsDateTime().Nullable()
				.WithColumn("ErrorMessage".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("AttemptNumber".ToLower()).AsInt32().NotNullable().WithDefaultValue(1)
				.WithColumn("QueuedOn".ToLower()).AsDateTime().NotNullable();

			Create.ForeignKey("FK_WorkflowRuns_Workflows".ToLower())
				.FromTable("WorkflowRuns".ToLower()).ForeignColumn("WorkflowId".ToLower())
				.ToTable("Workflows".ToLower()).PrimaryColumn("WorkflowId".ToLower());

			Create.Index("IX_WorkflowRuns_DepartmentId".ToLower()).OnTable("WorkflowRuns".ToLower()).OnColumn("DepartmentId".ToLower());
			Create.Index("IX_WorkflowRuns_WorkflowId".ToLower()).OnTable("WorkflowRuns".ToLower()).OnColumn("WorkflowId".ToLower());
			Create.Index("IX_WorkflowRuns_Status".ToLower()).OnTable("WorkflowRuns".ToLower()).OnColumn("Status".ToLower());

			Create.Table("WorkflowRunLogs".ToLower())
				.WithColumn("WorkflowRunLogId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("WorkflowRunId".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("WorkflowStepId".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("Status".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("RenderedOutput".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("ActionResult".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("ErrorMessage".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("StartedOn".ToLower()).AsDateTime().NotNullable()
				.WithColumn("CompletedOn".ToLower()).AsDateTime().Nullable()
				.WithColumn("DurationMs".ToLower()).AsInt64().Nullable();

			Create.ForeignKey("FK_WorkflowRunLogs_WorkflowRuns".ToLower())
				.FromTable("WorkflowRunLogs".ToLower()).ForeignColumn("WorkflowRunId".ToLower())
				.ToTable("WorkflowRuns".ToLower()).PrimaryColumn("WorkflowRunId".ToLower());

			Create.Index("IX_WorkflowRunLogs_WorkflowRunId".ToLower()).OnTable("WorkflowRunLogs".ToLower()).OnColumn("WorkflowRunId".ToLower());
		}

		public override void Down()
		{
			Delete.Table("WorkflowRunLogs".ToLower());
			Delete.Table("WorkflowRuns".ToLower());
			Delete.Table("WorkflowSteps".ToLower());
			Delete.Table("WorkflowCredentials".ToLower());
			Delete.Table("Workflows".ToLower());
		}
	}
}

