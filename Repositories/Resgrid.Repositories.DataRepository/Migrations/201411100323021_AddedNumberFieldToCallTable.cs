namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedNumberFieldToCallTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Calls", "Number", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Calls", "Number");
        }
    }
}
