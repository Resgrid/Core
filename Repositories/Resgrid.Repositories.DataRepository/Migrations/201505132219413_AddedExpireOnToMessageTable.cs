namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedExpireOnToMessageTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Messages", "ExpireOn", c => c.DateTime());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Messages", "ExpireOn");
        }
    }
}
