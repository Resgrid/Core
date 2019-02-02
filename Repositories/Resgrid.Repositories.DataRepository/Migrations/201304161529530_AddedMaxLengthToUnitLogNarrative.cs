namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedMaxLengthToUnitLogNarrative : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.UnitLogs", "Narrative", c => c.String(nullable: false, maxLength: 4000));
        }
        
        public override void Down()
        {
            AlterColumn("dbo.UnitLogs", "Narrative", c => c.String(nullable: false));
        }
    }
}
