namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedSystemGeneratedFlagToEmailMessage : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Messages", "SystemGenerated", c => c.Boolean(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.Messages", "SystemGenerated");
        }
    }
}
