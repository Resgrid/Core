namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class CleanedUpForignKeysandIndexes : DbMigration
    {
        public override void Up()
        {
            DropForeignKey("DepartmentMembers", "UserId", "Users");
            DropForeignKey("DepartmentGroupMembers", "UserId", "Users");
            DropForeignKey("ActionLogs", "DepartmentId", "Departments");
            DropIndex("DepartmentMembers", new[] { "UserId" });
            DropIndex("DepartmentGroupMembers", new[] { "UserId" });
            DropIndex("ActionLogs", new[] { "DepartmentId" });
            AddForeignKey("DepartmentMembers", "UserId", "Users", "UserId");
            AddForeignKey("DepartmentGroupMembers", "UserId", "Users", "UserId");
            AddForeignKey("ActionLogs", "DepartmentId", "Departments", "DepartmentId");
            CreateIndex("DepartmentMembers", "UserId");
            CreateIndex("DepartmentGroupMembers", "UserId");
            CreateIndex("ActionLogs", "DepartmentId");
        }
        
        public override void Down()
        {
            DropIndex("ActionLogs", new[] { "DepartmentId" });
            DropIndex("DepartmentGroupMembers", new[] { "UserId" });
            DropIndex("DepartmentMembers", new[] { "UserId" });
            DropForeignKey("ActionLogs", "DepartmentId", "Departments");
            DropForeignKey("DepartmentGroupMembers", "UserId", "Users");
            DropForeignKey("DepartmentMembers", "UserId", "Users");
            CreateIndex("ActionLogs", "DepartmentId");
            CreateIndex("DepartmentGroupMembers", "UserId");
            CreateIndex("DepartmentMembers", "UserId");
            AddForeignKey("ActionLogs", "DepartmentId", "Departments", "DepartmentId", cascadeDelete: true);
            AddForeignKey("DepartmentGroupMembers", "UserId", "Users", "UserId", cascadeDelete: true);
            AddForeignKey("DepartmentMembers", "UserId", "Users", "UserId", cascadeDelete: true);
        }
    }
}
