using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(66)]
	public class M0066_AddingPoiDestinations : Migration
	{
		public override void Up()
		{
			Alter.Table("Pois")
				.AddColumn("Address").AsString(500).Nullable();

			Alter.Table("Calls")
				.AddColumn("DestinationPoiId").AsInt32().Nullable();

			Alter.Table("UnitStates")
				.AddColumn("DestinationType").AsInt32().Nullable();
		}

		public override void Down()
		{
			Delete.Column("Address").FromTable("Pois");
			Delete.Column("DestinationPoiId").FromTable("Calls");
			Delete.Column("DestinationType").FromTable("UnitStates");
		}
	}
}
