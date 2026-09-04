using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Reusable transactional domain-event outbox (RMS plan sections 5.3/5.6, registry M0153).
	/// PostgreSQL twin of the SQL Server migration. Existence-guarded for safe retry.
	/// </summary>
	[Migration(153)]
	public class M0153_AddDomainEventOutboxPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("domaineventoutbox").Exists())
			{
				Create.Table("domaineventoutbox")
					.WithColumn("domaineventoutboxid").AsInt64().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("eventid").AsCustom("citext").NotNullable()
					.WithColumn("producersubsystem").AsCustom("citext").NotNullable()
					.WithColumn("eventname").AsCustom("citext").NotNullable()
					.WithColumn("schemaversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("aggregatetype").AsCustom("citext").NotNullable()
					.WithColumn("aggregateid").AsCustom("citext").NotNullable()
					.WithColumn("aggregateversion").AsInt32().Nullable()
					.WithColumn("sequence").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("triggereventtype").AsInt32().Nullable()
					.WithColumn("payloadjson").AsCustom("citext").NotNullable()
					.WithColumn("correlationid").AsCustom("citext").Nullable()
					.WithColumn("causationid").AsCustom("citext").Nullable()
					.WithColumn("originclient").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("originworkflowrunid").AsCustom("citext").Nullable()
					.WithColumn("hopcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("nextattempton").AsDateTime2().Nullable()
					.WithColumn("lasterror").AsCustom("citext").Nullable()
					.WithColumn("leaseowner").AsCustom("citext").Nullable()
					.WithColumn("leaseexpireson").AsDateTime2().Nullable()
					.WithColumn("occurredon").AsDateTime2().NotNullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("dispatchedon").AsDateTime2().Nullable();

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_domaineventoutbox_eventid ON domaineventoutbox (eventid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_domaineventoutbox_state_nextattempt ON domaineventoutbox (state, nextattempton);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_domaineventoutbox_department_aggregate_sequence ON domaineventoutbox (departmentid, aggregateid, sequence);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_domaineventoutbox_createdon ON domaineventoutbox (createdon);");
			}

			// Workflow run contract (plan section 5.6): envelope columns, one initial run per (workflowid, eventid),
			// durable skip reason.
			if (Schema.Table("workflowruns").Exists() && !Schema.Table("workflowruns").Column("eventid").Exists())
			{
				Alter.Table("workflowruns")
					.AddColumn("eventid").AsString(36).Nullable()
					.AddColumn("eventschemaversion").AsInt32().Nullable()
					.AddColumn("correlationid").AsString(36).Nullable()
					.AddColumn("causationid").AsString(36).Nullable()
					.AddColumn("recordsequence").AsInt64().Nullable()
					.AddColumn("originclient").AsInt32().Nullable()
					.AddColumn("aggregateid").AsString(36).Nullable()
					.AddColumn("skipreason").AsString(100).Nullable();

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_workflowruns_workflow_event ON workflowruns (workflowid, eventid) WHERE eventid IS NOT NULL;");
			}
		}

		public override void Down()
		{
			if (Schema.Table("workflowruns").Exists() && Schema.Table("workflowruns").Column("eventid").Exists())
			{
				Execute.Sql("DROP INDEX IF EXISTS ux_workflowruns_workflow_event;");
				foreach (var column in new[] { "eventid", "eventschemaversion", "correlationid", "causationid", "recordsequence", "originclient", "aggregateid", "skipreason" })
					Delete.Column(column).FromTable("workflowruns");
			}

			if (Schema.Table("domaineventoutbox").Exists())
				Delete.Table("domaineventoutbox");
		}
	}
}
