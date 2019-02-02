namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDeviceIdToPushUriTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.PushUris", "DeviceId", c => c.String(maxLength: 500));
        }
        
        public override void Down()
        {
            DropColumn("dbo.PushUris", "DeviceId");
        }
    }
}
