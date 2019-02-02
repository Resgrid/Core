namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedNoteFieldToUserStateTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.UserStates", "Note", c => c.String(maxLength: 3000));
        }
        
        public override void Down()
        {
            DropColumn("dbo.UserStates", "Note");
        }
    }
}
