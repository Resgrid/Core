namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedScheduleTasksTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.ScheduledTasks",
                c => new
                    {
                        UserScheduleId = c.Int(nullable: false, identity: true),
                        UserId = c.Guid(nullable: false),
                        ScheduleType = c.Int(nullable: false),
                        SpecifcDate = c.DateTime(nullable: false),
                        Sunday = c.Boolean(nullable: false),
                        Monday = c.Boolean(nullable: false),
                        Tuesday = c.Boolean(nullable: false),
                        Wednesday = c.Boolean(nullable: false),
                        Thursday = c.Boolean(nullable: false),
                        Friday = c.Boolean(nullable: false),
                        Saturday = c.Boolean(nullable: false),
                        Hours = c.Int(nullable: false),
                        Minutes = c.Int(nullable: false),
                        Active = c.Boolean(nullable: false),
                        TaskType = c.Int(nullable: false),
                        Data = c.String(),
                    })
                .PrimaryKey(t => t.UserScheduleId)
                .ForeignKey("dbo.Users", t => t.UserId, cascadeDelete: true)
                .Index(t => t.UserId);
            
        }
        
        public override void Down()
        {
            DropIndex("dbo.ScheduledTasks", new[] { "UserId" });
            DropForeignKey("dbo.ScheduledTasks", "UserId", "dbo.Users");
            DropTable("dbo.ScheduledTasks");
        }
    }
}
