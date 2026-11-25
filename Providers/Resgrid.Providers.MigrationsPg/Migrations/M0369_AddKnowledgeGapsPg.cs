using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0369_AddKnowledgeGaps: knowledgegaps (member ids citext). Existence-guarded.</summary>
	[Migration(369)]
	public class M0369_AddKnowledgeGapsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("knowledgegaps").Exists())
			{
				Create.Table("knowledgegaps")
					.WithColumn("knowledgegapid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").Nullable()
					.WithColumn("askerrole").AsString(250).Nullable()
					.WithColumn("question").AsString(2000).Nullable()
					.WithColumn("reason").AsInt32().NotNullable()
					.WithColumn("surface").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("askedon").AsDateTime2().NotNullable()
					.WithColumn("nearestsourcesjson").AsString(4000).Nullable()
					.WithColumn("contextkind").AsInt32().Nullable()
					.WithColumn("contextitemid").AsString(64).Nullable()
					.WithColumn("aigenerationid").AsString(36).Nullable()
					.WithColumn("resolvedon").AsDateTime2().Nullable()
					.WithColumn("resolvedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("resolutionnote").AsString(500).Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_knowledgegaps_department ON knowledgegaps (departmentid, status, askedon DESC);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS knowledgegaps;");
		}
	}
}
