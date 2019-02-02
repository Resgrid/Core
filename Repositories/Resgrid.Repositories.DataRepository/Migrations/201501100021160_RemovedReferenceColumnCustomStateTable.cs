namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class RemovedReferenceColumnCustomStateTable : DbMigration
    {
        public override void Up()
        {
            DropColumn("dbo.CustomStates", "Reference");
        }
        
        public override void Down()
        {
            AddColumn("dbo.CustomStates", "Reference", c => c.String());
        }
    }
}
