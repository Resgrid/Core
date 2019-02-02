namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddingUniqueEmailCodeToExistingDepartments : DbMigration
    {
        public override void Up()
        {
			Sql(@"
					DECLARE @DepartmentId INT  
					DECLARE @EmailCode VARCHAR(40)
					DECLARE @UseCount INT  

					DECLARE db_cursor CURSOR FOR  
					SELECT DepartmentId 
					FROM Departments

					OPEN db_cursor
					FETCH NEXT FROM db_cursor INTO @DepartmentId

					WHILE @@FETCH_STATUS = 0   
					BEGIN
	
						SET @EmailCode = (SELECT SUBSTRING(CONVERT(varchar(40), NEWID()),0,7))
						SET @UseCount = (SELECT COUNT(*) FROM [dbo].[DepartmentSettings] WHERE Setting = @EmailCode)

						WHILE @UseCount > 0
						BEGIN
							SET @EmailCode = (SELECT SUBSTRING(CONVERT(varchar(40), NEWID()),0,7))
							SET @UseCount = (SELECT COUNT(*) FROM [dbo].[DepartmentSettings] WHERE Setting = @EmailCode)
						END

						INSERT INTO [dbo].[DepartmentSettings]
							   ([DepartmentId]
							   ,[SettingType]
							   ,[Setting])
						 VALUES
							   (@DepartmentId
							   ,15
							   ,@EmailCode)

						FETCH NEXT FROM db_cursor INTO @DepartmentId   
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
