namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedTypeToLogAttachmentTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.LogAttachments", "Type", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.LogAttachments", "Type");
        }
    }
}
