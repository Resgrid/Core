using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>Recurring calls, second part; see the SQL Server M0320_ExtendRecurringCalls.</summary>
	[Migration(320)]
	public class M0320_ExtendRecurringCallsPg : Migration
	{
		public override void Up()
		{
			AddColumn("description", "citext NULL");
			AddColumn("schedulecontactname", "citext NULL");
			AddColumn("schedulecontactnumber", "citext NULL");
			AddColumn("schedulecontactemail", "citext NULL");
			AddColumn("facilityname", "citext NULL");
			AddColumn("facilitydetails", "citext NULL");
			AddColumn("facilitypoiid", "integer NULL");
			AddColumn("destinationname", "citext NULL");
			AddColumn("destinationdetails", "citext NULL");
			AddColumn("creationmode", "integer NOT NULL DEFAULT 0");
			AddColumn("manualalertminutes", "integer NOT NULL DEFAULT 15");
			AddColumn("useruncard", "boolean NOT NULL DEFAULT false");
			AddColumn("alertuserids", "citext NULL");
			AddColumn("alertgroupids", "citext NULL");
			AddColumn("alertroleids", "citext NULL");
			AddColumn("pausedon", "timestamp without time zone NULL");
			AddColumn("pausedbyuserid", "citext NULL");
			AddColumn("pausereason", "citext NULL");
			AddColumn("resumeon", "timestamp without time zone NULL");

			if (!Schema.Table("recurringcallalerts").Exists())
			{
				Create.Table("recurringcallalerts")
					.WithColumn("recurringcallalertid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("recurringcallid").AsInt32().NotNullable()
					.WithColumn("occurrenceon").AsDateTime2().NotNullable()
					.WithColumn("kind").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("detailjson").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("notifiedon").AsDateTime2().Nullable()
					.WithColumn("notificationclaimedon").AsDateTime2().Nullable()
					.WithColumn("acknowledgedon").AsDateTime2().Nullable()
					.WithColumn("acknowledgedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("resolvedon").AsDateTime2().Nullable()
					.WithColumn("resolvedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("resolution").AsInt32().Nullable()
					.WithColumn("resolutionnote").AsCustom("citext").Nullable();
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_recurringcallalerts_occurrence ON recurringcallalerts (recurringcallid, occurrenceon, kind) WHERE resolvedon IS NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_recurringcallalerts_open ON recurringcallalerts (departmentid, createdon) WHERE resolvedon IS NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_recurringcallalerts_department ON recurringcallalerts (departmentid, resolvedon);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS recurringcallalerts;");

			foreach (var column in new[] { "description", "schedulecontactname", "schedulecontactnumber", "schedulecontactemail", "facilityname",
						 "facilitydetails", "facilitypoiid", "destinationname", "destinationdetails", "creationmode", "manualalertminutes", "useruncard",
						 "alertuserids", "alertgroupids", "alertroleids", "pausedon", "pausedbyuserid", "pausereason", "resumeon" })
				Execute.Sql($"ALTER TABLE recurringcalls DROP COLUMN IF EXISTS {column};");
		}

		private void AddColumn(string column, string definition)
		{
			Execute.Sql($"ALTER TABLE recurringcalls ADD COLUMN IF NOT EXISTS {column} {definition};");
		}
	}
}
