using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0394_AddExposureIncidents (member wellness WL1, §13b): rmsexposureincidents. Member ids are citext so the
	/// case-insensitive lookups use their index; protected columns are text. No foreign keys; guarded for a safe retry; Down drops the table.
	/// </summary>
	[Migration(394)]
	public class M0394_AddExposureIncidentsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsexposureincidents").Exists())
			{
				Create.Table("rmsexposureincidents")
					.WithColumn("rmsexposureincidentid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("responderexposureid").AsString(36).Nullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("route").AsInt32().NotNullable()
					.WithColumn("occurredon").AsDateTime2().NotNullable()
					.WithColumn("stage").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("reportedon").AsDateTime2().NotNullable()
					.WithColumn("reportedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("sourcefacility").AsString(200).Nullable()
					.WithColumn("sourcerequestedon").AsDateTime2().Nullable()
					.WithColumn("notificationrequestedon").AsDateTime2().Nullable()
					.WithColumn("notificationdueon").AsDateTime2().Nullable()
					.WithColumn("notificationreceivedon").AsDateTime2().Nullable()
					.WithColumn("notificationoverduenoticeon").AsDateTime2().Nullable()
					.WithColumn("followupstartedon").AsDateTime2().Nullable()
					.WithColumn("closedon").AsDateTime2().Nullable()
					.WithColumn("closedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("withdrawnon").AsDateTime2().Nullable()
					.WithColumn("withdrawnbyuserid").AsCustom("citext").Nullable()
					.WithColumn("sourcefollowupnotes").AsCustom("text").Nullable()
					.WithColumn("notificationresponse").AsCustom("text").Nullable()
					.WithColumn("medicalnotes").AsCustom("text").Nullable()
					.WithColumn("closurenotes").AsCustom("text").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("modifiedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1);
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsexposureincidents_user ON rmsexposureincidents (departmentid, userid, occurredon);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsexposureincidents_exposure ON rmsexposureincidents (departmentid, responderexposureid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsexposureincidents_overdue ON rmsexposureincidents (stage, notificationdueon);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsexposureincidents_occurred ON rmsexposureincidents (occurredon, departmentid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS rmsexposureincidents;");
		}
	}
}
