using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(67)]
	public class M0067_AddingPoiName : Migration
	{
		public override void Up()
		{
			Alter.Table("Pois")
				.AddColumn("Name").AsString(250).Nullable();
		}

		public override void Down()
		{
			Delete.Column("Name").FromTable("Pois");
		}
	}
}
