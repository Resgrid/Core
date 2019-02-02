namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedFileUploadToCertificationsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.PersonnelCertifications", "Filetype", c => c.String());
            AddColumn("dbo.PersonnelCertifications", "Filename", c => c.String());
            AddColumn("dbo.PersonnelCertifications", "Data", c => c.Binary());
        }
        
        public override void Down()
        {
            DropColumn("dbo.PersonnelCertifications", "Data");
            DropColumn("dbo.PersonnelCertifications", "Filename");
            DropColumn("dbo.PersonnelCertifications", "Filetype");
        }
    }
}
