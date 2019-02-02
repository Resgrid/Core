namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedIdentificationNumberToProfile : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.UserProfiles", "IdentificationNumber", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.UserProfiles", "IdentificationNumber");
        }
    }
}
