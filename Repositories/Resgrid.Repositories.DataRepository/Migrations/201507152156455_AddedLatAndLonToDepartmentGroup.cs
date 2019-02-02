namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedLatAndLonToDepartmentGroup : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentGroups", "Latitude", c => c.String());
            AddColumn("dbo.DepartmentGroups", "Longitude", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentGroups", "Longitude");
            DropColumn("dbo.DepartmentGroups", "Latitude");
        }
    }
}
