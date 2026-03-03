using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(47)]
	public class M0047_AddingCalendarSyncToken : Migration
	{
		public override void Up()
		{
			// Add CalendarSyncToken column to UserProfiles.
			// This nullable GUID string is used as part of the encrypted external calendar
			// subscription URL token. When null the user has not activated calendar sync.
			Alter.Table("UserProfiles")
				.AddColumn("CalendarSyncToken").AsString(128).Nullable();
		}

		public override void Down()
		{
			Delete.Column("CalendarSyncToken").FromTable("UserProfiles");
		}
	}
}

