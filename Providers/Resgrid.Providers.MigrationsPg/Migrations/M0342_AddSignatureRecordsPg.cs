using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0342_AddSignatureRecords: signaturerecords (S0). Text columns are citext; the artifact hash stays exact
	/// varchar(64). No foreign keys. Guarded for safe retry.
	/// </summary>
	[Migration(342)]
	public class M0342_AddSignatureRecordsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("signaturerecords").Exists())
			{
				Create.Table("signaturerecords")
					.WithColumn("signaturerecordid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("subjectkind").AsInt32().NotNullable()
					.WithColumn("subjectid").AsCustom("citext").NotNullable()
					.WithColumn("revisionid").AsCustom("citext").Nullable()
					.WithColumn("signeruserid").AsCustom("citext").NotNullable()
					.WithColumn("signernamesnapshot").AsCustom("citext").Nullable()
					.WithColumn("signerrolesnapshot").AsCustom("citext").Nullable()
					.WithColumn("intent").AsInt32().NotNullable()
					.WithColumn("statementversion").AsCustom("citext").Nullable()
					.WithColumn("statementtext").AsCustom("citext").Nullable()
					.WithColumn("level").AsInt32().NotNullable()
					.WithColumn("method").AsInt32().NotNullable()
					.WithColumn("artifactsha256").AsString(64).NotNullable()
					.WithColumn("signedon").AsDateTime2().NotNullable()
					.WithColumn("ipaddress").AsCustom("citext").Nullable()
					.WithColumn("useragent").AsCustom("citext").Nullable()
					.WithColumn("passkeycredentialid").AsCustom("citext").Nullable()
					.WithColumn("stepupevidencereference").AsCustom("citext").Nullable()
					.WithColumn("voidedon").AsDateTime2().Nullable()
					.WithColumn("voidedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("voidreason").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_signaturerecords_subject ON signaturerecords (departmentid, subjectkind, subjectid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS signaturerecords;");
		}
	}
}
