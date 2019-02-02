namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedNameAndSizeToCallAttachments : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.CallAttachments", "Name", c => c.String(maxLength: 250));
            AddColumn("dbo.CallAttachments", "Size", c => c.Int());
        }
        
        public override void Down()
        {
            DropColumn("dbo.CallAttachments", "Size");
            DropColumn("dbo.CallAttachments", "Name");
        }
    }
}
