namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedOrdersToDepartmentLinksTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentLinks", "DepartmentShareOrders", c => c.Boolean(nullable: false));
            AddColumn("dbo.DepartmentLinks", "LinkedDepartmentShareOrders", c => c.Boolean(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentLinks", "LinkedDepartmentShareOrders");
            DropColumn("dbo.DepartmentLinks", "DepartmentShareOrders");
        }
    }
}
