namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedGeofenceToDepartmentGroup : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentGroups", "Geofence", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentGroups", "Geofence");
        }
    }
}
