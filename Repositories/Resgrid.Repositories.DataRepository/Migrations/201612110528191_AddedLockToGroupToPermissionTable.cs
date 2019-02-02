namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedLockToGroupToPermissionTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Permissions", "LockToGroup", c => c.Boolean(nullable: false, defaultValue: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.Permissions", "LockToGroup");
        }
    }
}
