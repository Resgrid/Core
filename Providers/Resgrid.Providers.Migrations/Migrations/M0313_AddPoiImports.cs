using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Background POI imports: one row per upload, holding the parsed rows until worker 79 has geocoded the addresses and
	/// saved the POIs. Pois.PoiImportId + PoiImportRow tag each imported POI with its import and row (unique together), so an
	/// import resumed after a worker restart carries on after the last row it saved and can never save a row twice.
	/// PoiImports.ClaimToken names the worker run that owns the import; a run replaced after stalling can no longer write to
	/// it. No foreign keys: the import row is purged with the department (UnlinkedDepartmentCleanup) and finished imports
	/// age out after 90 days. Existence-guarded for safe retry.
	/// </summary>
	[Migration(313)]
	public class M0313_AddPoiImports : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("PoiImports").Exists())
			{
				Create.Table("PoiImports")
					.WithColumn("PoiImportId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("PoiTypeId").AsInt32().NotNullable()
					.WithColumn("PoiTypeName").AsString(250).Nullable()
					.WithColumn("UserId").AsString(128).Nullable()
					.WithColumn("FileName").AsString(260).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("TotalRows").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SkippedCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ImportedCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("GeocodedCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("UnlocatedCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("LookupFailedCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("LookupNotConfigured").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("NotTriedCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("GeocodingLimitReached").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("PendingRetryCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RetryAfter").AsDateTime2().Nullable()
					.WithColumn("RowsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("UnlocatedJson").AsString(int.MaxValue).Nullable()
					.WithColumn("Error").AsString(1000).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("StartedOn").AsDateTime2().Nullable()
					.WithColumn("HeartbeatOn").AsDateTime2().Nullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable()
					.WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ClaimToken").AsString(36).Nullable();

				Execute.Sql("CREATE NONCLUSTERED INDEX IX_PoiImports_Status ON PoiImports (Status, AddedOn);");
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_PoiImports_Department ON PoiImports (DepartmentId, AddedOn);");
			}

			if (!Schema.Table("Pois").Column("PoiImportId").Exists())
			{
				Alter.Table("Pois").AddColumn("PoiImportId").AsString(36).Nullable();
				Alter.Table("Pois").AddColumn("PoiImportRow").AsInt32().Nullable();
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_Pois_PoiImportRow ON Pois (PoiImportId, PoiImportRow) WHERE PoiImportId IS NOT NULL AND PoiImportRow IS NOT NULL;");
			}
		}

		public override void Down()
		{
			if (Schema.Table("Pois").Column("PoiImportId").Exists())
			{
				Execute.Sql("DROP INDEX IF EXISTS UX_Pois_PoiImportRow ON Pois;");
				Delete.Column("PoiImportRow").FromTable("Pois");
				Delete.Column("PoiImportId").FromTable("Pois");
			}

			if (Schema.Table("PoiImports").Exists())
				Delete.Table("PoiImports");
		}
	}
}
