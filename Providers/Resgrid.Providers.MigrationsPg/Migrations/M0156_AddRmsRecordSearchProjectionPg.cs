using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Records (RMS) search projection (RMS plan sections 5.3/5.10, registry M0156):
	/// rmsrecordsearchprojections (the safe, rebuildable RecordSearchProjectionV1 row per Record) and
	/// rmssearchindexstates (per-department index generation key). PostgreSQL twin of the SQL Server
	/// migration. Existence-guarded for safe retry.
	/// </summary>
	[Migration(156)]
	public class M0156_AddRmsRecordSearchProjectionPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsrecordsearchprojections").Exists())
			{
				Create.Table("rmsrecordsearchprojections")
					.WithColumn("rmsrecordsearchprojectionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("sourcetype").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("sourceid").AsCustom("citext").NotNullable()
					.WithColumn("recordkind").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("recordnumber").AsCustom("citext").Nullable()
					.WithColumn("draftreference").AsCustom("citext").Nullable()
					.WithColumn("definitionkey").AsCustom("citext").NotNullable()
					.WithColumn("definitionversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("recordtype").AsInt32().Nullable()
					.WithColumn("state").AsInt32().NotNullable()
					.WithColumn("occurredon").AsDateTime2().Nullable()
					.WithColumn("recordcreatedon").AsDateTime2().NotNullable()
					.WithColumn("finalizedon").AsDateTime2().Nullable()
					.WithColumn("stationgroupid").AsInt32().Nullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("callnumber").AsCustom("citext").Nullable()
					.WithColumn("authoruserid").AsCustom("citext").Nullable()
					.WithColumn("owneruserid").AsCustom("citext").Nullable()
					.WithColumn("revieweruserid").AsCustom("citext").Nullable()
					.WithColumn("participantuserids").AsCustom("citext").Nullable()
					.WithColumn("unitids").AsCustom("citext").Nullable()
					.WithColumn("groupscopeids").AsCustom("citext").Nullable()
					.WithColumn("displaysummary").AsCustom("citext").Nullable()
					.WithColumn("searchtext").AsCustom("citext").Nullable()
					.WithColumn("islegacy").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("projectionversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("policyepoch").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsrecordsearchprojections_department_source ON rmsrecordsearchprojections (departmentid, sourcetype, sourceid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordsearchprojections_department_state ON rmsrecordsearchprojections (departmentid, state);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordsearchprojections_department_occurred ON rmsrecordsearchprojections (departmentid, occurredon DESC);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordsearchprojections_department_modified ON rmsrecordsearchprojections (departmentid, modifiedon);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordsearchprojections_department_call ON rmsrecordsearchprojections (departmentid, callid) WHERE callid IS NOT NULL;");
			}

			if (!Schema.Table("rmssearchindexstates").Exists())
			{
				Create.Table("rmssearchindexstates")
					.WithColumn("rmssearchindexstateid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("indexname").AsCustom("citext").NotNullable()
					.WithColumn("schemaversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("policyepoch").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("generation").AsCustom("citext").NotNullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("documentcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("lastrebuilton").AsDateTime2().Nullable()
					.WithColumn("lastindexedmodifiedon").AsDateTime2().Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable();

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmssearchindexstates_department_index ON rmssearchindexstates (departmentid, indexname);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("rmssearchindexstates").Exists())
				Delete.Table("rmssearchindexstates");

			if (Schema.Table("rmsrecordsearchprojections").Exists())
				Delete.Table("rmsrecordsearchprojections");
		}
	}
}
