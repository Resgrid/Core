namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedPaymentsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Payments",
                c => new
                    {
                        PaymentId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        PlanId = c.Int(nullable: false),
                        Method = c.Int(nullable: false),
                        IsTrial = c.Boolean(nullable: false),
                        PurchaseOn = c.DateTime(nullable: false),
                        PurchasingUserId = c.Guid(nullable: false),
                        TransactionId = c.String(),
                        Successful = c.Boolean(nullable: false),
                        Data = c.String(),
                    })
                .PrimaryKey(t => t.PaymentId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId)
                .ForeignKey("dbo.Plans", t => t.PlanId, cascadeDelete: true)
                .ForeignKey("dbo.Users", t => t.PurchasingUserId, cascadeDelete: true)
                .Index(t => t.DepartmentId)
                .Index(t => t.PlanId)
                .Index(t => t.PurchasingUserId);
            
        }
        
        public override void Down()
        {
            DropIndex("dbo.Payments", new[] { "PurchasingUserId" });
            DropIndex("dbo.Payments", new[] { "PlanId" });
            DropIndex("dbo.Payments", new[] { "DepartmentId" });
            DropForeignKey("dbo.Payments", "PurchasingUserId", "dbo.Users");
            DropForeignKey("dbo.Payments", "PlanId", "dbo.Plans");
            DropForeignKey("dbo.Payments", "DepartmentId", "dbo.Departments");
            DropTable("dbo.Payments");
        }
    }
}
