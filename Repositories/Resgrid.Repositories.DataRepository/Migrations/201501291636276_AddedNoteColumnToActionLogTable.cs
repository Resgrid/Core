namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedNoteColumnToActionLogTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ActionLogs", "Note", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.ActionLogs", "Note");
        }
    }
}
