using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Records (RMS) child tables (registry M0151): rmsrecordparticipants, rmsrecordunitresponses and
	/// rmsrecordattachments. Draft rows carry revisionid NULL; finalization copies them under the new
	/// revisionid. departmentid and protectionid are on every child row (plan section 5.9.1).
	/// PostgreSQL twin of the SQL Server migration. Existence-guarded for safe retry.
	/// </summary>
	[Migration(151)]
	public class M0151_AddRmsParticipantsUnitsAttachmentsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsrecordparticipants").Exists())
			{
				Create.Table("rmsrecordparticipants")
					.WithColumn("rmsrecordparticipantid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("recordid").AsCustom("citext").NotNullable()
					.WithColumn("revisionid").AsCustom("citext").Nullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("displaynamesnapshot").AsCustom("citext").Nullable()
					.WithColumn("groupidsnapshot").AsInt32().Nullable()
					.WithColumn("groupnamesnapshot").AsCustom("citext").Nullable()
					.WithColumn("unitid").AsInt32().Nullable()
					.WithColumn("role").AsCustom("citext").Nullable()
					.WithColumn("participationstart").AsDateTime2().Nullable()
					.WithColumn("participationend").AsDateTime2().Nullable()
					.WithColumn("sourcekind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordparticipants_department_record ON rmsrecordparticipants (departmentid, recordid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordparticipants_department_user ON rmsrecordparticipants (departmentid, userid);");
			}

			if (!Schema.Table("rmsrecordunitresponses").Exists())
			{
				Create.Table("rmsrecordunitresponses")
					.WithColumn("rmsrecordunitresponseid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("recordid").AsCustom("citext").NotNullable()
					.WithColumn("revisionid").AsCustom("citext").Nullable()
					.WithColumn("unitid").AsInt32().NotNullable()
					.WithColumn("unitnamesnapshot").AsCustom("citext").Nullable()
					.WithColumn("unittypesnapshot").AsCustom("citext").Nullable()
					.WithColumn("stationgroupidsnapshot").AsInt32().Nullable()
					.WithColumn("dispatched").AsDateTime2().Nullable()
					.WithColumn("enroute").AsDateTime2().Nullable()
					.WithColumn("onscene").AsDateTime2().Nullable()
					.WithColumn("released").AsDateTime2().Nullable()
					.WithColumn("inquarters").AsDateTime2().Nullable()
					.WithColumn("timessourcekind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("prefilljson").AsCustom("citext").Nullable()
					.WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordunitresponses_department_record ON rmsrecordunitresponses (departmentid, recordid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordunitresponses_department_unit ON rmsrecordunitresponses (departmentid, unitid);");
			}

			if (!Schema.Table("rmsrecordattachments").Exists())
			{
				Create.Table("rmsrecordattachments")
					.WithColumn("rmsrecordattachmentid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("recordid").AsCustom("citext").NotNullable()
					.WithColumn("filename").AsCustom("citext").Nullable()
					.WithColumn("contenttype").AsCustom("citext").Nullable()
					.WithColumn("bytesize").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("checksum").AsCustom("citext").Nullable()
					.WithColumn("data").AsCustom("bytea").Nullable()
					.WithColumn("storagereference").AsCustom("citext").Nullable()
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("uploadedbyuserid").AsCustom("citext").NotNullable()
					.WithColumn("uploadedon").AsDateTime2().NotNullable()
					.WithColumn("scanstate").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("metadatastripped").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordattachments_department_record ON rmsrecordattachments (departmentid, recordid);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("rmsrecordattachments").Exists())
				Delete.Table("rmsrecordattachments");

			if (Schema.Table("rmsrecordunitresponses").Exists())
				Delete.Table("rmsrecordunitresponses");

			if (Schema.Table("rmsrecordparticipants").Exists())
				Delete.Table("rmsrecordparticipants");
		}
	}
}
