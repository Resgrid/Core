using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Smoke / CO alarm tracking for community risk reduction (see the SQL Server M0284): five columns on
	/// rmscrractivities and the rmscrralarmrequests queue. Ids stay varchar like the other RMS-5 prevention tables (M0186);
	/// the request number is citext like the other prevention numbers (M0269). No foreign keys.
	/// </summary>
	[Migration(284)]
	public class M0284_AddCrrAlarmTrackingPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE rmscrractivities ADD COLUMN IF NOT EXISTS coalarmsinstalled integer NOT NULL DEFAULT 0;");
			Execute.Sql("ALTER TABLE rmscrractivities ADD COLUMN IF NOT EXISTS comboalarmsinstalled integer NOT NULL DEFAULT 0;");
			Execute.Sql("ALTER TABLE rmscrractivities ADD COLUMN IF NOT EXISTS alarmreason integer NOT NULL DEFAULT 0;");
			Execute.Sql("ALTER TABLE rmscrractivities ADD COLUMN IF NOT EXISTS callid integer NULL;");
			Execute.Sql("ALTER TABLE rmscrractivities ADD COLUMN IF NOT EXISTS rmscrralarmrequestid varchar(36) NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS \"IX_RmsCrrActivities_Department_Occupancy\" ON rmscrractivities (departmentid, rmsoccupancyid);");

			if (!Schema.Table("rmscrralarmrequests").Exists())
			{
				Create.Table("rmscrralarmrequests")
					.WithColumn("rmscrralarmrequestid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("requestnumber").AsCustom("citext").NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("source").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("reason").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("requestedon").AsDateTime2().NotNullable()
					.WithColumn("smokealarmsrequested").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("coalarmsrequested").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("comboalarmsrequested").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("rmsoccupancyid").AsString(36).Nullable()
					.WithColumn("locationtext").AsString(500).Nullable()
					.WithColumn("latitude").AsDecimal(12, 8).Nullable()
					.WithColumn("longitude").AsDecimal(12, 8).Nullable()
					.WithColumn("requestercontactid").AsString(128).Nullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("notes").AsCustom("text").Nullable()
					.WithColumn("scheduledfor").AsDateTime2().Nullable()
					.WithColumn("assignedtouserid").AsString(128).Nullable()
					.WithColumn("closedon").AsDateTime2().Nullable()
					.WithColumn("closedbyuserid").AsString(128).Nullable()
					.WithColumn("declinereason").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("resolutionnotes").AsCustom("text").Nullable()
					.WithColumn("rmscrractivityid").AsString(36).Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("modifiedbyuserid").AsString(128).Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Create.Index("IX_RmsCrrAlarmRequests_Department_Status").OnTable("rmscrralarmrequests").OnColumn("departmentid").Ascending().OnColumn("status").Ascending().OnColumn("requestedon").Ascending();
				Create.Index("IX_RmsCrrAlarmRequests_Department_Occupancy").OnTable("rmscrralarmrequests").OnColumn("departmentid").Ascending().OnColumn("rmsoccupancyid").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("rmscrralarmrequests").Exists())
				Delete.Table("rmscrralarmrequests");

			Execute.Sql("DROP INDEX IF EXISTS \"IX_RmsCrrActivities_Department_Occupancy\";");
			Execute.Sql("ALTER TABLE rmscrractivities DROP COLUMN IF EXISTS rmscrralarmrequestid;");
			Execute.Sql("ALTER TABLE rmscrractivities DROP COLUMN IF EXISTS callid;");
			Execute.Sql("ALTER TABLE rmscrractivities DROP COLUMN IF EXISTS alarmreason;");
			Execute.Sql("ALTER TABLE rmscrractivities DROP COLUMN IF EXISTS comboalarmsinstalled;");
			Execute.Sql("ALTER TABLE rmscrractivities DROP COLUMN IF EXISTS coalarmsinstalled;");
		}
	}
}
