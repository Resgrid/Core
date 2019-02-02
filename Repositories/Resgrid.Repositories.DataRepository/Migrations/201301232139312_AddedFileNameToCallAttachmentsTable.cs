namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedFileNameToCallAttachmentsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.CallAttachments", "FileName", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.CallAttachments", "FileName");
        }
    }
}
