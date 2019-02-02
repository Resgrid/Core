namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedGeofenceColorToDepartmentGroup : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentGroups", "GeofenceColor", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentGroups", "GeofenceColor");
        }
    }
}
