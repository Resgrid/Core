using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(52)]
	public class M0052_AddingCustomMapSupport : Migration
	{
		public override void Up()
		{
			// ── IndoorMaps – new columns ─────────────────────────────────────
			Alter.Table("IndoorMaps")
				.AddColumn("MapType").AsInt32().NotNullable().WithDefaultValue(0)
				.AddColumn("BoundsGeoJson").AsString(int.MaxValue).Nullable()
				.AddColumn("ThumbnailData").AsBinary(int.MaxValue).Nullable()
				.AddColumn("ThumbnailContentType").AsString(50).Nullable();

			// ── IndoorMapFloors – new columns ────────────────────────────────
			Alter.Table("IndoorMapFloors")
				.AddColumn("LayerType").AsInt32().NotNullable().WithDefaultValue(0)
				.AddColumn("IsTiled").AsBoolean().NotNullable().WithDefaultValue(false)
				.AddColumn("TileMinZoom").AsInt32().Nullable()
				.AddColumn("TileMaxZoom").AsInt32().Nullable()
				.AddColumn("SourceFileSize").AsInt64().Nullable()
				.AddColumn("GeoJsonData").AsString(int.MaxValue).Nullable();

			// ── IndoorMapZones – new columns ─────────────────────────────────
			Alter.Table("IndoorMapZones")
				.AddColumn("IsDispatchable").AsBoolean().NotNullable().WithDefaultValue(true);

			// ── CustomMapTiles ───────────────────────────────────────────────
			Create.Table("CustomMapTiles")
				.WithColumn("CustomMapTileId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("CustomMapLayerId").AsString(128).NotNullable()
				.WithColumn("ZoomLevel").AsInt32().NotNullable()
				.WithColumn("TileX").AsInt32().NotNullable()
				.WithColumn("TileY").AsInt32().NotNullable()
				.WithColumn("TileData").AsBinary(int.MaxValue).NotNullable()
				.WithColumn("TileContentType").AsString(50).NotNullable()
				.WithColumn("AddedOn").AsDateTime2().NotNullable();

			Create.ForeignKey("FK_CustomMapTiles_IndoorMapFloors")
				.FromTable("CustomMapTiles").ForeignColumn("CustomMapLayerId")
				.ToTable("IndoorMapFloors").PrimaryColumn("IndoorMapFloorId");

			Create.UniqueConstraint("UQ_CustomMapTiles_LayerZoomXY")
				.OnTable("CustomMapTiles")
				.Columns(new[] { "CustomMapLayerId", "ZoomLevel", "TileX", "TileY" });

			// ── CustomMapImports ─────────────────────────────────────────────
			Create.Table("CustomMapImports")
				.WithColumn("CustomMapImportId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("CustomMapId").AsString(128).NotNullable()
				.WithColumn("CustomMapLayerId").AsString(128).Nullable()
				.WithColumn("SourceFileName").AsString(500).NotNullable()
				.WithColumn("SourceFileType").AsInt32().NotNullable()
				.WithColumn("Status").AsInt32().NotNullable()
				.WithColumn("ErrorMessage").AsString(int.MaxValue).Nullable()
				.WithColumn("ImportedById").AsString(128).NotNullable()
				.WithColumn("ImportedOn").AsDateTime2().NotNullable();

			Create.ForeignKey("FK_CustomMapImports_IndoorMaps")
				.FromTable("CustomMapImports").ForeignColumn("CustomMapId")
				.ToTable("IndoorMaps").PrimaryColumn("IndoorMapId");

			Create.ForeignKey("FK_CustomMapImports_IndoorMapFloors")
				.FromTable("CustomMapImports").ForeignColumn("CustomMapLayerId")
				.ToTable("IndoorMapFloors").PrimaryColumn("IndoorMapFloorId");
		}

		public override void Down()
		{
			Delete.ForeignKey("FK_CustomMapImports_IndoorMapFloors").OnTable("CustomMapImports");
			Delete.ForeignKey("FK_CustomMapImports_IndoorMaps").OnTable("CustomMapImports");
			Delete.Table("CustomMapImports");

			Delete.UniqueConstraint("UQ_CustomMapTiles_LayerZoomXY").FromTable("CustomMapTiles");
			Delete.ForeignKey("FK_CustomMapTiles_IndoorMapFloors").OnTable("CustomMapTiles");
			Delete.Table("CustomMapTiles");

			Delete.Column("IsDispatchable").FromTable("IndoorMapZones");

			Delete.Column("LayerType").FromTable("IndoorMapFloors");
			Delete.Column("IsTiled").FromTable("IndoorMapFloors");
			Delete.Column("TileMinZoom").FromTable("IndoorMapFloors");
			Delete.Column("TileMaxZoom").FromTable("IndoorMapFloors");
			Delete.Column("SourceFileSize").FromTable("IndoorMapFloors");
			Delete.Column("GeoJsonData").FromTable("IndoorMapFloors");

			Delete.Column("MapType").FromTable("IndoorMaps");
			Delete.Column("BoundsGeoJson").FromTable("IndoorMaps");
			Delete.Column("ThumbnailData").FromTable("IndoorMaps");
			Delete.Column("ThumbnailContentType").FromTable("IndoorMaps");
		}
	}
}
