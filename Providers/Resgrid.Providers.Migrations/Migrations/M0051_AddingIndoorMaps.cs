using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(51)]
	public class M0051_AddingIndoorMaps : Migration
	{
		public override void Up()
		{
			Create.Table("IndoorMaps")
			   .WithColumn("IndoorMapId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("Name").AsString(250).NotNullable()
			   .WithColumn("Description").AsString(1000).Nullable()
			   .WithColumn("CenterLatitude").AsDecimal(10, 7).NotNullable()
			   .WithColumn("CenterLongitude").AsDecimal(10, 7).NotNullable()
			   .WithColumn("BoundsNELat").AsDecimal(10, 7).NotNullable()
			   .WithColumn("BoundsNELon").AsDecimal(10, 7).NotNullable()
			   .WithColumn("BoundsSWLat").AsDecimal(10, 7).NotNullable()
			   .WithColumn("BoundsSWLon").AsDecimal(10, 7).NotNullable()
			   .WithColumn("DefaultFloorId").AsString(128).Nullable()
			   .WithColumn("IsDeleted").AsBoolean().NotNullable()
			   .WithColumn("AddedById").AsString(128).NotNullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable()
			   .WithColumn("UpdatedById").AsString(128).Nullable()
			   .WithColumn("UpdatedOn").AsDateTime2().Nullable();

			Create.ForeignKey("FK_IndoorMaps_Departments")
				.FromTable("IndoorMaps").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.Table("IndoorMapFloors")
			   .WithColumn("IndoorMapFloorId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("IndoorMapId").AsString(128).NotNullable()
			   .WithColumn("Name").AsString(100).NotNullable()
			   .WithColumn("FloorOrder").AsInt32().NotNullable()
			   .WithColumn("ImageData").AsBinary(int.MaxValue).Nullable()
			   .WithColumn("ImageContentType").AsString(50).Nullable()
			   .WithColumn("BoundsNELat").AsDecimal(10, 7).Nullable()
			   .WithColumn("BoundsNELon").AsDecimal(10, 7).Nullable()
			   .WithColumn("BoundsSWLat").AsDecimal(10, 7).Nullable()
			   .WithColumn("BoundsSWLon").AsDecimal(10, 7).Nullable()
			   .WithColumn("Opacity").AsDecimal(3, 2).NotNullable().WithDefaultValue(0.8m)
			   .WithColumn("IsDeleted").AsBoolean().NotNullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable();

			Create.ForeignKey("FK_IndoorMapFloors_IndoorMaps")
				.FromTable("IndoorMapFloors").ForeignColumn("IndoorMapId")
				.ToTable("IndoorMaps").PrimaryColumn("IndoorMapId");

			Create.Table("IndoorMapZones")
			   .WithColumn("IndoorMapZoneId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("IndoorMapFloorId").AsString(128).NotNullable()
			   .WithColumn("Name").AsString(250).NotNullable()
			   .WithColumn("Description").AsString(500).Nullable()
			   .WithColumn("ZoneType").AsInt32().NotNullable()
			   .WithColumn("PixelGeometry").AsString(int.MaxValue).Nullable()
			   .WithColumn("GeoGeometry").AsString(int.MaxValue).Nullable()
			   .WithColumn("CenterPixelX").AsDouble().NotNullable()
			   .WithColumn("CenterPixelY").AsDouble().NotNullable()
			   .WithColumn("CenterLatitude").AsDecimal(10, 7).NotNullable()
			   .WithColumn("CenterLongitude").AsDecimal(10, 7).NotNullable()
			   .WithColumn("Color").AsString(50).Nullable()
			   .WithColumn("Metadata").AsString(int.MaxValue).Nullable()
			   .WithColumn("IsSearchable").AsBoolean().NotNullable()
			   .WithColumn("IsDeleted").AsBoolean().NotNullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable();

			Create.ForeignKey("FK_IndoorMapZones_IndoorMapFloors")
				.FromTable("IndoorMapZones").ForeignColumn("IndoorMapFloorId")
				.ToTable("IndoorMapFloors").PrimaryColumn("IndoorMapFloorId");

			Alter.Table("Calls")
				.AddColumn("IndoorMapZoneId").AsString(128).Nullable();

			Alter.Table("Calls")
				.AddColumn("IndoorMapFloorId").AsString(128).Nullable();
		}

		public override void Down()
		{
			Delete.ForeignKey("FK_IndoorMapZones_IndoorMapFloors").OnTable("IndoorMapZones");
			Delete.ForeignKey("FK_IndoorMapFloors_IndoorMaps").OnTable("IndoorMapFloors");
			Delete.ForeignKey("FK_IndoorMaps_Departments").OnTable("IndoorMaps");

			Delete.Column("IndoorMapZoneId").FromTable("Calls");
			Delete.Column("IndoorMapFloorId").FromTable("Calls");

			Delete.Table("IndoorMapZones");
			Delete.Table("IndoorMapFloors");
			Delete.Table("IndoorMaps");
		}
	}
}
