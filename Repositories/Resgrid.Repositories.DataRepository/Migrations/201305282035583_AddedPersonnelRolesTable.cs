namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedPersonnelRolesTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.PersonnelRoles",
                c => new
                    {
                        PersonnelRoleId = c.Int(nullable: false, identity: true),
                        Name = c.String(nullable: false),
                        Description = c.String(),
                    })
                .PrimaryKey(t => t.PersonnelRoleId);
            
        }
        
        public override void Down()
        {
            DropTable("dbo.PersonnelRoles");
        }
    }
}
