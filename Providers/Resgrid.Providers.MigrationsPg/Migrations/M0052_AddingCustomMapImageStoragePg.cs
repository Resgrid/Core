using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(52)]
	public class M0052_AddingCustomMapImageStoragePg : Migration
	{
		public override void Up()
		{
			// Extend CustomMapFloors with image-storage metadata.
			//
			// StorageType:
			//   0 = None          — no image uploaded
			//   1 = DatabaseBlob  — raw bytes stored in the Files table (≤ 10 MB)
			//   2 = TiledPyramid  — 256×256 PNG tile pyramid on filesystem (> 10 MB)
			//
			// ImageFileId is a soft FK to Files.FileId (no hard constraint so the
			// Files record can be deleted independently if needed).
			Alter.Table("CustomMapFloors".ToLower())
				.AddColumn("StorageType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0);

			Alter.Table("CustomMapFloors".ToLower())
				.AddColumn("ImageFileId".ToLower()).AsInt32().Nullable();

			Alter.Table("CustomMapFloors".ToLower())
				.AddColumn("ImageWidthPx".ToLower()).AsInt32().Nullable();

			Alter.Table("CustomMapFloors".ToLower())
				.AddColumn("ImageHeightPx".ToLower()).AsInt32().Nullable();

			Alter.Table("CustomMapFloors".ToLower())
				.AddColumn("TileZoomLevels".ToLower()).AsInt32().Nullable();

			// Rows that already carry an imageurl were created via the old
			// AddFloor upload path and store their bytes in the Files table.
			Execute.Sql(@"
				UPDATE custommapfloors
				SET    storagetype = 1
				WHERE  imageurl IS NOT NULL
				  AND  imageurl <> ''
				  AND  storagetype = 0;
			");
		}

		public override void Down()
		{
			Delete.Column("TileZoomLevels".ToLower()).FromTable("CustomMapFloors".ToLower());
			Delete.Column("ImageHeightPx".ToLower()).FromTable("CustomMapFloors".ToLower());
			Delete.Column("ImageWidthPx".ToLower()).FromTable("CustomMapFloors".ToLower());
			Delete.Column("ImageFileId".ToLower()).FromTable("CustomMapFloors".ToLower());
			Delete.Column("StorageType".ToLower()).FromTable("CustomMapFloors".ToLower());
		}
	}
}

