namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedRankTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Ranks",
                c => new
                    {
                        RankId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Name = c.String(),
                        Code = c.String(),
                        SortWeight = c.Int(nullable: false),
                        TradeWeight = c.Int(nullable: false),
                        Image = c.Binary(),
                        Color = c.String(),
                    })
                .PrimaryKey(t => t.RankId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
            AddColumn("dbo.UserProfiles", "RankId", c => c.Int());
            CreateIndex("dbo.UserProfiles", "RankId");
            AddForeignKey("dbo.UserProfiles", "RankId", "dbo.Ranks", "RankId");
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.UserProfiles", "RankId", "dbo.Ranks");
            DropForeignKey("dbo.Ranks", "DepartmentId", "dbo.Departments");
            DropIndex("dbo.UserProfiles", new[] { "RankId" });
            DropIndex("dbo.Ranks", new[] { "DepartmentId" });
            DropColumn("dbo.UserProfiles", "RankId");
            DropTable("dbo.Ranks");
        }
    }
}
