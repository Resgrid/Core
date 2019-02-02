namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedFilenameToDocumentTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Documents", "Filename", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Documents", "Filename");
        }
    }
}
