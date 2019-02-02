namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class UpdatedCategoryColumnInNotesTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Notes", "Category", c => c.String());
            DropColumn("dbo.Notes", "Categories");
        }
        
        public override void Down()
        {
            AddColumn("dbo.Notes", "Categories", c => c.String());
            DropColumn("dbo.Notes", "Category");
        }
    }
}
