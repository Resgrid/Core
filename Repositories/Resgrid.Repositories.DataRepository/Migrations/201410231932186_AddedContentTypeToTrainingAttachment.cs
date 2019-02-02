namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedContentTypeToTrainingAttachment : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.TrainingAttachments", "FileType", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.TrainingAttachments", "FileType");
        }
    }
}
