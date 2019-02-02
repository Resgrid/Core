namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedAddressToDepartment : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Departments", "AddressId", c => c.Int());
            AddForeignKey("dbo.Departments", "AddressId", "dbo.Addresses", "AddressId");
            CreateIndex("dbo.Departments", "AddressId");
        }
        
        public override void Down()
        {
            DropIndex("dbo.Departments", new[] { "AddressId" });
            DropForeignKey("dbo.Departments", "AddressId", "dbo.Addresses");
            DropColumn("dbo.Departments", "AddressId");
        }
    }
}
