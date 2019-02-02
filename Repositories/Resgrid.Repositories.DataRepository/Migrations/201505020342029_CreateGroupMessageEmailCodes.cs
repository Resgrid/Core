namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class CreateGroupMessageEmailCodes : DbMigration
    {
        public override void Up()
        {
					Sql(@"
					DECLARE @GroupId INT  
					DECLARE @EmailCode VARCHAR(40)
					DECLARE @UseCount INT  

					DECLARE db_cursor CURSOR FOR  
					SELECT DepartmentGroupId
					FROM DepartmentGroups

					OPEN db_cursor
					FETCH NEXT FROM db_cursor INTO @GroupId

					WHILE @@FETCH_STATUS = 0   
					BEGIN
	
						SET @EmailCode = (SELECT SUBSTRING(CONVERT(varchar(40), NEWID()),0,7))
						SET @UseCount = (SELECT COUNT(*) FROM DepartmentGroups WHERE DispatchEmail = @EmailCode)

						WHILE @UseCount > 0
						BEGIN
							SET @EmailCode = (SELECT SUBSTRING(CONVERT(varchar(40), NEWID()),0,7))
							SET @UseCount = (SELECT COUNT(*) FROM DepartmentGroups WHERE DispatchEmail = @EmailCode)
						END

						UPDATE [dbo].[DepartmentGroups]
					    SET MessageEmail = @EmailCode
					    WHERE DepartmentGroupId = @GroupId

						FETCH NEXT FROM db_cursor INTO @GroupId   
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
