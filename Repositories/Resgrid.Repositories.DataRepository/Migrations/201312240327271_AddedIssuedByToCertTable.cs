namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedIssuedByToCertTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.PersonnelCertifications", "IssuedBy", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.PersonnelCertifications", "IssuedBy");
        }
    }
}
