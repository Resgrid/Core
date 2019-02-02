namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedNoteToShiftTradeRequest : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ShiftSignupTrades", "Note", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.ShiftSignupTrades", "Note");
        }
    }
}
