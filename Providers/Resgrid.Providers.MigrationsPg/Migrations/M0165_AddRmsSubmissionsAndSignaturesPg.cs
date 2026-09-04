using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0165 (registry M0165, RMS-2): rmssubmissions and rmssignatures.</summary>
	[Migration(165)]
	public class M0165_AddRmsSubmissionsAndSignaturesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmssubmissions").Exists())
			{
				Create.Table("rmssubmissions")
					.WithColumn("rmssubmissionid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("recordkind").AsInt32().NotNullable()
					.WithColumn("revisionid").AsString(36).Nullable()
					.WithColumn("destination").AsCustom("citext").NotNullable()
					.WithColumn("destinationversion").AsCustom("citext").Nullable()
					.WithColumn("idempotencykey").AsCustom("citext").NotNullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("maxattempts").AsInt32().NotNullable().WithDefaultValue(5)
					.WithColumn("nextattempton").AsDateTime2().Nullable()
					.WithColumn("leaseowner").AsCustom("citext").Nullable()
					.WithColumn("leaseexpireson").AsDateTime2().Nullable()
					.WithColumn("payloadjson").AsCustom("citext").Nullable()
					.WithColumn("payloadchecksum").AsCustom("citext").Nullable()
					.WithColumn("responsejson").AsCustom("citext").Nullable()
					.WithColumn("responsechecksum").AsCustom("citext").Nullable()
					.WithColumn("responsestatuscode").AsInt32().Nullable()
					.WithColumn("externalid").AsCustom("citext").Nullable()
					.WithColumn("externalstatus").AsCustom("citext").Nullable()
					.WithColumn("errorsummary").AsCustom("citext").Nullable()
					.WithColumn("queuedon").AsDateTime2().NotNullable()
					.WithColumn("senton").AsDateTime2().Nullable()
					.WithColumn("completedon").AsDateTime2().Nullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmssubmissions_idempotencykey ON rmssubmissions (idempotencykey);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmssubmissions_state_nextattempt ON rmssubmissions (state, nextattempton);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmssubmissions_department_record ON rmssubmissions (departmentid, recordid);");
			}

			if (!Schema.Table("rmssignatures").Exists())
			{
				Create.Table("rmssignatures")
					.WithColumn("rmssignatureid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("recordkind").AsInt32().NotNullable()
					.WithColumn("revisionid").AsString(36).NotNullable()
					.WithColumn("signeruserid").AsString(128).NotNullable()
					.WithColumn("signernamesnapshot").AsCustom("citext").Nullable()
					.WithColumn("signerrolesnapshot").AsCustom("citext").Nullable()
					.WithColumn("intent").AsInt32().NotNullable()
					.WithColumn("statementversion").AsCustom("citext").Nullable()
					.WithColumn("statementtext").AsCustom("citext").Nullable()
					.WithColumn("method").AsInt32().NotNullable()
					.WithColumn("signedon").AsDateTime2().NotNullable()
					.WithColumn("ipaddress").AsCustom("citext").Nullable()
					.WithColumn("artifactchecksum").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmssignatures_department_record ON rmssignatures (departmentid, recordid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmssignatures_department_revision ON rmssignatures (departmentid, revisionid);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("rmssignatures").Exists())
				Delete.Table("rmssignatures");
			if (Schema.Table("rmssubmissions").Exists())
				Delete.Table("rmssubmissions");
		}
	}
}
