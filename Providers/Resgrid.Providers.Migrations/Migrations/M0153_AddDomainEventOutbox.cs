using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Reusable transactional domain-event outbox (RMS plan sections 5.3/5.6, cross-plan adjustment 7,
	/// registry M0153). A producer writes its versioned, safe event payload in the same transaction as
	/// the state change; the in-process post-commit dispatcher and worker command 40
	/// (DomainEventOutboxDispatchCommand, the durable catch-up sweep) deliver it to the Workflow event
	/// pipeline. Producers keep ownership of their event schemas, trigger semantics and permissions;
	/// this table is transport only. EventId is the idempotency key for (WorkflowId, EventId)
	/// deduplication; (AggregateId, Sequence) preserves per-record ordering in payloads.
	/// </summary>
	[Migration(153)]
	public class M0153_AddDomainEventOutbox : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("DomainEventOutbox").Exists())
			{
				Create.Table("DomainEventOutbox")
					.WithColumn("DomainEventOutboxId").AsInt64().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("EventId").AsString(36).NotNullable()
					.WithColumn("ProducerSubsystem").AsString(50).NotNullable()
					.WithColumn("EventName").AsString(100).NotNullable()
					.WithColumn("SchemaVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("AggregateType").AsString(100).NotNullable()
					.WithColumn("AggregateId").AsString(36).NotNullable()
					.WithColumn("AggregateVersion").AsInt32().Nullable()
					.WithColumn("Sequence").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("TriggerEventType").AsInt32().Nullable()
					.WithColumn("PayloadJson").AsString(int.MaxValue).NotNullable()
					.WithColumn("CorrelationId").AsString(36).Nullable()
					.WithColumn("CausationId").AsString(36).Nullable()
					.WithColumn("OriginClient").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("OriginWorkflowRunId").AsString(36).Nullable()
					.WithColumn("HopCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("NextAttemptOn").AsDateTime2().Nullable()
					.WithColumn("LastError").AsString(int.MaxValue).Nullable()
					.WithColumn("LeaseOwner").AsString(100).Nullable()
					.WithColumn("LeaseExpiresOn").AsDateTime2().Nullable()
					.WithColumn("OccurredOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("DispatchedOn").AsDateTime2().Nullable();

				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_DomainEventOutbox_EventId ON DomainEventOutbox (EventId);");
				Create.Index("IX_DomainEventOutbox_State_NextAttempt").OnTable("DomainEventOutbox")
					.OnColumn("State").Ascending().OnColumn("NextAttemptOn").Ascending();
				Create.Index("IX_DomainEventOutbox_Department_Aggregate_Sequence").OnTable("DomainEventOutbox")
					.OnColumn("DepartmentId").Ascending().OnColumn("AggregateId").Ascending().OnColumn("Sequence").Ascending();
				Create.Index("IX_DomainEventOutbox_CreatedOn").OnTable("DomainEventOutbox")
					.OnColumn("CreatedOn").Ascending();
			}

			// Workflow run contract (plan section 5.6): the envelope on every Records-triggered run, one initial run
			// per (WorkflowId, EventId), and a durable skip reason so a suppressed Records event is never silent.
			if (Schema.Table("WorkflowRuns").Exists() && !Schema.Table("WorkflowRuns").Column("EventId").Exists())
			{
				Alter.Table("WorkflowRuns")
					.AddColumn("EventId").AsString(36).Nullable()
					.AddColumn("EventSchemaVersion").AsInt32().Nullable()
					.AddColumn("CorrelationId").AsString(36).Nullable()
					.AddColumn("CausationId").AsString(36).Nullable()
					.AddColumn("RecordSequence").AsInt64().Nullable()
					.AddColumn("OriginClient").AsInt32().Nullable()
					.AddColumn("AggregateId").AsString(36).Nullable()
					.AddColumn("SkipReason").AsString(100).Nullable();

				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_WorkflowRuns_Workflow_Event') CREATE UNIQUE NONCLUSTERED INDEX UX_WorkflowRuns_Workflow_Event ON WorkflowRuns (WorkflowId, EventId) WHERE EventId IS NOT NULL;");
			}
		}

		public override void Down()
		{
			if (Schema.Table("WorkflowRuns").Exists() && Schema.Table("WorkflowRuns").Column("EventId").Exists())
			{
				Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_WorkflowRuns_Workflow_Event') DROP INDEX UX_WorkflowRuns_Workflow_Event ON WorkflowRuns;");
				foreach (var column in new[] { "EventId", "EventSchemaVersion", "CorrelationId", "CausationId", "RecordSequence", "OriginClient", "AggregateId", "SkipReason" })
					Delete.Column(column).FromTable("WorkflowRuns");
			}

			if (Schema.Table("DomainEventOutbox").Exists())
				Delete.Table("DomainEventOutbox");
		}
	}
}
