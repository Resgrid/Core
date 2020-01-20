namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddStartsOnForNote : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Notes", "StartsOn", c => c.DateTime());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Notes", "StartsOn");
        }
    }
}
