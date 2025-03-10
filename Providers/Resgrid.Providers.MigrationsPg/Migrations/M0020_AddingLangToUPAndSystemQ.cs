using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(20)]
	public class M0020_AddingLangToUPAndSystemQ : Migration
	{
		public override void Up()
		{
			// Adding Language to User Profile
			Alter.Table("UserProfiles".ToLower()).AddColumn("Language".ToLower()).AsCustom("citext").Nullable();

			// Updating Queue Item to handle System Queue stuff (i.e. Delete Account and Department requests)
			Alter.Table("QueueItems".ToLower()).AddColumn("ToBeCompletedOn".ToLower()).AsDateTime2().Nullable();
			Alter.Table("QueueItems".ToLower()).AddColumn("Reason".ToLower()).AsCustom("citext").Nullable();
			Alter.Table("QueueItems".ToLower()).AddColumn("QueuedByUserId".ToLower()).AsCustom("citext").Nullable();
			Alter.Table("QueueItems".ToLower()).AddColumn("Data".ToLower()).AsCustom("citext").Nullable();
			Alter.Table("QueueItems".ToLower()).AddColumn("ReminderCount".ToLower()).AsInt32().Nullable();
		}

		public override void Down()
		{

		}
	}
}
