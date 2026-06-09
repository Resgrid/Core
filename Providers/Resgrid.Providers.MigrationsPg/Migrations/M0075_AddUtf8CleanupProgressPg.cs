using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(75)]
	public class M0075_AddUtf8CleanupProgressPg : Migration
	{
		public override void Up()
		{
			// Per-table watermark for the nightly UTF-8 data cleanup worker so each run resumes
			// where the previous one stopped while staying idempotent.
			// Guarded so the migration is safe to re-run / safe on databases where a prior partial
			// apply already created the table.
			if (!Schema.Table("utf8cleanupprogress").Exists())
			{
				Create.Table("utf8cleanupprogress")
					.WithColumn("tablename").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("lastprocessedkey").AsCustom("text").Nullable()
					.WithColumn("lastcompletedutc").AsDateTime().Nullable()
					.WithColumn("rowsscanned").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("rowsfixed").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("updatedonutc").AsDateTime().NotNullable();
			}
		}

		public override void Down()
		{
			Delete.Table("utf8cleanupprogress");
		}
	}
}
