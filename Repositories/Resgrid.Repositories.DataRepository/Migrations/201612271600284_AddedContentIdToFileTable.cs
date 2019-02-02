namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedContentIdToFileTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Files", "ContentId", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Files", "ContentId");
        }
    }
}
