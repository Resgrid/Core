namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDistributionList : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.DistributionListMembers",
                c => new
                    {
                        DistributionListMemberId = c.Int(nullable: false, identity: true),
                        DistributionListId = c.Int(nullable: false),
                        UserId = c.Guid(nullable: false),
                    })
                .PrimaryKey(t => t.DistributionListMemberId)
                .ForeignKey("dbo.DistributionLists", t => t.DistributionListId, cascadeDelete: true)
                .ForeignKey("dbo.Users", t => t.UserId)
                .Index(t => t.DistributionListId)
                .Index(t => t.UserId);
            
            CreateTable(
                "dbo.DistributionLists",
                c => new
                    {
                        DistributionListId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Name = c.String(nullable: false),
                        IsDisabled = c.Boolean(nullable: false),
                        Hostname = c.String(nullable: false, maxLength: 500),
                        Port = c.Int(nullable: false),
                        UseSsl = c.Boolean(nullable: false),
                        Username = c.String(maxLength: 125),
                        Password = c.String(maxLength: 125),
                        LastCheck = c.DateTime(),
                        IsFailure = c.Boolean(nullable: false),
                        ErrorMessage = c.String(maxLength: 1000),
                    })
                .PrimaryKey(t => t.DistributionListId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.DistributionListMembers", "UserId", "dbo.Users");
            DropForeignKey("dbo.DistributionListMembers", "DistributionListId", "dbo.DistributionLists");
            DropForeignKey("dbo.DistributionLists", "DepartmentId", "dbo.Departments");
            DropIndex("dbo.DistributionListMembers", new[] { "UserId" });
            DropIndex("dbo.DistributionListMembers", new[] { "DistributionListId" });
            DropIndex("dbo.DistributionLists", new[] { "DepartmentId" });
            DropTable("dbo.DistributionLists");
            DropTable("dbo.DistributionListMembers");
        }
    }
}
