namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedFieldsToProfileMessages : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentProfileMessages", "IsUserSender", c => c.Boolean(nullable: false));
            AddColumn("dbo.DepartmentProfileMessages", "ConversationId", c => c.Guid(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentProfileMessages", "ConversationId");
            DropColumn("dbo.DepartmentProfileMessages", "IsUserSender");
        }
    }
}
