namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedUserIdAndUpdatedDateToPermissionsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Permissions", "UpdatedBy", c => c.Guid(nullable: false));
            AddColumn("dbo.Permissions", "UpdatedOn", c => c.DateTime(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.Permissions", "UpdatedOn");
            DropColumn("dbo.Permissions", "UpdatedBy");
        }
    }
}
