namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDefaultCallPriorites : DbMigration
    {
        public override void Up()
        {
			Sql(@"SET IDENTITY_INSERT [dbo].[DepartmentCallPriorities] ON;

					INSERT INTO [dbo].[DepartmentCallPriorities]
							   ([DepartmentCallPriorityId]
							   ,[DepartmentId]
							   ,[Name]
							   ,[Color]
							   ,[Sort]
							   ,[IsDeleted]
							   ,[IsDefault])
						 VALUES
							   (0
							   ,1
							   ,'Low'
							   ,'#028602'
							   ,0
							   ,0
							   ,0)

					INSERT INTO [dbo].[DepartmentCallPriorities]
							   ([DepartmentCallPriorityId]
							   ,[DepartmentId]
							   ,[Name]
							   ,[Color]
							   ,[Sort]
							   ,[IsDeleted]
							   ,[IsDefault])
						 VALUES
							   (1
							   ,1
							   ,'Medium'
							   ,'#DBDB2E'
							   ,1
							   ,0
							   ,0)

					INSERT INTO [dbo].[DepartmentCallPriorities]
							   ([DepartmentCallPriorityId]
							   ,[DepartmentId]
							   ,[Name]
							   ,[Color]
							   ,[Sort]
							   ,[IsDeleted]
							   ,[IsDefault])
						 VALUES
							   (2
							   ,1
							   ,'High'
							   ,'#f9a203'
							   ,2
							   ,0
							   ,0)

					INSERT INTO [dbo].[DepartmentCallPriorities]
							   ([DepartmentCallPriorityId]
							   ,[DepartmentId]
							   ,[Name]
							   ,[Color]
							   ,[Sort]
							   ,[IsDeleted]
							   ,[IsDefault])
						 VALUES
							   (3
							   ,1
							   ,'Emergency'
							   ,'#fd0303'
							   ,3
							   ,0
							   ,1)

					SET IDENTITY_INSERT [dbo].[DepartmentCallPriorities] OFF;");
        }
        
        public override void Down()
        {
        }
    }
}
