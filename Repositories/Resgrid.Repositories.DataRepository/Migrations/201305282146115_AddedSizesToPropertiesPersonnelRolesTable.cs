namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedSizesToPropertiesPersonnelRolesTable : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.PersonnelRoles", "Name", c => c.String(nullable: false, maxLength: 250));
            AlterColumn("dbo.PersonnelRoles", "Description", c => c.String(maxLength: 3000));
        }
        
        public override void Down()
        {
            AlterColumn("dbo.PersonnelRoles", "Description", c => c.String());
            AlterColumn("dbo.PersonnelRoles", "Name", c => c.String(nullable: false));
        }
    }
}
