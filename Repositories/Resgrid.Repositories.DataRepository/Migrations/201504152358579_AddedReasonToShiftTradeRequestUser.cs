namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedReasonToShiftTradeRequestUser : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ShiftSignupTradeUsers", "Reason", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.ShiftSignupTradeUsers", "Reason");
        }
    }
}
