namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedAffiliatesTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Affiliates",
                c => new
                    {
                        AffiliateId = c.Int(nullable: false, identity: true),
                        AffiliateMailingAddressId = c.Int(),
                        AffiliateCode = c.String(),
                        FirstName = c.String(),
                        LastName = c.String(),
                        EmailAddress = c.String(),
                        CompanyOrDepartment = c.String(),
                        Country = c.String(),
                        Region = c.String(),
                        Experiance = c.String(),
                        Qualifications = c.String(),
                        Approved = c.Boolean(nullable: false),
                        RejectReason = c.String(),
                        PayPalAddress = c.String(),
                        TaxIdentifier = c.String(),
                        Active = c.Boolean(nullable: false),
                        DeactivateReason = c.String(),
                    })
                .PrimaryKey(t => t.AffiliateId);
            
        }
        
        public override void Down()
        {
            DropTable("dbo.Affiliates");
        }
    }
}
