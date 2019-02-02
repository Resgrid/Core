namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedCreatedOnToPushUri : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.PushUris", "CreatedOn", c => c.DateTime(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.PushUris", "CreatedOn");
        }
    }
}
