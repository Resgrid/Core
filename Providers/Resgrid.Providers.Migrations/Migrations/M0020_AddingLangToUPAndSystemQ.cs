using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(20)]
	public class M0020_AddingLangToUPAndSystemQ : Migration
	{
		public override void Up()
		{
			// Adding Language to User Profile
			Alter.Table("UserProfiles").AddColumn("Language").AsString().Nullable();

			// Updating Queue Item to handle System Queue stuff (i.e. Delete Account and Department requests)
			Alter.Table("QueueItems").AddColumn("ToBeCompletedOn").AsDateTime2().Nullable();
			Alter.Table("QueueItems").AddColumn("Reason").AsString().Nullable();
			Alter.Table("QueueItems").AddColumn("QueuedByUserId").AsString(128).Nullable();
			Alter.Table("QueueItems").AddColumn("Data").AsString().Nullable();
			Alter.Table("QueueItems").AddColumn("ReminderCount").AsInt32().Nullable();
		}

		public override void Down()
		{

		}
	}
}
