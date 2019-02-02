namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedValuesToCustomStatesTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.CustomStates", "NoteType", c => c.Int(nullable: false));
            AddColumn("dbo.CustomStates", "DetailType", c => c.Int(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.CustomStates", "DetailType");
            DropColumn("dbo.CustomStates", "NoteType");
        }
    }
}
