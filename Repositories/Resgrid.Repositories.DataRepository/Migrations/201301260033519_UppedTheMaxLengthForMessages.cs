namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class UppedTheMaxLengthForMessages : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.Messages", "Body", c => c.String(nullable: false, maxLength: 4000));
        }
        
        public override void Down()
        {
            AlterColumn("dbo.Messages", "Body", c => c.String(nullable: false, maxLength: 500));
        }
    }
}
