namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedEffectiveOnToPaymentsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Payments", "EffectiveOn", c => c.DateTime(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.Payments", "EffectiveOn");
        }
    }
}
