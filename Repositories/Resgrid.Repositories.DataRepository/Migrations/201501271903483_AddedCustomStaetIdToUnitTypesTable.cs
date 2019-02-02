namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedCustomStaetIdToUnitTypesTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.UnitTypes", "CustomStatesId", c => c.Int());
        }
        
        public override void Down()
        {
            DropColumn("dbo.UnitTypes", "CustomStatesId");
        }
    }
}
