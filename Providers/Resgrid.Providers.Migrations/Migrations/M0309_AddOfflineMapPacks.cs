using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Offline map packs (int-Coordination maplibre-mapping-routing-navigation-plan.md §19.4): one row per built or queued
	/// pack. Base-map cells are shared (DepartmentId null) and keyed by cell, max zoom and archive version; department
	/// packs (custom map rasters, sprite, incident and corridor packs) carry their department. Files live in the pack
	/// store (MappingConfig.OfflinePackStorage), not here. Existence-guarded for safe retry.
	/// </summary>
	[Migration(309)]
	public class M0309_AddOfflineMapPacks : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("OfflineMapPacks").Exists())
			{
				Create.Table("OfflineMapPacks")
					.WithColumn("OfflineMapPackId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().Nullable()
					.WithColumn("Kind").AsInt32().NotNullable()
					.WithColumn("PackKey").AsString(200).NotNullable()
					.WithColumn("Name").AsString(200).Nullable()
					.WithColumn("SourceId").AsString(64).Nullable()
					.WithColumn("Version").AsString(100).Nullable()
					.WithColumn("Bounds").AsString(100).Nullable()
					.WithColumn("MinZoom").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("MaxZoom").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SizeBytes").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("Sha256").AsString(64).Nullable()
					.WithColumn("StoragePath").AsString(400).Nullable()
					.WithColumn("FilesJson").AsString(int.MaxValue).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("BuiltOn").AsDateTime2().Nullable()
					.WithColumn("LastRequestedOn").AsDateTime2().Nullable()
					.WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Error").AsString(1000).Nullable();

				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX IX_OfflineMapPacks_PackKey ON OfflineMapPacks (PackKey);");
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_OfflineMapPacks_Department ON OfflineMapPacks (DepartmentId) WHERE DepartmentId IS NOT NULL;");
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_OfflineMapPacks_Status ON OfflineMapPacks (Status, AddedOn);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("OfflineMapPacks").Exists())
				Delete.Table("OfflineMapPacks");
		}
	}
}
