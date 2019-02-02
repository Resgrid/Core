namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedAddedOnToNotesTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Notes", "AddedOn", c => c.DateTime(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.Notes", "AddedOn");
        }
    }
}
