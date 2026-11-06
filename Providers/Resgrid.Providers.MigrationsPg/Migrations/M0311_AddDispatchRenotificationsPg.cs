using System.Data;
using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Dispatch re-notification (DepartmentSettingTypes.DispatchRenotifyConfig, worker 78): one row per person or unit a call
	/// broadcast reached while the department re-notifies, advanced by the sweep until the recipient sets a status, the attempts
	/// run out, or the row no longer applies. Ids and times only. Rows cascade with their call; the sweep purges finished rows.
	/// </summary>
	[Migration(311)]
	public class M0311_AddDispatchRenotificationsPg : Migration
	{
		private const string Table = "dispatchrenotifications";

		public override void Up()
		{
			if (!Schema.Table(Table).Exists())
			{
				Create.Table(Table)
					.WithColumn("dispatchrenotificationid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().NotNullable()
					.WithColumn("recipienttype").AsInt32().NotNullable()
					.WithColumn("recipientid").AsCustom("citext").NotNullable()
					.WithColumn("sourcetype").AsInt32().NotNullable()
					.WithColumn("sourceid").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("acknowledgeafter").AsDateTime2().NotNullable()
					.WithColumn("nextnotifyon").AsDateTime2().Nullable()
					.WithColumn("lastnotifiedon").AsDateTime2().Nullable()
					.WithColumn("acknowledgedon").AsDateTime2().Nullable()
					.WithColumn("completedon").AsDateTime2().Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable();

				Create.ForeignKey("fk_dispatchrenotifications_calls")
					.FromTable(Table).ForeignColumn("callid")
					.ToTable("calls").PrimaryColumn("callid")
					.OnDelete(Rule.Cascade);
			}

			Execute.Sql(@"
				CREATE UNIQUE INDEX IF NOT EXISTS ux_dispatchrenotifications_call_recipient ON dispatchrenotifications (callid, recipienttype, recipientid);
				CREATE INDEX IF NOT EXISTS ix_dispatchrenotifications_status_nextnotifyon ON dispatchrenotifications (status, nextnotifyon);
				CREATE INDEX IF NOT EXISTS ix_dispatchrenotifications_completedon ON dispatchrenotifications (completedon);");
		}

		public override void Down()
		{
			if (Schema.Table(Table).Exists())
				Delete.Table(Table);
		}
	}
}
