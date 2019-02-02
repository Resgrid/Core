namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedIdDeletedColumnToCustomStatesTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.CustomStates", "IsDeleted", c => c.Boolean(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.CustomStates", "IsDeleted");
        }
    }
}
