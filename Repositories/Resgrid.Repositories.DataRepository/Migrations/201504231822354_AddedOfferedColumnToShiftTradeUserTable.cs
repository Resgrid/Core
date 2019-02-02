namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedOfferedColumnToShiftTradeUserTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ShiftSignupTradeUsers", "Offered", c => c.Boolean(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.ShiftSignupTradeUsers", "Offered");
        }
    }
}
