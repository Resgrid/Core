namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedValuesToLogTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Logs", "OtherPersonnel", c => c.String());
            AddColumn("dbo.Logs", "Location", c => c.String());
            AddColumn("dbo.Logs", "OtherAgencies", c => c.String());
            AddColumn("dbo.Logs", "OtherUnits", c => c.String());
            AddColumn("dbo.Logs", "BodyLocation", c => c.String());
            AddColumn("dbo.Logs", "PronouncedDeceasedBy", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Logs", "PronouncedDeceasedBy");
            DropColumn("dbo.Logs", "BodyLocation");
            DropColumn("dbo.Logs", "OtherUnits");
            DropColumn("dbo.Logs", "OtherAgencies");
            DropColumn("dbo.Logs", "Location");
            DropColumn("dbo.Logs", "OtherPersonnel");
        }
    }
}
