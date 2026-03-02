using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(47)]
	public class M0047_AddingCalendarSyncTokenPg : Migration
	{
		public override void Up()
		{
			// Add CalendarSyncToken column to UserProfiles (Postgres).
			// citext is used instead of a length-limited string type, matching all other Pg migrations.
			Alter.Table("UserProfiles".ToLower()).AddColumn("CalendarSyncToken".ToLower()).AsCustom("citext").Nullable();
		}

		public override void Down()
		{
			Delete.Column("CalendarSyncToken".ToLower()).FromTable("UserProfiles".ToLower());
		}
	}
}
