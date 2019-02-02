namespace Resgrid.Repositories.DataRepository.Migrations
{
	using System;
	using System.Data.Entity.Migrations;

	public partial class ForceReminderSentOnOldCalendarItems : DbMigration
	{
		public override void Up()
		{
			Sql(@"	
						UPDATE CalendarItems
						SET ReminderSent = 1
					");
		}

		public override void Down()
		{
		}
	}
}
