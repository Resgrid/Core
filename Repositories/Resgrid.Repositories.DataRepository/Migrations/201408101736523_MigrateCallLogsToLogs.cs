namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class MigrateCallLogsToLogs : DbMigration
    {
        public override void Up()
        {
			Sql(@"
					DECLARE @CallLogId INT

					DECLARE db_cursor CURSOR FOR  
					SELECT CallLogId 
					FROM CallLogs

					OPEN db_cursor
					FETCH NEXT FROM db_cursor INTO @CallLogId   

					WHILE @@FETCH_STATUS = 0   
					BEGIN
						  INSERT INTO [dbo].[Logs]
							   ([DepartmentId]
							   ,[Narrative]
							   ,[StartedOn]
							   ,[EndedOn]
							   ,[LoggedOn]
							   ,[LoggedByUserId]
							   ,[LogType]
							   ,[ExternalId]
							   ,[InitialReport]
							   ,[Type]
							   ,[StationGroupId]
							   ,[Course]
							   ,[CourseCode]
							   ,[Instructors]
							   ,[Cause]
							   ,[InvestigatedByUserId]
							   ,[ContactName]
							   ,[ContactNumber]
							   ,[OfficerUserId]
							   ,[CallId])
						 VALUES
							   ((SELECT [DepartmentId] FROM [dbo].[CallLogs] WHERE CallLogId = @CallLogId)
							   ,(SELECT [Narrative] FROM [dbo].[CallLogs] WHERE CallLogId = @CallLogId)
							   ,NULL
							   ,NULL
							   ,(SELECT [LoggedOn] FROM [dbo].[CallLogs] WHERE CallLogId = @CallLogId)
							   ,(SELECT [LoggedByUserId] FROM [dbo].[CallLogs] WHERE CallLogId = @CallLogId)
							   ,1
							   ,NULL
							   ,''
							   ,NULL
							   ,NULL
							   ,NULL
							   ,NULL
							   ,NULL
							   ,NULL
							   ,NULL
							   ,NULL
							   ,NULL
							   ,NULL
							   ,(SELECT [CallId] FROM [dbo].[CallLogs] WHERE CallLogId = @CallLogId))
  
						FETCH NEXT FROM db_cursor INTO @CallLogId   
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
