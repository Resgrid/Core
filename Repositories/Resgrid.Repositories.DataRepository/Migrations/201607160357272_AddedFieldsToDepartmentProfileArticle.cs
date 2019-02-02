namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedFieldsToDepartmentProfileArticle : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentProfileArticles", "Keywords", c => c.String());
            AddColumn("dbo.DepartmentProfileArticles", "CreatedByUserId", c => c.Guid(nullable: false));
            AddColumn("dbo.DepartmentProfileArticles", "StartOn", c => c.DateTime(nullable: false));
            CreateIndex("dbo.DepartmentProfileArticles", "CreatedByUserId");
            AddForeignKey("dbo.DepartmentProfileArticles", "CreatedByUserId", "dbo.Users", "UserId");
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.DepartmentProfileArticles", "CreatedByUserId", "dbo.Users");
            DropIndex("dbo.DepartmentProfileArticles", new[] { "CreatedByUserId" });
            DropColumn("dbo.DepartmentProfileArticles", "StartOn");
            DropColumn("dbo.DepartmentProfileArticles", "CreatedByUserId");
            DropColumn("dbo.DepartmentProfileArticles", "Keywords");
        }
    }
}
