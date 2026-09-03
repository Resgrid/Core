using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Records (RMS) immutable revisions and access audit (RMS plan sections 4.8/5.2, registry M0155).
	/// PostgreSQL twin of the SQL Server migration. Existence-guarded for safe retry.
	/// </summary>
	[Migration(155)]
	public class M0155_AddRmsRevisionsAndAuditPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsrevisions").Exists())
			{
				Create.Table("rmsrevisions")
					.WithColumn("rmsrevisionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("recordid").AsCustom("citext").NotNullable()
					.WithColumn("recordkind").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("revisionnumber").AsInt32().NotNullable()
					.WithColumn("transition").AsInt32().NotNullable()
					.WithColumn("priorrevisionid").AsCustom("citext").Nullable()
					.WithColumn("definitionkey").AsCustom("citext").NotNullable()
					.WithColumn("definitionversion").AsInt32().NotNullable()
					.WithColumn("snapshotjson").AsCustom("citext").NotNullable()
					.WithColumn("checksum").AsCustom("citext").NotNullable()
					.WithColumn("actoruserid").AsCustom("citext").NotNullable()
					.WithColumn("actorrolesnapshot").AsCustom("citext").Nullable()
					.WithColumn("reasoncode").AsCustom("citext").Nullable()
					.WithColumn("reasontext").AsCustom("citext").Nullable()
					.WithColumn("attestationstatementversion").AsCustom("citext").Nullable()
					.WithColumn("attestedon").AsDateTime2().Nullable()
					.WithColumn("originclient").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable();

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsrevisions_department_record_number ON rmsrevisions (departmentid, recordid, revisionnumber);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrevisions_department_record ON rmsrevisions (departmentid, recordid);");
			}

			if (!Schema.Table("rmsaccessaudits").Exists())
			{
				Create.Table("rmsaccessaudits")
					.WithColumn("rmsaccessauditid").AsInt64().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("recordid").AsCustom("citext").Nullable()
					.WithColumn("revisionid").AsCustom("citext").Nullable()
					.WithColumn("action").AsInt32().NotNullable()
					.WithColumn("actoruserid").AsCustom("citext").Nullable()
					.WithColumn("purpose").AsCustom("citext").Nullable()
					.WithColumn("correlationid").AsCustom("citext").Nullable()
					.WithColumn("originclient").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ipaddress").AsCustom("citext").Nullable()
					.WithColumn("successful").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("occurredon").AsDateTime2().NotNullable()
					.WithColumn("detailjson").AsCustom("citext").Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsaccessaudits_department_record_occurred ON rmsaccessaudits (departmentid, recordid, occurredon DESC);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsaccessaudits_department_occurred ON rmsaccessaudits (departmentid, occurredon DESC);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("rmsaccessaudits").Exists())
				Delete.Table("rmsaccessaudits");

			if (Schema.Table("rmsrevisions").Exists())
				Delete.Table("rmsrevisions");
		}
	}
}
