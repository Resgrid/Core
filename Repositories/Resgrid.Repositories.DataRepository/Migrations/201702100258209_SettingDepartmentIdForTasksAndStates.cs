namespace Resgrid.Repositories.DataRepository.Migrations
{
	using System;
	using System.Data.Entity.Migrations;

	public partial class SettingDepartmentIdForTasksAndStates : DbMigration
	{
		public override void Up()
		{
			Sql(@"DECLARE @StaffingId INT

						DECLARE db_cursor CURSOR FOR  
						SELECT UserStateId 
						FROM UserStates
						WHERE DepartmentId = 0

						OPEN db_cursor
						FETCH NEXT FROM db_cursor INTO @StaffingId   

						WHILE @@FETCH_STATUS = 0
						BEGIN

							UPDATE UserStates 
							SET DepartmentId = dm.DepartmentId
							FROM (SELECT DepartmentId, UserId FROM DepartmentMembers) dm
							WHERE dm.UserId = UserStates.UserId AND UserStateId = @StaffingId

							FETCH NEXT FROM db_cursor INTO @StaffingId   
						END

						CLOSE db_cursor
						DEALLOCATE db_cursor



						DELETE FROM UserStates
						WHERE DepartmentId = 0



						DECLARE @ScheduleId INT

						DECLARE db_cursor CURSOR FOR  
						SELECT ScheduledTaskId
						FROM ScheduledTasks
						WHERE DepartmentId = 0

						OPEN db_cursor
						FETCH NEXT FROM db_cursor INTO @ScheduleId   

						WHILE @@FETCH_STATUS = 0
						BEGIN

							UPDATE ScheduledTasks
							SET DepartmentId = dm.DepartmentId
							FROM (SELECT DepartmentId, UserId FROM DepartmentMembers) dm
							WHERE ScheduledTasks.UserId = dm.UserId AND ScheduledTaskId = @ScheduleId

							FETCH NEXT FROM db_cursor INTO @ScheduleId   
						END   

						CLOSE db_cursor
						DEALLOCATE db_cursor
						");
		}

		public override void Down()
		{
		}
	}
}
