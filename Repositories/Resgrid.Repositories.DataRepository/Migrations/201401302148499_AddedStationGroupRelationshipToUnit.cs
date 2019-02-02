namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedStationGroupRelationshipToUnit : DbMigration
    {
        public override void Up()
        {
            CreateIndex("dbo.Units", "StationGroupId");
            AddForeignKey("dbo.Units", "StationGroupId", "dbo.DepartmentGroups", "DepartmentGroupId");
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.Units", "StationGroupId", "dbo.DepartmentGroups");
            DropIndex("dbo.Units", new[] { "StationGroupId" });
        }
    }
}
