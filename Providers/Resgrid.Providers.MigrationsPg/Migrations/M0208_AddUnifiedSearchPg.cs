using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Unified Search Phase 1b/2 (plan R4, R5; registry §4F): searchprojections, searchindexstates, searchindexleases
	/// and the Search.Unified flag seed. PostgreSQL twin of the SQL Server migration. Existence-guarded for safe retry.
	/// </summary>
	[Migration(208)]
	public class M0208_AddUnifiedSearchPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("searchprojections").Exists())
			{
				Create.Table("searchprojections")
					.WithColumn("searchprojectionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("entitytype").AsCustom("citext").NotNullable()
					.WithColumn("entityid").AsCustom("citext").NotNullable()
					.WithColumn("title").AsCustom("citext").Nullable()
					.WithColumn("summary").AsCustom("citext").Nullable()
					.WithColumn("searchtext").AsCustom("citext").Nullable()
					.WithColumn("keywords").AsCustom("citext").Nullable()
					.WithColumn("category").AsCustom("citext").Nullable()
					.WithColumn("status").AsCustom("citext").Nullable()
					.WithColumn("priority").AsInt32().Nullable()
					.WithColumn("groupid").AsInt32().Nullable()
					.WithColumn("owneruserid").AsCustom("citext").Nullable()
					.WithColumn("participantuserids").AsCustom("citext").Nullable()
					.WithColumn("isadminonly").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("occurredon").AsDateTime2().NotNullable()
					.WithColumn("url").AsCustom("citext").Nullable()
					.WithColumn("metadatajson").AsCustom("citext").Nullable()
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("policyepoch").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("includesprotectedtext").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_searchprojections_department_entity ON searchprojections (departmentid, entitytype, entityid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_searchprojections_department_modified ON searchprojections (departmentid, modifiedon);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_searchprojections_department_type_modified ON searchprojections (departmentid, entitytype, modifiedon);");
			}

			if (!Schema.Table("searchindexstates").Exists())
			{
				Create.Table("searchindexstates")
					.WithColumn("searchindexstateid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("indexname").AsCustom("citext").NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("schemaversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("policyepoch").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("generation").AsCustom("citext").NotNullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("documentcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("lastrebuilton").AsDateTime2().Nullable()
					.WithColumn("lastindexedmodifiedon").AsDateTime2().Nullable()
					.WithColumn("rebuildrequestedon").AsDateTime2().Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable();

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_searchindexstates_index_department ON searchindexstates (indexname, departmentid);");
			}

			if (!Schema.Table("searchindexleases").Exists())
			{
				Create.Table("searchindexleases")
					.WithColumn("indexname").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("leaseowner").AsCustom("citext").Nullable()
					.WithColumn("leaseexpireson").AsDateTime2().Nullable()
					.WithColumn("lastpublishedrevision").AsCustom("citext").Nullable()
					.WithColumn("lastpublishedon").AsDateTime2().Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable();
			}

			Execute.Sql("INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) SELECT 'Search.Unified', 'Unified Search', 'Cross-entity search (calls, units, personnel, contacts, messages, documents, notes, records) and the system-functionality command palette. Requires the search host (SearchConfig.Enabled) in every process. Seeded off.', 'Search', false WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Search.Unified');");
		}

		public override void Down()
		{
			if (Schema.Table("searchindexleases").Exists())
				Delete.Table("searchindexleases");

			if (Schema.Table("searchindexstates").Exists())
				Delete.Table("searchindexstates");

			if (Schema.Table("searchprojections").Exists())
				Delete.Table("searchprojections");

			// Flag row preserved, as in the SQL Server twin.
		}
	}
}
