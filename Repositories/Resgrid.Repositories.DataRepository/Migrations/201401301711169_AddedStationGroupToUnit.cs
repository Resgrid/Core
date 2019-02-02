namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedStationGroupToUnit : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Units", "StationGroupId", c => c.Int());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Units", "StationGroupId");
        }
    }
}
