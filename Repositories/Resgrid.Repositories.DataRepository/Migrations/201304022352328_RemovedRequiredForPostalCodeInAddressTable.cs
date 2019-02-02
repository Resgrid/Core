namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class RemovedRequiredForPostalCodeInAddressTable : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.Addresses", "PostalCode", c => c.String(maxLength: 50));
        }
        
        public override void Down()
        {
            AlterColumn("dbo.Addresses", "PostalCode", c => c.String(nullable: false, maxLength: 50));
        }
    }
}
