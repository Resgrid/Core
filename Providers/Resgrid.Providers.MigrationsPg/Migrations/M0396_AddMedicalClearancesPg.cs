using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0396_AddMedicalClearances (member wellness WL1, §13b): rmsmedicalclearances. Member ids are citext; the restriction
	/// detail is text. No foreign keys; guarded for a safe retry; Down drops the table.
	/// </summary>
	[Migration(396)]
	public class M0396_AddMedicalClearancesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsmedicalclearances").Exists())
			{
				Create.Table("rmsmedicalclearances")
					.WithColumn("rmsmedicalclearanceid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("personnelcertificationid").AsInt32().Nullable()
					.WithColumn("status").AsInt32().NotNullable()
					.WithColumn("evaluatedon").AsDateTime2().NotNullable()
					.WithColumn("validuntil").AsDateTime2().Nullable()
					.WithColumn("restrictiondetail").AsCustom("text").Nullable()
					.WithColumn("voidedon").AsDateTime2().Nullable()
					.WithColumn("voidedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("modifiedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1);
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsmedicalclearances_user ON rmsmedicalclearances (departmentid, userid, evaluatedon);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsmedicalclearances_evaluated ON rmsmedicalclearances (evaluatedon, departmentid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS rmsmedicalclearances;");
		}
	}
}
