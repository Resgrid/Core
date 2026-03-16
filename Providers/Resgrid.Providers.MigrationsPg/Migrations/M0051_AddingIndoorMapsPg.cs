using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(51)]
	public class M0051_AddingIndoorMapsPg : Migration
	{
		public override void Up()
		{
			// ── IndoorMaps ────────────────────────────────────────────────────
			Create.Table("IndoorMaps".ToLower())
				.WithColumn("IndoorMapId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
				.WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("Description".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("CenterLatitude".ToLower()).AsDecimal(10, 7).NotNullable()
				.WithColumn("CenterLongitude".ToLower()).AsDecimal(10, 7).NotNullable()
				.WithColumn("BoundsNELat".ToLower()).AsDecimal(10, 7).NotNullable()
				.WithColumn("BoundsNELon".ToLower()).AsDecimal(10, 7).NotNullable()
				.WithColumn("BoundsSWLat".ToLower()).AsDecimal(10, 7).NotNullable()
				.WithColumn("BoundsSWLon".ToLower()).AsDecimal(10, 7).NotNullable()
				.WithColumn("DefaultFloorId".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("IsDeleted".ToLower()).AsBoolean().NotNullable()
				.WithColumn("AddedById".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("AddedOn".ToLower()).AsDateTime().NotNullable()
				.WithColumn("UpdatedById".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("UpdatedOn".ToLower()).AsDateTime().Nullable();

			Create.ForeignKey("FK_IndoorMaps_Departments".ToLower())
				.FromTable("IndoorMaps".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());

			Create.Index("IX_IndoorMaps_DepartmentId".ToLower())
				.OnTable("IndoorMaps".ToLower())
				.OnColumn("DepartmentId".ToLower());

			// ── IndoorMapFloors ───────────────────────────────────────────────
			Create.Table("IndoorMapFloors".ToLower())
				.WithColumn("IndoorMapFloorId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("IndoorMapId".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("FloorOrder".ToLower()).AsInt32().NotNullable()
				.WithColumn("ImageData".ToLower()).AsCustom("bytea").Nullable()
				.WithColumn("ImageContentType".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("BoundsNELat".ToLower()).AsDecimal(10, 7).Nullable()
				.WithColumn("BoundsNELon".ToLower()).AsDecimal(10, 7).Nullable()
				.WithColumn("BoundsSWLat".ToLower()).AsDecimal(10, 7).Nullable()
				.WithColumn("BoundsSWLon".ToLower()).AsDecimal(10, 7).Nullable()
				.WithColumn("Opacity".ToLower()).AsDecimal(3, 2).NotNullable().WithDefaultValue(0.8m)
				.WithColumn("IsDeleted".ToLower()).AsBoolean().NotNullable()
				.WithColumn("AddedOn".ToLower()).AsDateTime().NotNullable();

			Create.ForeignKey("FK_IndoorMapFloors_IndoorMaps".ToLower())
				.FromTable("IndoorMapFloors".ToLower()).ForeignColumn("IndoorMapId".ToLower())
				.ToTable("IndoorMaps".ToLower()).PrimaryColumn("IndoorMapId".ToLower());

			Create.Index("IX_IndoorMapFloors_IndoorMapId".ToLower())
				.OnTable("IndoorMapFloors".ToLower())
				.OnColumn("IndoorMapId".ToLower());

			// ── IndoorMapZones ────────────────────────────────────────────────
			Create.Table("IndoorMapZones".ToLower())
				.WithColumn("IndoorMapZoneId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("IndoorMapFloorId".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("Description".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("ZoneType".ToLower()).AsInt32().NotNullable()
				.WithColumn("PixelGeometry".ToLower()).AsCustom("text").Nullable()
				.WithColumn("GeoGeometry".ToLower()).AsCustom("text").Nullable()
				.WithColumn("CenterPixelX".ToLower()).AsDouble().NotNullable()
				.WithColumn("CenterPixelY".ToLower()).AsDouble().NotNullable()
				.WithColumn("CenterLatitude".ToLower()).AsDecimal(10, 7).NotNullable()
				.WithColumn("CenterLongitude".ToLower()).AsDecimal(10, 7).NotNullable()
				.WithColumn("Color".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("Metadata".ToLower()).AsCustom("text").Nullable()
				.WithColumn("IsSearchable".ToLower()).AsBoolean().NotNullable()
				.WithColumn("IsDeleted".ToLower()).AsBoolean().NotNullable()
				.WithColumn("AddedOn".ToLower()).AsDateTime().NotNullable();

			Create.ForeignKey("FK_IndoorMapZones_IndoorMapFloors".ToLower())
				.FromTable("IndoorMapZones".ToLower()).ForeignColumn("IndoorMapFloorId".ToLower())
				.ToTable("IndoorMapFloors".ToLower()).PrimaryColumn("IndoorMapFloorId".ToLower());

			Create.Index("IX_IndoorMapZones_IndoorMapFloorId".ToLower())
				.OnTable("IndoorMapZones".ToLower())
				.OnColumn("IndoorMapFloorId".ToLower());

			// ── Calls (additional columns) ────────────────────────────────────
			Alter.Table("Calls".ToLower())
				.AddColumn("IndoorMapZoneId".ToLower()).AsCustom("citext").Nullable()
				.AddColumn("IndoorMapFloorId".ToLower()).AsCustom("citext").Nullable();
		}

		public override void Down()
		{
			Delete.Index("IX_IndoorMapZones_IndoorMapFloorId".ToLower()).OnTable("IndoorMapZones".ToLower());
			Delete.ForeignKey("FK_IndoorMapZones_IndoorMapFloors".ToLower()).OnTable("IndoorMapZones".ToLower());

			Delete.Index("IX_IndoorMapFloors_IndoorMapId".ToLower()).OnTable("IndoorMapFloors".ToLower());
			Delete.ForeignKey("FK_IndoorMapFloors_IndoorMaps".ToLower()).OnTable("IndoorMapFloors".ToLower());

			Delete.Index("IX_IndoorMaps_DepartmentId".ToLower()).OnTable("IndoorMaps".ToLower());
			Delete.ForeignKey("FK_IndoorMaps_Departments".ToLower()).OnTable("IndoorMaps".ToLower());

			Delete.Column("IndoorMapZoneId".ToLower()).FromTable("Calls".ToLower());
			Delete.Column("IndoorMapFloorId".ToLower()).FromTable("Calls".ToLower());

			Delete.Table("IndoorMapZones".ToLower());
			Delete.Table("IndoorMapFloors".ToLower());
			Delete.Table("IndoorMaps".ToLower());
		}
	}
}
