namespace Resgrid.Repositories.DataRepository.Migrations
{
	using System;
	using System.Data.Entity.Migrations;

	public partial class MigratingCallLoggedOnValueToLogStartedOn : DbMigration
	{
		public override void Up()
		{
			Sql(@"	UPDATE Logs
							SET StartedOn = c.LoggedOn
							FROM Logs l
							INNER JOIN Calls c ON l.CallId = c.CallId
							WHERE LogType = 1
					");
		}

		public override void Down()
		{
		}
	}
}
