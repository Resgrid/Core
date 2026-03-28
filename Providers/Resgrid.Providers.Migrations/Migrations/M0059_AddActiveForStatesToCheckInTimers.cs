using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(59)]
	public class M0059_AddActiveForStatesToCheckInTimers : Migration
	{
		public override void Up()
		{
			Alter.Table("CheckInTimerConfigs")
				.AddColumn("ActiveForStates").AsString(200).Nullable();

			Alter.Table("CheckInTimerOverrides")
				.AddColumn("ActiveForStates").AsString(200).Nullable();
		}

		public override void Down()
		{
			Delete.Column("ActiveForStates").FromTable("CheckInTimerConfigs");
			Delete.Column("ActiveForStates").FromTable("CheckInTimerOverrides");
		}
	}
}
