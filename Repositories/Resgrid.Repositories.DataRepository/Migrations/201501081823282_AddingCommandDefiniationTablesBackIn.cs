namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddingCommandDefiniationTablesBackIn : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.CommandDefinitionRoleCerts",
                c => new
                    {
                        CommandDefinitionRoleCertId = c.Int(nullable: false, identity: true),
                        CommandDefinitionRoleId = c.Int(nullable: false),
                        DepartmentCertificationTypeId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.CommandDefinitionRoleCertId)
                .ForeignKey("dbo.DepartmentCertificationTypes", t => t.DepartmentCertificationTypeId, cascadeDelete: true)
                .ForeignKey("dbo.CommandDefinitionRoles", t => t.CommandDefinitionRoleId, cascadeDelete: true)
                .Index(t => t.CommandDefinitionRoleId)
                .Index(t => t.DepartmentCertificationTypeId);
            
            CreateTable(
                "dbo.CommandDefinitionRoles",
                c => new
                    {
                        CommandDefinitionRoleId = c.Int(nullable: false, identity: true),
                        CommandDefinitionId = c.Int(nullable: false),
                        Name = c.String(),
                        Description = c.String(),
                        MinUnitPersonnel = c.Int(nullable: false),
                        MaxUnitPersonnel = c.Int(nullable: false),
                        MaxUnits = c.Int(nullable: false),
                        MinTimeInRole = c.Int(nullable: false),
                        MaxTimeInRole = c.Int(nullable: false),
                        ForceRequirements = c.Boolean(nullable: false),
                    })
                .PrimaryKey(t => t.CommandDefinitionRoleId)
                .ForeignKey("dbo.CommandDefinitions", t => t.CommandDefinitionId, cascadeDelete: true)
                .Index(t => t.CommandDefinitionId);
            
            CreateTable(
                "dbo.CommandDefinitions",
                c => new
                    {
                        CommandDefinitionId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        CallTypeId = c.Int(),
                        Timer = c.Boolean(nullable: false),
                        TimerMinutes = c.Int(nullable: false),
                        Name = c.String(),
                        Description = c.String(),
                    })
                .PrimaryKey(t => t.CommandDefinitionId)
                .ForeignKey("dbo.CallTypes", t => t.CallTypeId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId)
                .Index(t => t.DepartmentId)
                .Index(t => t.CallTypeId);
            
            CreateTable(
                "dbo.CommandDefinitionRolePersonnelRoles",
                c => new
                    {
                        CommandDefinitionRolePersonnelRoleId = c.Int(nullable: false, identity: true),
                        CommandDefinitionRoleId = c.Int(nullable: false),
                        PersonnelRoleId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.CommandDefinitionRolePersonnelRoleId)
                .ForeignKey("dbo.CommandDefinitionRoles", t => t.CommandDefinitionRoleId, cascadeDelete: true)
                .ForeignKey("dbo.PersonnelRoles", t => t.PersonnelRoleId, cascadeDelete: true)
                .Index(t => t.CommandDefinitionRoleId)
                .Index(t => t.PersonnelRoleId);
            
            CreateTable(
                "dbo.CommandDefinitionRoleUnitTypes",
                c => new
                    {
                        CommandDefinitionRoleUnitTypeId = c.Int(nullable: false, identity: true),
                        CommandDefinitionRoleId = c.Int(nullable: false),
                        UnitTypeId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.CommandDefinitionRoleUnitTypeId)
                .ForeignKey("dbo.CommandDefinitionRoles", t => t.CommandDefinitionRoleId, cascadeDelete: true)
                .ForeignKey("dbo.UnitTypes", t => t.UnitTypeId, cascadeDelete: true)
                .Index(t => t.CommandDefinitionRoleId)
                .Index(t => t.UnitTypeId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.CommandDefinitionRoleCerts", "CommandDefinitionRoleId", "dbo.CommandDefinitionRoles");
            DropForeignKey("dbo.CommandDefinitionRoleUnitTypes", "UnitTypeId", "dbo.UnitTypes");
            DropForeignKey("dbo.CommandDefinitionRoleUnitTypes", "CommandDefinitionRoleId", "dbo.CommandDefinitionRoles");
            DropForeignKey("dbo.CommandDefinitionRolePersonnelRoles", "PersonnelRoleId", "dbo.PersonnelRoles");
            DropForeignKey("dbo.CommandDefinitionRolePersonnelRoles", "CommandDefinitionRoleId", "dbo.CommandDefinitionRoles");
            DropForeignKey("dbo.CommandDefinitionRoles", "CommandDefinitionId", "dbo.CommandDefinitions");
            DropForeignKey("dbo.CommandDefinitions", "DepartmentId", "dbo.Departments");
            DropForeignKey("dbo.CommandDefinitions", "CallTypeId", "dbo.CallTypes");
            DropForeignKey("dbo.CommandDefinitionRoleCerts", "DepartmentCertificationTypeId", "dbo.DepartmentCertificationTypes");
            DropIndex("dbo.CommandDefinitionRoleUnitTypes", new[] { "UnitTypeId" });
            DropIndex("dbo.CommandDefinitionRoleUnitTypes", new[] { "CommandDefinitionRoleId" });
            DropIndex("dbo.CommandDefinitionRolePersonnelRoles", new[] { "PersonnelRoleId" });
            DropIndex("dbo.CommandDefinitionRolePersonnelRoles", new[] { "CommandDefinitionRoleId" });
            DropIndex("dbo.CommandDefinitions", new[] { "CallTypeId" });
            DropIndex("dbo.CommandDefinitions", new[] { "DepartmentId" });
            DropIndex("dbo.CommandDefinitionRoles", new[] { "CommandDefinitionId" });
            DropIndex("dbo.CommandDefinitionRoleCerts", new[] { "DepartmentCertificationTypeId" });
            DropIndex("dbo.CommandDefinitionRoleCerts", new[] { "CommandDefinitionRoleId" });
            DropTable("dbo.CommandDefinitionRoleUnitTypes");
            DropTable("dbo.CommandDefinitionRolePersonnelRoles");
            DropTable("dbo.CommandDefinitions");
            DropTable("dbo.CommandDefinitionRoles");
            DropTable("dbo.CommandDefinitionRoleCerts");
        }
    }
}
