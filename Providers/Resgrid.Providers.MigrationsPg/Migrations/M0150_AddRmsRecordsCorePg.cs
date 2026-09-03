using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Records (RMS) core aggregate (RMS plan sections 5.2/5.3, registry M0150): rmsoperationalrecords
	/// (header), rmsoperationalrecorddetails (typed Logs-parity fields; one working-draft row with
	/// revisionid NULL and one immutable row per revision) and rmsexternalreferences. Every table carries
	/// departmentid and an immutable random protectionid (plan section 5.9.1). PostgreSQL twin of the SQL
	/// Server migration: lowercase identifiers, citext text, partial unique indexes in place of filtered
	/// ones. Existence-guarded for safe retry.
	/// </summary>
	[Migration(150)]
	public class M0150_AddRmsRecordsCorePg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsoperationalrecords").Exists())
			{
				Create.Table("rmsoperationalrecords")
					.WithColumn("rmsoperationalrecordid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("definitionkey").AsCustom("citext").NotNullable()
					.WithColumn("definitionversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("recordtype").AsInt32().Nullable()
					.WithColumn("lifecyclepreset").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("recordnumber").AsCustom("citext").Nullable()
					.WithColumn("draftreference").AsCustom("citext").NotNullable()
					.WithColumn("displaysummary").AsCustom("citext").Nullable()
					.WithColumn("stationgroupid").AsInt32().Nullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("externalid").AsCustom("citext").Nullable()
					.WithColumn("authoruserid").AsCustom("citext").NotNullable()
					.WithColumn("authorgroupidsnapshot").AsInt32().Nullable()
					.WithColumn("owneruserid").AsCustom("citext").NotNullable()
					.WithColumn("startedon").AsDateTime2().Nullable()
					.WithColumn("endedon").AsDateTime2().Nullable()
					.WithColumn("reviewdueon").AsDateTime2().Nullable()
					.WithColumn("submittedforreviewon").AsDateTime2().Nullable()
					.WithColumn("returnedon").AsDateTime2().Nullable()
					.WithColumn("returnreasoncode").AsCustom("citext").Nullable()
					.WithColumn("returnreasontext").AsCustom("citext").Nullable()
					.WithColumn("returncount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("revieweruserid").AsCustom("citext").Nullable()
					.WithColumn("approvedon").AsDateTime2().Nullable()
					.WithColumn("approveruserid").AsCustom("citext").Nullable()
					.WithColumn("finalizedon").AsDateTime2().Nullable()
					.WithColumn("finalizedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("currentrevisionid").AsCustom("citext").Nullable()
					.WithColumn("revisioncount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("amendsrevisionid").AsCustom("citext").Nullable()
					.WithColumn("voidedon").AsDateTime2().Nullable()
					.WithColumn("voidedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("voidreasoncode").AsCustom("citext").Nullable()
					.WithColumn("voidreasontext").AsCustom("citext").Nullable()
					.WithColumn("cancelledon").AsDateTime2().Nullable()
					.WithColumn("cancelledbyuserid").AsCustom("citext").Nullable()
					.WithColumn("idempotencykey").AsCustom("citext").Nullable()
					.WithColumn("originclient").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("modifiedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsoperationalrecords_department_state ON rmsoperationalrecords (departmentid, state);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsoperationalrecords_department_definition_created ON rmsoperationalrecords (departmentid, definitionkey, createdon DESC);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsoperationalrecords_department_author ON rmsoperationalrecords (departmentid, authoruserid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsoperationalrecords_department_owner ON rmsoperationalrecords (departmentid, owneruserid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsoperationalrecords_department_call ON rmsoperationalrecords (departmentid, callid) WHERE callid IS NOT NULL;");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsoperationalrecords_department_idempotencykey ON rmsoperationalrecords (departmentid, idempotencykey) WHERE idempotencykey IS NOT NULL;");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsoperationalrecords_department_recordnumber ON rmsoperationalrecords (departmentid, recordnumber) WHERE recordnumber IS NOT NULL;");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsoperationalrecords_department_draftreference ON rmsoperationalrecords (departmentid, draftreference);");
			}

			if (!Schema.Table("rmsoperationalrecorddetails").Exists())
			{
				Create.Table("rmsoperationalrecorddetails")
					.WithColumn("rmsoperationalrecorddetailid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("recordid").AsCustom("citext").NotNullable()
					.WithColumn("revisionid").AsCustom("citext").Nullable()
					.WithColumn("narrative").AsCustom("citext").Nullable()
					.WithColumn("initialreport").AsCustom("citext").Nullable()
					.WithColumn("type").AsCustom("citext").Nullable()
					.WithColumn("course").AsCustom("citext").Nullable()
					.WithColumn("coursecode").AsCustom("citext").Nullable()
					.WithColumn("instructors").AsCustom("citext").Nullable()
					.WithColumn("cause").AsCustom("citext").Nullable()
					.WithColumn("investigatedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("contactname").AsCustom("citext").Nullable()
					.WithColumn("contactnumber").AsCustom("citext").Nullable()
					.WithColumn("otherpersonnel").AsCustom("citext").Nullable()
					.WithColumn("location").AsCustom("citext").Nullable()
					.WithColumn("otheragencies").AsCustom("citext").Nullable()
					.WithColumn("otherunits").AsCustom("citext").Nullable()
					.WithColumn("bodylocation").AsCustom("citext").Nullable()
					.WithColumn("pronounceddeceasedby").AsCustom("citext").Nullable()
					.WithColumn("casenumber").AsCustom("citext").Nullable()
					.WithColumn("destination").AsCustom("citext").Nullable()
					.WithColumn("facilitator").AsCustom("citext").Nullable()
					.WithColumn("unitid").AsInt32().Nullable()
					.WithColumn("activityon").AsDateTime2().Nullable()
					.WithColumn("callnumber").AsCustom("citext").Nullable()
					.WithColumn("callname").AsCustom("citext").Nullable()
					.WithColumn("calltype").AsCustom("citext").Nullable()
					.WithColumn("callpriority").AsInt32().Nullable()
					.WithColumn("callloggedon").AsDateTime2().Nullable()
					.WithColumn("calladdress").AsCustom("citext").Nullable()
					.WithColumn("callnature").AsCustom("citext").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedenvelope").AsString(int.MaxValue).Nullable()
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsoperationalrecorddetails_department_record ON rmsoperationalrecorddetails (departmentid, recordid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsoperationalrecorddetails_record_draft ON rmsoperationalrecorddetails (departmentid, recordid) WHERE revisionid IS NULL;");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsoperationalrecorddetails_record_revision ON rmsoperationalrecorddetails (departmentid, recordid, revisionid) WHERE revisionid IS NOT NULL;");
			}

			if (!Schema.Table("rmsexternalreferences").Exists())
			{
				Create.Table("rmsexternalreferences")
					.WithColumn("rmsexternalreferenceid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("recordid").AsCustom("citext").NotNullable()
					.WithColumn("recordkind").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("sourcesubsystem").AsCustom("citext").NotNullable()
					.WithColumn("sourceentitytype").AsCustom("citext").NotNullable()
					.WithColumn("sourceentityid").AsCustom("citext").NotNullable()
					.WithColumn("identifierscheme").AsCustom("citext").Nullable()
					.WithColumn("sourceversion").AsCustom("citext").Nullable()
					.WithColumn("sourceeventid").AsCustom("citext").Nullable()
					.WithColumn("semanticrole").AsCustom("citext").NotNullable()
					.WithColumn("capturedon").AsDateTime2().NotNullable()
					.WithColumn("capturedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("checksum").AsCustom("citext").Nullable()
					.WithColumn("safeurl").AsCustom("citext").Nullable()
					.WithColumn("snapshotjson").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsexternalreferences_department_record ON rmsexternalreferences (departmentid, recordid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsexternalreferences_department_source ON rmsexternalreferences (departmentid, sourcesubsystem, sourceentitytype, sourceentityid);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("rmsexternalreferences").Exists())
				Delete.Table("rmsexternalreferences");

			if (Schema.Table("rmsoperationalrecorddetails").Exists())
				Delete.Table("rmsoperationalrecorddetails");

			if (Schema.Table("rmsoperationalrecords").Exists())
				Delete.Table("rmsoperationalrecords");
		}
	}
}
