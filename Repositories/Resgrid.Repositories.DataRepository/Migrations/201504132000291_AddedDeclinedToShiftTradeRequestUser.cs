namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDeclinedToShiftTradeRequestUser : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ShiftSignupTradeUsers", "Declined", c => c.Boolean(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.ShiftSignupTradeUsers", "Declined");
        }
    }
}
