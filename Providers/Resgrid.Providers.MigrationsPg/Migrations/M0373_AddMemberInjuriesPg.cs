using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0373_AddMemberInjuries (member wellness WL1, §13b): rmsmemberinjuries and rmsinjurydutyperiods. Member ids are
	/// citext; protected columns are text; case numbers are unique per department and OSHA year. No foreign keys; guarded for a safe retry;
	/// Down drops both tables.
	/// </summary>
	[Migration(373)]
	public class M0373_AddMemberInjuriesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsmemberinjuries").Exists())
			{
				Create.Table("rmsmemberinjuries")
					.WithColumn("rmsmemberinjuryid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("exposureincidentid").AsString(36).Nullable()
					.WithColumn("occurredon").AsDateTime2().NotNullable()
					.WithColumn("reportedon").AsDateTime2().NotNullable()
					.WithColumn("reportedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("oshayear").AsInt32().NotNullable()
					.WithColumn("oshacasenumber").AsInt32().NotNullable()
					.WithColumn("location").AsString(200).Nullable()
					.WithColumn("jobtitle").AsString(100).Nullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("dutystatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("dutystatussince").AsDateTime2().Nullable()
					.WithColumn("expectedreturnon").AsDateTime2().Nullable()
					.WithColumn("returnreviewnoticeon").AsDateTime2().Nullable()
					.WithColumn("returnedtodutyon").AsDateTime2().Nullable()
					.WithColumn("clearedon").AsDateTime2().Nullable()
					.WithColumn("clearedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("isrecordable").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("oshainjurytype").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprivacycase").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("treatedinemergencyroom").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("hospitalizedovernight").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("diedon").AsDateTime2().Nullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("injurydetails").AsCustom("text").Nullable()
					.WithColumn("restrictions").AsCustom("text").Nullable()
					.WithColumn("treatmentdetails").AsCustom("text").Nullable()
					.WithColumn("clearancenotes").AsCustom("text").Nullable()
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

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsmemberinjuries_user ON rmsmemberinjuries (departmentid, userid, occurredon);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsmemberinjuries_case ON rmsmemberinjuries (departmentid, oshayear, oshacasenumber);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsmemberinjuries_review ON rmsmemberinjuries (state, expectedreturnon);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsmemberinjuries_year ON rmsmemberinjuries (oshayear, departmentid);");

			if (!Schema.Table("rmsinjurydutyperiods").Exists())
			{
				Create.Table("rmsinjurydutyperiods")
					.WithColumn("rmsinjurydutyperiodid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("injuryid").AsString(36).NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("dutystatus").AsInt32().NotNullable()
					.WithColumn("startson").AsDateTime2().NotNullable()
					.WithColumn("endson").AsDateTime2().Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsinjurydutyperiods_injury ON rmsinjurydutyperiods (departmentid, injuryid, startson);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS rmsinjurydutyperiods;");
			Execute.Sql("DROP TABLE IF EXISTS rmsmemberinjuries;");
		}
	}
}
