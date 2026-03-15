using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(51)]
	public class M0051_AddingCustomMaps : Migration
	{
		public override void Up()
		{
			// CustomMaps — department-owned map definitions (indoor, satellite, schematic, event)
			Create.Table("CustomMaps")
				.WithColumn("CustomMapId").AsString(36).NotNullable().PrimaryKey()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("Name").AsString(500).NotNullable()
				.WithColumn("Description").AsString(2000).Nullable()
				.WithColumn("Type").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("BoundsTopLeftLat").AsDouble().NotNullable().WithDefaultValue(0)
				.WithColumn("BoundsTopLeftLng").AsDouble().NotNullable().WithDefaultValue(0)
				.WithColumn("BoundsBottomRightLat").AsDouble().NotNullable().WithDefaultValue(0)
				.WithColumn("BoundsBottomRightLng").AsDouble().NotNullable().WithDefaultValue(0)
				.WithColumn("DefaultZoom").AsInt32().NotNullable().WithDefaultValue(17)
				.WithColumn("MinZoom").AsInt32().NotNullable().WithDefaultValue(1)
				.WithColumn("MaxZoom").AsInt32().NotNullable().WithDefaultValue(22)
				.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("EventStartsOn").AsDateTime().Nullable()
				.WithColumn("EventEndsOn").AsDateTime().Nullable()
				.WithColumn("AddedById").AsString(128).NotNullable()
				.WithColumn("AddedOn").AsDateTime2().NotNullable()
				.WithColumn("UpdatedById").AsString(128).Nullable()
				.WithColumn("UpdatedOn").AsDateTime2().Nullable();

			Create.Index("IX_CustomMaps_DepartmentId")
				.OnTable("CustomMaps")
				.OnColumn("DepartmentId").Ascending();

			Create.ForeignKey("FK_CustomMaps_Departments")
				.FromTable("CustomMaps").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			// CustomMapFloors — individual floor/level within a custom map
			Create.Table("CustomMapFloors")
				.WithColumn("CustomMapFloorId").AsString(36).NotNullable().PrimaryKey()
				.WithColumn("CustomMapId").AsString(36).NotNullable()
				.WithColumn("FloorNumber").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("Name").AsString(200).NotNullable()
				.WithColumn("ImageUrl").AsString(2000).Nullable()
				.WithColumn("TileBaseUrl").AsString(2000).Nullable()
				.WithColumn("Elevation").AsDouble().Nullable()
				.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("IsDefault").AsBoolean().NotNullable().WithDefaultValue(false);

			Create.Index("IX_CustomMapFloors_CustomMapId")
				.OnTable("CustomMapFloors")
				.OnColumn("CustomMapId").Ascending();

			Create.ForeignKey("FK_CustomMapFloors_CustomMaps")
				.FromTable("CustomMapFloors").ForeignColumn("CustomMapId")
				.ToTable("CustomMaps").PrimaryColumn("CustomMapId");

			// CustomMapZones — polygon regions on a floor with human-readable names
			Create.Table("CustomMapZones")
				.WithColumn("CustomMapZoneId").AsString(36).NotNullable().PrimaryKey()
				.WithColumn("CustomMapFloorId").AsString(36).NotNullable()
				.WithColumn("Name").AsString(500).NotNullable()
				.WithColumn("ZoneType").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("PolygonGeoJson").AsCustom("nvarchar(max)").Nullable()
				.WithColumn("Color").AsString(20).Nullable()
				.WithColumn("Metadata").AsCustom("nvarchar(max)").Nullable()
				.WithColumn("Elevation").AsDouble().Nullable()
				.WithColumn("IsSearchable").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true);

			Create.Index("IX_CustomMapZones_CustomMapFloorId")
				.OnTable("CustomMapZones")
				.OnColumn("CustomMapFloorId").Ascending();

			Create.ForeignKey("FK_CustomMapZones_CustomMapFloors")
				.FromTable("CustomMapZones").ForeignColumn("CustomMapFloorId")
				.ToTable("CustomMapFloors").PrimaryColumn("CustomMapFloorId");

			// Extend Calls with optional zone reference for human-readable call locations
			Alter.Table("Calls")
				.AddColumn("CustomMapZoneId").AsString(36).Nullable();
		}

		public override void Down()
		{
			Delete.Column("CustomMapZoneId").FromTable("Calls");

			Delete.ForeignKey("FK_CustomMapZones_CustomMapFloors").OnTable("CustomMapZones");
			Delete.Index("IX_CustomMapZones_CustomMapFloorId").OnTable("CustomMapZones");
			Delete.Table("CustomMapZones");

			Delete.ForeignKey("FK_CustomMapFloors_CustomMaps").OnTable("CustomMapFloors");
			Delete.Index("IX_CustomMapFloors_CustomMapId").OnTable("CustomMapFloors");
			Delete.Table("CustomMapFloors");

			Delete.ForeignKey("FK_CustomMaps_Departments").OnTable("CustomMaps");
			Delete.Index("IX_CustomMaps_DepartmentId").OnTable("CustomMaps");
			Delete.Table("CustomMaps");
		}
	}
}

