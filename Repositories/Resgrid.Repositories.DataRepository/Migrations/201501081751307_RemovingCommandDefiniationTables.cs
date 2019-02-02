namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class RemovingCommandDefiniationTables : DbMigration
    {
        public override void Up()
        {
            DropForeignKey("dbo.CommandDefinitionRoleCerts", "DepartmentCertificationTypeId", "dbo.DepartmentCertificationTypes");
            DropForeignKey("dbo.CommandDefinitions", "CallTypeId", "dbo.CallTypes");
            DropForeignKey("dbo.CommandDefinitions", "DepartmentId", "dbo.Departments");
            DropForeignKey("dbo.CommandDefinitionRoles", "CommandDefinitionId", "dbo.CommandDefinitions");
            DropForeignKey("dbo.CommandDefinitionRoleUnitTypes", "CommandDefinitionRoleId", "dbo.CommandDefinitionRoles");
            DropForeignKey("dbo.CommandDefinitionRoleUnitTypes", "UnitTypeId", "dbo.UnitTypes");
            DropForeignKey("dbo.CommandDefinitionRoleUnitTypes", "CommandDefinitionRole_CommandDefinitionRoleId", "dbo.CommandDefinitionRoles");
            DropForeignKey("dbo.CommandDefinitionRolePersonnelRoles", "CommandDefinitionRoleId", "dbo.CommandDefinitionRoles");
            DropForeignKey("dbo.CommandDefinitionRolePersonnelRoles", "PersonnelRoleId", "dbo.PersonnelRoles");
            DropForeignKey("dbo.CommandDefinitionRoleUnitTypes", "CommandDefinitionRole_CommandDefinitionRoleId1", "dbo.CommandDefinitionRoles");
            DropForeignKey("dbo.CommandDefinitionRoleCerts", "CommandDefinitionRoleId", "dbo.CommandDefinitionRoles");
            DropIndex("dbo.CommandDefinitionRoleCerts", new[] { "CommandDefinitionRoleId" });
            DropIndex("dbo.CommandDefinitionRoleCerts", new[] { "DepartmentCertificationTypeId" });
            DropIndex("dbo.CommandDefinitionRoles", new[] { "CommandDefinitionId" });
            DropIndex("dbo.CommandDefinitions", new[] { "DepartmentId" });
            DropIndex("dbo.CommandDefinitions", new[] { "CallTypeId" });
            DropIndex("dbo.CommandDefinitionRoleUnitTypes", new[] { "CommandDefinitionRoleId" });
            DropIndex("dbo.CommandDefinitionRoleUnitTypes", new[] { "UnitTypeId" });
            DropIndex("dbo.CommandDefinitionRoleUnitTypes", new[] { "CommandDefinitionRole_CommandDefinitionRoleId" });
            DropIndex("dbo.CommandDefinitionRoleUnitTypes", new[] { "CommandDefinitionRole_CommandDefinitionRoleId1" });
            DropIndex("dbo.CommandDefinitionRolePersonnelRoles", new[] { "CommandDefinitionRoleId" });
            DropIndex("dbo.CommandDefinitionRolePersonnelRoles", new[] { "PersonnelRoleId" });
            DropTable("dbo.CommandDefinitionRoleCerts");
            DropTable("dbo.CommandDefinitionRoles");
            DropTable("dbo.CommandDefinitions");
            DropTable("dbo.CommandDefinitionRoleUnitTypes");
            DropTable("dbo.CommandDefinitionRolePersonnelRoles");
        }
        
        public override void Down()
        {
            CreateTable(
                "dbo.CommandDefinitionRolePersonnelRoles",
                c => new
                    {
                        CommandDefinitionRolePersonnelRoleId = c.Int(nullable: false, identity: true),
                        CommandDefinitionRoleId = c.Int(nullable: false),
                        PersonnelRoleId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.CommandDefinitionRolePersonnelRoleId);
            
            CreateTable(
                "dbo.CommandDefinitionRoleUnitTypes",
                c => new
                    {
                        CommandDefinitionRoleUnitTypeId = c.Int(nullable: false, identity: true),
                        CommandDefinitionRoleId = c.Int(nullable: false),
                        UnitTypeId = c.Int(nullable: false),
                        CommandDefinitionRole_CommandDefinitionRoleId = c.Int(),
                        CommandDefinitionRole_CommandDefinitionRoleId1 = c.Int(),
                    })
                .PrimaryKey(t => t.CommandDefinitionRoleUnitTypeId);
            
            CreateTable(
                "dbo.CommandDefinitions",
                c => new
                    {
                        CommandDefinitionId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        CallTypeId = c.Int(nullable: false),
                        Timer = c.Boolean(nullable: false),
                        TimerMinutes = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.CommandDefinitionId);
            
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
                    })
                .PrimaryKey(t => t.CommandDefinitionRoleId);
            
            CreateTable(
                "dbo.CommandDefinitionRoleCerts",
                c => new
                    {
                        CommandDefinitionRoleCertId = c.Int(nullable: false, identity: true),
                        CommandDefinitionRoleId = c.Int(nullable: false),
                        DepartmentCertificationTypeId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.CommandDefinitionRoleCertId);
            
            CreateIndex("dbo.CommandDefinitionRolePersonnelRoles", "PersonnelRoleId");
            CreateIndex("dbo.CommandDefinitionRolePersonnelRoles", "CommandDefinitionRoleId");
            CreateIndex("dbo.CommandDefinitionRoleUnitTypes", "CommandDefinitionRole_CommandDefinitionRoleId1");
            CreateIndex("dbo.CommandDefinitionRoleUnitTypes", "CommandDefinitionRole_CommandDefinitionRoleId");
            CreateIndex("dbo.CommandDefinitionRoleUnitTypes", "UnitTypeId");
            CreateIndex("dbo.CommandDefinitionRoleUnitTypes", "CommandDefinitionRoleId");
            CreateIndex("dbo.CommandDefinitions", "CallTypeId");
            CreateIndex("dbo.CommandDefinitions", "DepartmentId");
            CreateIndex("dbo.CommandDefinitionRoles", "CommandDefinitionId");
            CreateIndex("dbo.CommandDefinitionRoleCerts", "DepartmentCertificationTypeId");
            CreateIndex("dbo.CommandDefinitionRoleCerts", "CommandDefinitionRoleId");
            AddForeignKey("dbo.CommandDefinitionRoleCerts", "CommandDefinitionRoleId", "dbo.CommandDefinitionRoles", "CommandDefinitionRoleId", cascadeDelete: true);
            AddForeignKey("dbo.CommandDefinitionRoleUnitTypes", "CommandDefinitionRole_CommandDefinitionRoleId1", "dbo.CommandDefinitionRoles", "CommandDefinitionRoleId");
            AddForeignKey("dbo.CommandDefinitionRolePersonnelRoles", "PersonnelRoleId", "dbo.PersonnelRoles", "PersonnelRoleId", cascadeDelete: true);
            AddForeignKey("dbo.CommandDefinitionRolePersonnelRoles", "CommandDefinitionRoleId", "dbo.CommandDefinitionRoles", "CommandDefinitionRoleId", cascadeDelete: true);
            AddForeignKey("dbo.CommandDefinitionRoleUnitTypes", "CommandDefinitionRole_CommandDefinitionRoleId", "dbo.CommandDefinitionRoles", "CommandDefinitionRoleId");
            AddForeignKey("dbo.CommandDefinitionRoleUnitTypes", "UnitTypeId", "dbo.UnitTypes", "UnitTypeId", cascadeDelete: true);
            AddForeignKey("dbo.CommandDefinitionRoleUnitTypes", "CommandDefinitionRoleId", "dbo.CommandDefinitionRoles", "CommandDefinitionRoleId", cascadeDelete: true);
            AddForeignKey("dbo.CommandDefinitionRoles", "CommandDefinitionId", "dbo.CommandDefinitions", "CommandDefinitionId", cascadeDelete: true);
            AddForeignKey("dbo.CommandDefinitions", "DepartmentId", "dbo.Departments", "DepartmentId");
            AddForeignKey("dbo.CommandDefinitions", "CallTypeId", "dbo.CallTypes", "CallTypeId");
            AddForeignKey("dbo.CommandDefinitionRoleCerts", "DepartmentCertificationTypeId", "dbo.DepartmentCertificationTypes", "DepartmentCertificationTypeId", cascadeDelete: true);
        }
    }
}
