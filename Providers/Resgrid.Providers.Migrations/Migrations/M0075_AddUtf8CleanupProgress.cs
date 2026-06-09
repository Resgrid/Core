using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(75)]
	public class M0075_AddUtf8CleanupProgress : Migration
	{
		public override void Up()
		{
			// Per-table watermark for the nightly UTF-8 data cleanup worker so each run resumes
			// where the previous one stopped while staying idempotent.
			// Guarded so the migration is safe to re-run / safe on databases where a prior partial
			// apply already created the table.
			if (!Schema.Table("Utf8CleanupProgress").Exists())
			{
				Create.Table("Utf8CleanupProgress")
					.WithColumn("TableName").AsString(256).NotNullable().PrimaryKey()
					.WithColumn("LastProcessedKey").AsString(450).Nullable()
					.WithColumn("LastCompletedUtc").AsDateTime().Nullable()
					.WithColumn("RowsScanned").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("RowsFixed").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("UpdatedOnUtc").AsDateTime().NotNullable();
			}
		}

		public override void Down()
		{
			Delete.Table("Utf8CleanupProgress");
		}
	}
}
