namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedTypeToDocumentTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Documents", "Type", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Documents", "Type");
        }
    }
}
