namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedUserIdAndTimeStampToCallAttachments : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.CallAttachments", "UserId", c => c.Guid());
            AddColumn("dbo.CallAttachments", "Timestamp", c => c.DateTime());
        }
        
        public override void Down()
        {
            DropColumn("dbo.CallAttachments", "Timestamp");
            DropColumn("dbo.CallAttachments", "UserId");
        }
    }
}
