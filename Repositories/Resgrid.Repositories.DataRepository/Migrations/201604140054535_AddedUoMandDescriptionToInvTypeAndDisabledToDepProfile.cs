namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedUoMandDescriptionToInvTypeAndDisabledToDepProfile : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentProfiles", "Disabled", c => c.Boolean(nullable: false));
            AddColumn("dbo.InventoryTypes", "Description", c => c.String());
            AddColumn("dbo.InventoryTypes", "UnitOfMesasure", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.InventoryTypes", "UnitOfMesasure");
            DropColumn("dbo.InventoryTypes", "Description");
            DropColumn("dbo.DepartmentProfiles", "Disabled");
        }
    }
}
