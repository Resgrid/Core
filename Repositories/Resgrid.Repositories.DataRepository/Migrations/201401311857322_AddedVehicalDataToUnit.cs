namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedVehicalDataToUnit : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Units", "VIN", c => c.String());
            AddColumn("dbo.Units", "PlateNumber", c => c.String());
            AddColumn("dbo.Units", "FourWheel", c => c.Boolean());
            AddColumn("dbo.Units", "SpecialPermit", c => c.Boolean());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Units", "SpecialPermit");
            DropColumn("dbo.Units", "FourWheel");
            DropColumn("dbo.Units", "PlateNumber");
            DropColumn("dbo.Units", "VIN");
        }
    }
}
