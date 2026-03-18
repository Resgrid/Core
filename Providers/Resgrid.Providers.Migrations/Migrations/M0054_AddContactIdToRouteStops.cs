using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(54)]
	public class M0054_AddContactIdToRouteStops : Migration
	{
		public override void Up()
		{
			Alter.Table("RouteStops")
				.AddColumn("ContactId").AsString(128).Nullable();
		}

		public override void Down()
		{
			Delete.Column("ContactId").FromTable("RouteStops");
		}
	}
}
