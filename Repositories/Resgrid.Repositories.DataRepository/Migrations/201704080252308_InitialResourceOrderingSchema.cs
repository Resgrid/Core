namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class InitialResourceOrderingSchema : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.ResourceOrderFills",
                c => new
                    {
                        ResourceOrderFillId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        ResourceOrderItemId = c.Int(nullable: false),
                        FillingUserId = c.String(maxLength: 128),
                        Note = c.String(),
                        ContactName = c.String(),
                        ContactNumber = c.String(),
                        FilledOn = c.DateTime(nullable: false),
                        Accepted = c.Boolean(nullable: false),
                        AcceptedOn = c.DateTime(),
                        LeadUserId = c.String(maxLength: 128),
                        AcceptedUserId = c.String(maxLength: 128),
                    })
                .PrimaryKey(t => t.ResourceOrderFillId)
                .ForeignKey("dbo.AspNetUsers", t => t.AcceptedUserId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .ForeignKey("dbo.AspNetUsers", t => t.FillingUserId)
                .ForeignKey("dbo.AspNetUsers", t => t.LeadUserId)
                .ForeignKey("dbo.ResourceOrderItems", t => t.ResourceOrderItemId, cascadeDelete: true)
                .Index(t => t.DepartmentId)
                .Index(t => t.ResourceOrderItemId)
                .Index(t => t.FillingUserId)
                .Index(t => t.LeadUserId)
                .Index(t => t.AcceptedUserId);
            
            CreateTable(
                "dbo.ResourceOrderItems",
                c => new
                    {
                        ResourceOrderItemId = c.Int(nullable: false, identity: true),
                        ResourceOrderId = c.Int(nullable: false),
                        Resource = c.String(),
                        Min = c.Int(nullable: false),
                        Max = c.Int(nullable: false),
                        FinancialCode = c.String(),
                        SpecialNeeds = c.String(),
                        Requirements = c.String(),
                    })
                .PrimaryKey(t => t.ResourceOrderItemId)
                .ForeignKey("dbo.ResourceOrders", t => t.ResourceOrderId, cascadeDelete: true)
                .Index(t => t.ResourceOrderId);
            
            CreateTable(
                "dbo.ResourceOrders",
                c => new
                    {
                        ResourceOrderId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Type = c.Int(nullable: false),
                        AllowPartialFills = c.Boolean(nullable: false),
                        Title = c.String(),
                        IncidentNumber = c.String(),
                        IncidentName = c.String(),
                        IncidentAddress = c.String(),
                        IncidentLatitude = c.Decimal(precision: 18, scale: 2),
                        IncidentLongitude = c.Decimal(precision: 18, scale: 2),
                        Summary = c.String(),
                        OpenDate = c.DateTime(nullable: false),
                        NeededBy = c.DateTime(nullable: false),
                        MeetupDate = c.DateTime(),
                        CloseDate = c.DateTime(),
                        ContactName = c.String(),
                        ContactNumber = c.String(),
                        SpecialInstructions = c.String(),
                        MeetupLocation = c.String(),
                        FinancialCode = c.String(),
                        AutomaticFillAcceptance = c.Boolean(nullable: false),
                    })
                .PrimaryKey(t => t.ResourceOrderId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId)
                .Index(t => t.DepartmentId);
            
            CreateTable(
                "dbo.ResourceOrderFillUnits",
                c => new
                    {
                        ResourceOrderFillUnitId = c.Int(nullable: false, identity: true),
                        ResourceOrderFillId = c.Int(nullable: false),
                        UnitId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.ResourceOrderFillUnitId)
                .ForeignKey("dbo.ResourceOrderFills", t => t.ResourceOrderFillId, cascadeDelete: true)
                .ForeignKey("dbo.Units", t => t.UnitId, cascadeDelete: true)
                .Index(t => t.ResourceOrderFillId)
                .Index(t => t.UnitId);
            
            CreateTable(
                "dbo.ResourceOrderSettings",
                c => new
                    {
                        ResourceOrderSettingId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Visibility = c.Int(nullable: false),
                        DoNotReceiveOrders = c.Boolean(nullable: false),
                        RoleAllowedToFulfilOrdersRoleId = c.Int(),
                        LimitStaffingLevelToReceiveNotifications = c.Boolean(nullable: false),
                        AllowedStaffingLevelToReceiveNotifications = c.Int(nullable: false),
                        DefaultResourceOrderManagerUserId = c.String(),
                        Latitude = c.Decimal(precision: 18, scale: 2),
                        Longitude = c.Decimal(precision: 18, scale: 2),
                        Range = c.Int(nullable: false),
                        BoundryGeofence = c.String(),
                        TargetDepartmentType = c.String(),
                        AutomaticFillAcceptance = c.Boolean(nullable: false),
                        ImportEmailCode = c.String(),
                        NotifyUsers = c.Boolean(nullable: false),
                        UserIdsToNotifyOnOrders = c.String(),
                    })
                .PrimaryKey(t => t.ResourceOrderSettingId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .ForeignKey("dbo.PersonnelRoles", t => t.RoleAllowedToFulfilOrdersRoleId)
                .Index(t => t.DepartmentId)
                .Index(t => t.RoleAllowedToFulfilOrdersRoleId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.ResourceOrderSettings", "RoleAllowedToFulfilOrdersRoleId", "dbo.PersonnelRoles");
            DropForeignKey("dbo.ResourceOrderSettings", "DepartmentId", "dbo.Departments");
            DropForeignKey("dbo.ResourceOrderFillUnits", "UnitId", "dbo.Units");
            DropForeignKey("dbo.ResourceOrderFillUnits", "ResourceOrderFillId", "dbo.ResourceOrderFills");
            DropForeignKey("dbo.ResourceOrderFills", "ResourceOrderItemId", "dbo.ResourceOrderItems");
            DropForeignKey("dbo.ResourceOrderItems", "ResourceOrderId", "dbo.ResourceOrders");
            DropForeignKey("dbo.ResourceOrders", "DepartmentId", "dbo.Departments");
            DropForeignKey("dbo.ResourceOrderFills", "LeadUserId", "dbo.AspNetUsers");
            DropForeignKey("dbo.ResourceOrderFills", "FillingUserId", "dbo.AspNetUsers");
            DropForeignKey("dbo.ResourceOrderFills", "DepartmentId", "dbo.Departments");
            DropForeignKey("dbo.ResourceOrderFills", "AcceptedUserId", "dbo.AspNetUsers");
            DropIndex("dbo.ResourceOrderSettings", new[] { "RoleAllowedToFulfilOrdersRoleId" });
            DropIndex("dbo.ResourceOrderSettings", new[] { "DepartmentId" });
            DropIndex("dbo.ResourceOrderFillUnits", new[] { "UnitId" });
            DropIndex("dbo.ResourceOrderFillUnits", new[] { "ResourceOrderFillId" });
            DropIndex("dbo.ResourceOrders", new[] { "DepartmentId" });
            DropIndex("dbo.ResourceOrderItems", new[] { "ResourceOrderId" });
            DropIndex("dbo.ResourceOrderFills", new[] { "AcceptedUserId" });
            DropIndex("dbo.ResourceOrderFills", new[] { "LeadUserId" });
            DropIndex("dbo.ResourceOrderFills", new[] { "FillingUserId" });
            DropIndex("dbo.ResourceOrderFills", new[] { "ResourceOrderItemId" });
            DropIndex("dbo.ResourceOrderFills", new[] { "DepartmentId" });
            DropTable("dbo.ResourceOrderSettings");
            DropTable("dbo.ResourceOrderFillUnits");
            DropTable("dbo.ResourceOrders");
            DropTable("dbo.ResourceOrderItems");
            DropTable("dbo.ResourceOrderFills");
        }
    }
}
