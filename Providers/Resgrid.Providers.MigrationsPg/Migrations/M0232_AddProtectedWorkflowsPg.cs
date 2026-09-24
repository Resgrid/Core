using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// ADP Protected Workflows (see the SQL Server M0232): the department opt-in on the egress policy, per-workflow
	/// protected releases and the append-only, per-department hash-chained disclosure log. Ships inert. workflowid on
	/// workflowprotectedreleases is deliberately not a foreign key (a deleted workflow's release is revoked and kept).
	/// Both tables are new and empty, so plain existence-guarded index builds are used.
	/// </summary>
	[Migration(232, TransactionBehavior.None)]
	public class M0232_AddProtectedWorkflowsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
ALTER TABLE departmentprotecteddataegresspolicies ADD COLUMN IF NOT EXISTS protectedworkflowsenabled boolean NOT NULL DEFAULT false;
ALTER TABLE departmentprotecteddataegresspolicies ADD COLUMN IF NOT EXISTS protectedworkflowsackversion citext NULL;
ALTER TABLE departmentprotecteddataegresspolicies ADD COLUMN IF NOT EXISTS protectedworkflowsackbyuserid citext NULL;
ALTER TABLE departmentprotecteddataegresspolicies ADD COLUMN IF NOT EXISTS protectedworkflowsackon timestamp NULL;
ALTER TABLE departmentprotecteddataegresspolicies ADD COLUMN IF NOT EXISTS protectedworkflowsrequiresecondapprover boolean NOT NULL DEFAULT false;
ALTER TABLE departmentprotecteddataegresspolicies ADD COLUMN IF NOT EXISTS protectedworkflowsrelaxrequestedbyuserid citext NULL;
ALTER TABLE departmentprotecteddataegresspolicies ADD COLUMN IF NOT EXISTS protectedworkflowsrelaxrequestedon timestamp NULL;");

			if (!Schema.Table("workflowprotectedreleases").Exists())
				Create.Table("workflowprotectedreleases")
					.WithColumn("workflowprotectedreleaseid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("workflowid").AsCustom("citext").NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("suspendedreason").AsCustom("citext").Nullable()
					.WithColumn("allowedfieldids").AsCustom("citext").Nullable()
					.WithColumn("destinationscheme").AsCustom("citext").Nullable()
					.WithColumn("destinationhost").AsCustom("citext").Nullable()
					.WithColumn("tokenhost").AsCustom("citext").Nullable()
					.WithColumn("workflowcredentialid").AsCustom("citext").Nullable()
					.WithColumn("authmethod").AsCustom("citext").Nullable()
					.WithColumn("allowsrestricted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("restrictedackversion").AsCustom("citext").Nullable()
					.WithColumn("restrictedackbyuserid").AsCustom("citext").Nullable()
					.WithColumn("allowspart2").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("part2ackversion").AsCustom("citext").Nullable()
					.WithColumn("part2ackbyuserid").AsCustom("citext").Nullable()
					.WithColumn("configfingerprint").AsCustom("citext").Nullable()
					.WithColumn("recipienttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("recipientname").AsCustom("citext").Nullable()
					.WithColumn("purpose").AsCustom("citext").Nullable()
					.WithColumn("ackversion").AsCustom("citext").Nullable()
					.WithColumn("requestedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("requestedon").AsDateTime2().Nullable()
					.WithColumn("approvedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("approvedon").AsDateTime2().Nullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("expirynoticesentdays").AsInt32().Nullable()
					.WithColumn("revokedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("revokedon").AsDateTime2().Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("updatedon").AsDateTime2().Nullable()
					.WithColumn("version").AsInt32().NotNullable().WithDefaultValue(1);

			// One current (non-revoked) release per workflow; revoked rows are history.
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_workflowprotectedreleases_workflow_current ON workflowprotectedreleases (workflowid) WHERE state <> 5;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_workflowprotectedreleases_workflow_created ON workflowprotectedreleases (workflowid, createdon DESC);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_workflowprotectedreleases_departmentid ON workflowprotectedreleases (departmentid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_workflowprotectedreleases_state ON workflowprotectedreleases (state);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_workflowprotectedreleases_workflowcredentialid ON workflowprotectedreleases (workflowcredentialid);");

			// Hashed columns use text/varchar rather than citext so the stored bytes are exactly the hashed bytes.
			if (!Schema.Table("protectedworkflowdisclosures").Exists())
				Create.Table("protectedworkflowdisclosures")
					.WithColumn("protectedworkflowdisclosureid").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("chainsequence").AsInt64().NotNullable()
					.WithColumn("recordtype").AsString(32).NotNullable()
					.WithColumn("eventtype").AsString(64).Nullable()
					.WithColumn("actoruserid").AsString(128).Nullable()
					.WithColumn("workflowid").AsString(128).Nullable()
					.WithColumn("workflowrunid").AsString(128).Nullable()
					.WithColumn("workflowstepid").AsString(128).Nullable()
					.WithColumn("workflowprotectedreleaseid").AsString(128).Nullable()
					.WithColumn("entitytype").AsString(32).Nullable()
					.WithColumn("entityid").AsString(64).Nullable()
					.WithColumn("fieldids").AsString(int.MaxValue).Nullable()
					.WithColumn("destinationhost").AsString(255).Nullable()
					.WithColumn("payloadsha256").AsString(64).Nullable()
					.WithColumn("payloadbytes").AsInt32().Nullable()
					.WithColumn("httpstatus").AsInt32().Nullable()
					.WithColumn("outcome").AsString(32).Nullable()
					.WithColumn("istest").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("brokerrequestid").AsString(64).Nullable()
					.WithColumn("detail").AsString(500).Nullable()
					.WithColumn("contenttype").AsString(64).Nullable()
					.WithColumn("capturedkeys").AsString(1000).Nullable()
					.WithColumn("occurredon").AsDateTime2().NotNullable()
					.WithColumn("prevhash").AsString(64).NotNullable()
					.WithColumn("hash").AsString(64).NotNullable();

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_protectedworkflowdisclosures_department_sequence ON protectedworkflowdisclosures (departmentid, chainsequence);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protectedworkflowdisclosures_department_workflow ON protectedworkflowdisclosures (departmentid, workflowid, occurredon DESC);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protectedworkflowdisclosures_department_entity ON protectedworkflowdisclosures (departmentid, entityid);");
		}

		public override void Down()
		{
			// Drops the disclosure chain (audit data): export and retain it first.
			if (Schema.Table("protectedworkflowdisclosures").Exists())
				Delete.Table("protectedworkflowdisclosures");
			if (Schema.Table("workflowprotectedreleases").Exists())
				Delete.Table("workflowprotectedreleases");

			Execute.Sql(@"
ALTER TABLE departmentprotecteddataegresspolicies DROP COLUMN IF EXISTS protectedworkflowsrelaxrequestedon;
ALTER TABLE departmentprotecteddataegresspolicies DROP COLUMN IF EXISTS protectedworkflowsrelaxrequestedbyuserid;
ALTER TABLE departmentprotecteddataegresspolicies DROP COLUMN IF EXISTS protectedworkflowsrequiresecondapprover;
ALTER TABLE departmentprotecteddataegresspolicies DROP COLUMN IF EXISTS protectedworkflowsackon;
ALTER TABLE departmentprotecteddataegresspolicies DROP COLUMN IF EXISTS protectedworkflowsackbyuserid;
ALTER TABLE departmentprotecteddataegresspolicies DROP COLUMN IF EXISTS protectedworkflowsackversion;
ALTER TABLE departmentprotecteddataegresspolicies DROP COLUMN IF EXISTS protectedworkflowsenabled;");
		}
	}
}
