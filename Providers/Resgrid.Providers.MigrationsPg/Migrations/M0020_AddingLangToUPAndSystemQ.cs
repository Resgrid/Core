using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(20)]
	public class M0020_AddingLangToUPAndSystemQ : Migration
	{
		public override void Up()
		{
			// Adding Language to User Profile
			Alter.Table("UserProfiles").AddColumn("Language").AsCustom("citext").Nullable();

			// Updating Queue Item to handle System Queue stuff (i.e. Delete Account and Department requests)
			Alter.Table("QueueItems").AddColumn("ToBeCompletedOn").AsDateTime2().Nullable();
			Alter.Table("QueueItems").AddColumn("Reason").AsCustom("citext").Nullable();
			Alter.Table("QueueItems").AddColumn("QueuedByUserId").AsCustom("citext").Nullable();
			Alter.Table("QueueItems").AddColumn("Data").AsCustom("citext").Nullable();
			Alter.Table("QueueItems").AddColumn("ReminderCount").AsInt32().Nullable();
		}

		public override void Down()
		{

		}
	}
}
