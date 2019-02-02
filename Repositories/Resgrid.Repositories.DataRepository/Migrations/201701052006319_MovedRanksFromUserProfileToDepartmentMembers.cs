namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class MovedRanksFromUserProfileToDepartmentMembers : DbMigration
    {
        public override void Up()
        {
            DropForeignKey("dbo.UserProfiles", "RankId", "dbo.Ranks");
            DropIndex("dbo.UserProfiles", new[] { "RankId" });
            AddColumn("dbo.DepartmentMembers", "RankId", c => c.Int());
            CreateIndex("dbo.DepartmentMembers", "RankId");
            AddForeignKey("dbo.DepartmentMembers", "RankId", "dbo.Ranks", "RankId");
            DropColumn("dbo.UserProfiles", "RankId");
        }
        
        public override void Down()
        {
            AddColumn("dbo.UserProfiles", "RankId", c => c.Int());
            DropForeignKey("dbo.DepartmentMembers", "RankId", "dbo.Ranks");
            DropIndex("dbo.DepartmentMembers", new[] { "RankId" });
            DropColumn("dbo.DepartmentMembers", "RankId");
            CreateIndex("dbo.UserProfiles", "RankId");
            AddForeignKey("dbo.UserProfiles", "RankId", "dbo.Ranks", "RankId");
        }
    }
}
