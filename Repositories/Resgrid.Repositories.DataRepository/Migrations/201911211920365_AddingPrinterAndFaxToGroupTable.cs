namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddingPrinterAndFaxToGroupTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentGroups", "DispatchToPrinter", c => c.Boolean(nullable: false));
            AddColumn("dbo.DepartmentGroups", "PrinterData", c => c.String());
            AddColumn("dbo.DepartmentGroups", "DispatchToFax", c => c.Boolean(nullable: false));
            AddColumn("dbo.DepartmentGroups", "FaxNumber", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentGroups", "FaxNumber");
            DropColumn("dbo.DepartmentGroups", "DispatchToFax");
            DropColumn("dbo.DepartmentGroups", "PrinterData");
            DropColumn("dbo.DepartmentGroups", "DispatchToPrinter");
        }
    }
}
