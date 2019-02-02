namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
	public partial class RemovedRequirementsForHostPickupOnDList : DbMigration
	{
		public override void Up()
		{
			AlterColumn("dbo.DistributionLists", "Hostname", c => c.String(maxLength: 500));
			AlterColumn("dbo.DistributionLists", "Port", c => c.Int());
			AlterColumn("dbo.DistributionLists", "UseSsl", c => c.Boolean());
		}
      
		public override void Down()
		{
			AlterColumn("dbo.DistributionLists", "UseSsl", c => c.Boolean(nullable: false));
			AlterColumn("dbo.DistributionLists", "Port", c => c.Int(nullable: false));
			AlterColumn("dbo.DistributionLists", "Hostname", c => c.String(nullable: false, maxLength: 500));
		}
	}
}
