using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(59)]
	public class M0059_AddActiveForStatesToCheckInTimersPg : Migration
	{
		public override void Up()
		{
			Alter.Table("checkintimerconfigs")
				.AddColumn("activeforstates").AsCustom("citext").Nullable();

			Alter.Table("checkintimeroverrides")
				.AddColumn("activeforstates").AsCustom("citext").Nullable();
		}

		public override void Down()
		{
			Delete.Column("activeforstates").FromTable("checkintimerconfigs");
			Delete.Column("activeforstates").FromTable("checkintimeroverrides");
		}
	}
}
