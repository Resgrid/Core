using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(51)]
	public class M0051_AddingCustomMapsPg : Migration
	{
		public override void Up()
		{
			// CustomMaps — department-owned map definitions (indoor, satellite, schematic, event)
			Create.Table("CustomMaps".ToLower())
				.WithColumn("CustomMapId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
				.WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("Description".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("Type".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("BoundsTopLeftLat".ToLower()).AsDouble().NotNullable().WithDefaultValue(0)
				.WithColumn("BoundsTopLeftLng".ToLower()).AsDouble().NotNullable().WithDefaultValue(0)
				.WithColumn("BoundsBottomRightLat".ToLower()).AsDouble().NotNullable().WithDefaultValue(0)
				.WithColumn("BoundsBottomRightLng".ToLower()).AsDouble().NotNullable().WithDefaultValue(0)
				.WithColumn("DefaultZoom".ToLower()).AsInt32().NotNullable().WithDefaultValue(17)
				.WithColumn("MinZoom".ToLower()).AsInt32().NotNullable().WithDefaultValue(1)
				.WithColumn("MaxZoom".ToLower()).AsInt32().NotNullable().WithDefaultValue(22)
				.WithColumn("IsActive".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("EventStartsOn".ToLower()).AsDateTime().Nullable()
				.WithColumn("EventEndsOn".ToLower()).AsDateTime().Nullable()
				.WithColumn("AddedById".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("AddedOn".ToLower()).AsDateTime().NotNullable()
				.WithColumn("UpdatedById".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("UpdatedOn".ToLower()).AsDateTime().Nullable();

			Create.Index("IX_CustomMaps_DepartmentId".ToLower())
				.OnTable("CustomMaps".ToLower())
				.OnColumn("DepartmentId".ToLower()).Ascending();

			Create.ForeignKey("FK_CustomMaps_Departments".ToLower())
				.FromTable("CustomMaps".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());

			// CustomMapFloors — individual floor/level within a custom map
			Create.Table("CustomMapFloors".ToLower())
				.WithColumn("CustomMapFloorId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("CustomMapId".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("FloorNumber".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("ImageUrl".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("TileBaseUrl".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("Elevation".ToLower()).AsDouble().Nullable()
				.WithColumn("SortOrder".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("IsDefault".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false);

			Create.Index("IX_CustomMapFloors_CustomMapId".ToLower())
				.OnTable("CustomMapFloors".ToLower())
				.OnColumn("CustomMapId".ToLower()).Ascending();

			Create.ForeignKey("FK_CustomMapFloors_CustomMaps".ToLower())
				.FromTable("CustomMapFloors".ToLower()).ForeignColumn("CustomMapId".ToLower())
				.ToTable("CustomMaps".ToLower()).PrimaryColumn("CustomMapId".ToLower());

			// CustomMapZones — polygon regions on a floor with human-readable names
			Create.Table("CustomMapZones".ToLower())
				.WithColumn("CustomMapZoneId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("CustomMapFloorId".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("ZoneType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("PolygonGeoJson".ToLower()).AsCustom("text").Nullable()
				.WithColumn("Color".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("Metadata".ToLower()).AsCustom("text").Nullable()
				.WithColumn("Elevation".ToLower()).AsDouble().Nullable()
				.WithColumn("IsSearchable".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("IsActive".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true);

			Create.Index("IX_CustomMapZones_CustomMapFloorId".ToLower())
				.OnTable("CustomMapZones".ToLower())
				.OnColumn("CustomMapFloorId".ToLower()).Ascending();

			Create.ForeignKey("FK_CustomMapZones_CustomMapFloors".ToLower())
				.FromTable("CustomMapZones".ToLower()).ForeignColumn("CustomMapFloorId".ToLower())
				.ToTable("CustomMapFloors".ToLower()).PrimaryColumn("CustomMapFloorId".ToLower());

			// Extend Calls with optional zone reference for human-readable call locations
			Alter.Table("Calls".ToLower())
				.AddColumn("CustomMapZoneId".ToLower()).AsCustom("citext").Nullable();
		}

		public override void Down()
		{
			Delete.Column("CustomMapZoneId".ToLower()).FromTable("Calls".ToLower());

			Delete.ForeignKey("FK_CustomMapZones_CustomMapFloors".ToLower()).OnTable("CustomMapZones".ToLower());
			Delete.Index("IX_CustomMapZones_CustomMapFloorId".ToLower()).OnTable("CustomMapZones".ToLower());
			Delete.Table("CustomMapZones".ToLower());

			Delete.ForeignKey("FK_CustomMapFloors_CustomMaps".ToLower()).OnTable("CustomMapFloors".ToLower());
			Delete.Index("IX_CustomMapFloors_CustomMapId".ToLower()).OnTable("CustomMapFloors".ToLower());
			Delete.Table("CustomMapFloors".ToLower());

			Delete.ForeignKey("FK_CustomMaps_Departments".ToLower()).OnTable("CustomMaps".ToLower());
			Delete.Index("IX_CustomMaps_DepartmentId".ToLower()).OnTable("CustomMaps".ToLower());
			Delete.Table("CustomMaps".ToLower());
		}
	}
}

