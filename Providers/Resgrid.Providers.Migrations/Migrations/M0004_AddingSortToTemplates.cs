using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(4)]
	public class M0004_AddingSortToTemplates : Migration
	{
		public override void Up()
		{
			Alter.Table("CallQuickTemplates").AddColumn("Sort").AsInt32().Nullable();
		}

		public override void Down()
		{
			
		}
	}
}
