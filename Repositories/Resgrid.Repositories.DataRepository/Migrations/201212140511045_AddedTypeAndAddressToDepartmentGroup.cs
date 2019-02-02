namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedTypeAndAddressToDepartmentGroup : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentGroups", "Type", c => c.Int());
            AddColumn("dbo.DepartmentGroups", "AddressId", c => c.Int());
            AddForeignKey("dbo.DepartmentGroups", "AddressId", "dbo.Addresses", "AddressId");
            CreateIndex("dbo.DepartmentGroups", "AddressId");
        }
        
        public override void Down()
        {
            DropIndex("dbo.DepartmentGroups", new[] { "AddressId" });
            DropForeignKey("dbo.DepartmentGroups", "AddressId", "dbo.Addresses");
            DropColumn("dbo.DepartmentGroups", "AddressId");
            DropColumn("dbo.DepartmentGroups", "Type");
        }
    }
}
