using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(52)]
	public class M0052_AddingCustomMapSupportPg : Migration
	{
		public override void Up()
		{
			// ── IndoorMaps – new columns ─────────────────────────────────────
			Alter.Table("indoormaps")
				.AddColumn("maptype").AsInt32().NotNullable().WithDefaultValue(0)
				.AddColumn("boundsgeojson").AsCustom("text").Nullable()
				.AddColumn("thumbnaildata").AsCustom("bytea").Nullable()
				.AddColumn("thumbnailcontenttype").AsCustom("citext").Nullable();

			// ── IndoorMapFloors – new columns ────────────────────────────────
			Alter.Table("indoormapfloors")
				.AddColumn("layertype").AsInt32().NotNullable().WithDefaultValue(0)
				.AddColumn("istiled").AsBoolean().NotNullable().WithDefaultValue(false)
				.AddColumn("tileminzoom").AsInt32().Nullable()
				.AddColumn("tilemaxzoom").AsInt32().Nullable()
				.AddColumn("sourcefilesize").AsInt64().Nullable()
				.AddColumn("geojsondata").AsCustom("text").Nullable();

			// ── IndoorMapZones – new columns ─────────────────────────────────
			Alter.Table("indoormapzones")
				.AddColumn("isdispatchable").AsBoolean().NotNullable().WithDefaultValue(true);

			// ── CustomMapTiles ───────────────────────────────────────────────
			Create.Table("custommaptiles")
				.WithColumn("custommaptileid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("custommaplayerid").AsCustom("citext").NotNullable()
				.WithColumn("zoomlevel").AsInt32().NotNullable()
				.WithColumn("tilex").AsInt32().NotNullable()
				.WithColumn("tiley").AsInt32().NotNullable()
				.WithColumn("tiledata").AsCustom("bytea").NotNullable()
				.WithColumn("tilecontenttype").AsCustom("citext").NotNullable()
				.WithColumn("addedon").AsDateTime().NotNullable();

			Create.ForeignKey("fk_custommaptiles_indoormapfloors")
				.FromTable("custommaptiles").ForeignColumn("custommaplayerid")
				.ToTable("indoormapfloors").PrimaryColumn("indoormapfloorid");

			Create.Index("ix_custommaptiles_layerzoomxy")
				.OnTable("custommaptiles")
				.OnColumn("custommaplayerid").Ascending()
				.OnColumn("zoomlevel").Ascending()
				.OnColumn("tilex").Ascending()
				.OnColumn("tiley").Ascending()
				.WithOptions().Unique();

			// ── CustomMapImports ─────────────────────────────────────────────
			Create.Table("custommapimports")
				.WithColumn("custommapimportid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("custommapid").AsCustom("citext").NotNullable()
				.WithColumn("custommaplayerid").AsCustom("citext").Nullable()
				.WithColumn("sourcefilename").AsCustom("citext").NotNullable()
				.WithColumn("sourcefiletype").AsInt32().NotNullable()
				.WithColumn("status").AsInt32().NotNullable()
				.WithColumn("errormessage").AsCustom("text").Nullable()
				.WithColumn("importedbyid").AsCustom("citext").NotNullable()
				.WithColumn("importedon").AsDateTime().NotNullable();

			Create.ForeignKey("fk_custommapimports_indoormaps")
				.FromTable("custommapimports").ForeignColumn("custommapid")
				.ToTable("indoormaps").PrimaryColumn("indoormapid");

			Create.ForeignKey("fk_custommapimports_indoormapfloors")
				.FromTable("custommapimports").ForeignColumn("custommaplayerid")
				.ToTable("indoormapfloors").PrimaryColumn("indoormapfloorid");

			Create.Index("ix_custommapimports_custommapid")
				.OnTable("custommapimports")
				.OnColumn("custommapid");
		}

		public override void Down()
		{
			Delete.Index("ix_custommapimports_custommapid").OnTable("custommapimports");
			Delete.ForeignKey("fk_custommapimports_indoormapfloors").OnTable("custommapimports");
			Delete.ForeignKey("fk_custommapimports_indoormaps").OnTable("custommapimports");
			Delete.Table("custommapimports");

			Delete.Index("ix_custommaptiles_layerzoomxy").OnTable("custommaptiles");
			Delete.ForeignKey("fk_custommaptiles_indoormapfloors").OnTable("custommaptiles");
			Delete.Table("custommaptiles");

			Delete.Column("isdispatchable").FromTable("indoormapzones");

			Delete.Column("layertype").FromTable("indoormapfloors");
			Delete.Column("istiled").FromTable("indoormapfloors");
			Delete.Column("tileminzoom").FromTable("indoormapfloors");
			Delete.Column("tilemaxzoom").FromTable("indoormapfloors");
			Delete.Column("sourcefilesize").FromTable("indoormapfloors");
			Delete.Column("geojsondata").FromTable("indoormapfloors");

			Delete.Column("maptype").FromTable("indoormaps");
			Delete.Column("boundsgeojson").FromTable("indoormaps");
			Delete.Column("thumbnaildata").FromTable("indoormaps");
			Delete.Column("thumbnailcontenttype").FromTable("indoormaps");
		}
	}
}
