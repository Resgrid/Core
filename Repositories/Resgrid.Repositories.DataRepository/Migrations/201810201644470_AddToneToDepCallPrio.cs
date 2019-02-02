namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddToneToDepCallPrio : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentCallPriorities", "Tone", c => c.Int(nullable: false, defaultValueSql: "0"));
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentCallPriorities", "Tone");
        }
    }
}
