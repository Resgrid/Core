namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddingButtonTextColorToCustomDetailsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.CustomStateDetails", "TextColor", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.CustomStateDetails", "TextColor");
        }
    }
}
