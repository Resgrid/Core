using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0353_AddKnowledgeIndex: knowledgechunks (embedding as bytea of little-endian float32 values, the same code path
	/// as SQL Server, no vector extension), knowledgeindexrequests (unique per item) and knowledgeindexstates. Existence-guarded.
	/// </summary>
	[Migration(353)]
	public class M0353_AddKnowledgeIndexPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("knowledgechunks").Exists())
			{
				Create.Table("knowledgechunks")
					.WithColumn("knowledgechunkid").AsInt64().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("sourcekind").AsInt32().NotNullable()
					.WithColumn("itemid").AsString(64).NotNullable()
					.WithColumn("versionid").AsString(64).Nullable()
					.WithColumn("versionlabel").AsString(32).Nullable()
					.WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("anchor").AsString(128).Nullable()
					.WithColumn("headingpath").AsString(500).Nullable()
					.WithColumn("title").AsString(250).NotNullable()
					.WithColumn("text").AsCustom("text").NotNullable()
					.WithColumn("texthash").AsString(64).NotNullable()
					.WithColumn("locale").AsString(16).Nullable()
					.WithColumn("effectiveon").AsDateTime2().Nullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("audiencejson").AsString(4000).Nullable()
					.WithColumn("accessflags").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("embedding").AsCustom("bytea").Nullable()
					.WithColumn("embeddingmodel").AsString(128).Nullable()
					.WithColumn("embeddedon").AsDateTime2().Nullable()
					.WithColumn("indexedon").AsDateTime2().NotNullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_knowledgechunks_item ON knowledgechunks (departmentid, sourcekind, itemid);");

			if (!Schema.Table("knowledgeindexrequests").Exists())
			{
				Create.Table("knowledgeindexrequests")
					.WithColumn("knowledgeindexrequestid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("sourcekind").AsInt32().NotNullable()
					.WithColumn("itemid").AsString(64).NotNullable().WithDefaultValue("")
					.WithColumn("requestedon").AsDateTime2().NotNullable()
					.WithColumn("attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("revision").AsInt32().NotNullable().WithDefaultValue(0);
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_knowledgeindexrequests_item ON knowledgeindexrequests (departmentid, sourcekind, itemid);");

			if (!Schema.Table("knowledgeindexstates").Exists())
			{
				Create.Table("knowledgeindexstates")
					.WithColumn("departmentid").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("revision").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("active").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("nextrepairon").AsDateTime2().Nullable()
					.WithColumn("lastrepairon").AsDateTime2().Nullable()
					.WithColumn("lastindexedon").AsDateTime2().Nullable()
					.WithColumn("chunkcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("embeddedcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("embeddingmodel").AsString(128).Nullable()
					.WithColumn("lasterror").AsString(500).Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_knowledgeindexstates_repair ON knowledgeindexstates (active, nextrepairon);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS knowledgeindexstates;");
			Execute.Sql("DROP TABLE IF EXISTS knowledgeindexrequests;");
			Execute.Sql("DROP TABLE IF EXISTS knowledgechunks;");
		}
	}
}
