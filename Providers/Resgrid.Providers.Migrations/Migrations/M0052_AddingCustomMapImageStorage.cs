using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(52)]
	public class M0052_AddingCustomMapImageStorage : Migration
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
			Alter.Table("CustomMapFloors")
				.AddColumn("StorageType").AsInt32().NotNullable().WithDefaultValue(0);

			Alter.Table("CustomMapFloors")
				.AddColumn("ImageFileId").AsInt32().Nullable();

			Alter.Table("CustomMapFloors")
				.AddColumn("ImageWidthPx").AsInt32().Nullable();

			Alter.Table("CustomMapFloors")
				.AddColumn("ImageHeightPx").AsInt32().Nullable();

			Alter.Table("CustomMapFloors")
				.AddColumn("TileZoomLevels").AsInt32().Nullable();

			// Rows that already carry an ImageUrl were created via the old
			// AddFloor upload path and store their bytes in the Files table.
			Execute.Sql(@"
				UPDATE [CustomMapFloors]
				SET    [StorageType] = 1
				WHERE  [ImageUrl] IS NOT NULL
				  AND  [ImageUrl] <> ''
				  AND  [StorageType] = 0;
			");
		}

		public override void Down()
		{
			Delete.Column("TileZoomLevels").FromTable("CustomMapFloors");
			Delete.Column("ImageHeightPx").FromTable("CustomMapFloors");
			Delete.Column("ImageWidthPx").FromTable("CustomMapFloors");
			Delete.Column("ImageFileId").FromTable("CustomMapFloors");
			Delete.Column("StorageType").FromTable("CustomMapFloors");
		}
	}
}

