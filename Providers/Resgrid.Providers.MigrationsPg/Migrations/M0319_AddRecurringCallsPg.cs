using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>Recurring calls; see the SQL Server M0319_AddRecurringCalls.</summary>
	[Migration(319)]
	public class M0319_AddRecurringCallsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("recurringcalls").Exists())
			{
				Create.Table("recurringcalls")
					.WithColumn("recurringcallid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("frequency").AsInt32().NotNullable()
					.WithColumn("repeatinterval").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("daysofweek").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("monthlyday").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("monthlyweek").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("monthlyweekday").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("times").AsCustom("citext").NotNullable()
					.WithColumn("startson").AsDateTime2().NotNullable()
					.WithColumn("endson").AsDateTime2().Nullable()
					.WithColumn("leadtimeminutes").AsInt32().NotNullable()
					.WithColumn("natureofcall").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("address").AsCustom("citext").Nullable()
					.WithColumn("geolocationdata").AsCustom("citext").Nullable()
					.WithColumn("priority").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("type").AsCustom("citext").Nullable()
					.WithColumn("contactname").AsCustom("citext").Nullable()
					.WithColumn("contactnumber").AsCustom("citext").Nullable()
					.WithColumn("externalidentifier").AsCustom("citext").Nullable()
					.WithColumn("referencenumber").AsCustom("citext").Nullable()
					.WithColumn("incidentnumber").AsCustom("citext").Nullable()
					.WithColumn("mappage").AsCustom("citext").Nullable()
					.WithColumn("destinationpoiid").AsInt32().Nullable()
					.WithColumn("checkintimersenabled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("dispatchuserids").AsCustom("citext").Nullable()
					.WithColumn("dispatchgroupids").AsCustom("citext").Nullable()
					.WithColumn("dispatchunitids").AsCustom("citext").Nullable()
					.WithColumn("dispatchroleids").AsCustom("citext").Nullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("updatedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("updatedon").AsDateTime2().Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_recurringcalls_department ON recurringcalls (departmentid);");
			}

			if (!Schema.Table("recurringcalloccurrences").Exists())
			{
				Create.Table("recurringcalloccurrences")
					.WithColumn("recurringcalloccurrenceid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("recurringcallid").AsInt32().NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("occurrenceon").AsDateTime2().NotNullable()
					.WithColumn("state").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("userid").AsCustom("citext").Nullable()
					.WithColumn("updatedon").AsDateTime2().NotNullable();

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_recurringcalloccurrences_occurrence ON recurringcalloccurrences (recurringcallid, occurrenceon);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_recurringcalloccurrences_department ON recurringcalloccurrences (departmentid, occurrenceon);");
			}

			Execute.Sql("ALTER TABLE calls ADD COLUMN IF NOT EXISTS recurringcallid integer NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_calls_recurringcallid ON calls (recurringcallid, dispatchon) WHERE recurringcallid IS NOT NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_calls_dispatchon ON calls (departmentid, dispatchon) WHERE dispatchon IS NOT NULL;");
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS ix_calls_dispatchon;");
			Execute.Sql("DROP INDEX IF EXISTS ix_calls_recurringcallid;");
			Execute.Sql("ALTER TABLE calls DROP COLUMN IF EXISTS recurringcallid;");

			if (Schema.Table("recurringcalloccurrences").Exists())
				Delete.Table("recurringcalloccurrences");

			if (Schema.Table("recurringcalls").Exists())
				Delete.Table("recurringcalls");
		}
	}
}
