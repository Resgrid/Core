namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class ModifiedSomeDepProfileTables : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentProfileArticles", "Deleted", c => c.Boolean(nullable: false));
            AddColumn("dbo.DepartmentProfileMessages", "ReplyToMessageId", c => c.Int());
            AddColumn("dbo.DepartmentProfileMessages", "ReadOn", c => c.DateTime());
            AddColumn("dbo.DepartmentProfileMessages", "CallId", c => c.Int());
            AddColumn("dbo.DepartmentProfileMessages", "Spam", c => c.Boolean(nullable: false));
            AddColumn("dbo.DepartmentProfileMessages", "Deleted", c => c.Boolean(nullable: false));
            CreateIndex("dbo.DepartmentProfileMessages", "ReplyToMessageId");
            CreateIndex("dbo.DepartmentProfileMessages", "CallId");
            AddForeignKey("dbo.DepartmentProfileMessages", "CallId", "dbo.Calls", "CallId");
            AddForeignKey("dbo.DepartmentProfileMessages", "ReplyToMessageId", "dbo.DepartmentProfileMessages", "DepartmentProfileMessageId");
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.DepartmentProfileMessages", "ReplyToMessageId", "dbo.DepartmentProfileMessages");
            DropForeignKey("dbo.DepartmentProfileMessages", "CallId", "dbo.Calls");
            DropIndex("dbo.DepartmentProfileMessages", new[] { "CallId" });
            DropIndex("dbo.DepartmentProfileMessages", new[] { "ReplyToMessageId" });
            DropColumn("dbo.DepartmentProfileMessages", "Deleted");
            DropColumn("dbo.DepartmentProfileMessages", "Spam");
            DropColumn("dbo.DepartmentProfileMessages", "CallId");
            DropColumn("dbo.DepartmentProfileMessages", "ReadOn");
            DropColumn("dbo.DepartmentProfileMessages", "ReplyToMessageId");
            DropColumn("dbo.DepartmentProfileArticles", "Deleted");
        }
    }
}
