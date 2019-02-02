namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class CreateDepartmentLinkCode : DbMigration
    {
        public override void Up()
        {
			Sql(@"
					DECLARE @DepartmentId INT  
					DECLARE @LinkCode VARCHAR(40)
					DECLARE @UseCount INT  

					DECLARE db_cursor CURSOR FOR  
					SELECT DepartmentId 
					FROM Departments

					OPEN db_cursor
					FETCH NEXT FROM db_cursor INTO @DepartmentId

					WHILE @@FETCH_STATUS = 0   
					BEGIN

						SET @LinkCode = (SELECT SUBSTRING(CONVERT(varchar(40), NEWID()),0,7))
						SET @UseCount = (SELECT COUNT(*) FROM [dbo].[Departments] WHERE LinkCode = @LinkCode)

						WHILE @UseCount > 0
						BEGIN
							SET @LinkCode = (SELECT SUBSTRING(CONVERT(varchar(40), NEWID()),0,7))
							SET @UseCount = (SELECT COUNT(*) FROM [dbo].[Departments] WHERE LinkCode = @LinkCode)
						END

						UPDATE [dbo].[Departments]
						SET LinkCode = @LinkCode
						WHERE DepartmentId = @DepartmentId

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
