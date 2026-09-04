using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0169 (registry M0169, RMS-3): rmsevidenceartifacts. Lowercase unquoted identifiers,
	/// citext for text. One table serves all six evidence sources; the per-source manifest lives in manifestjson
	/// so the checksum covers everything that was attested to.
	/// </summary>
	[Migration(169)]
	public class M0169_AddRmsEvidenceArtifactsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsevidenceartifacts").Exists())
			{
				Create.Table("rmsevidenceartifacts")
					.WithColumn("rmsevidenceartifactid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("recordkind").AsInt32().NotNullable()
					.WithColumn("revisionid").AsString(36).Nullable()
					.WithColumn("kind").AsInt32().NotNullable()
					.WithColumn("title").AsCustom("citext").Nullable()
					.WithColumn("capturereason").AsCustom("citext").Nullable()
					.WithColumn("sourcesubsystem").AsCustom("citext").Nullable()
					.WithColumn("sourceentitytype").AsCustom("citext").Nullable()
					.WithColumn("sourceentityid").AsCustom("citext").Nullable()
					.WithColumn("identifierscheme").AsCustom("citext").Nullable()
					.WithColumn("sourceversion").AsCustom("citext").Nullable()
					.WithColumn("coveragestart").AsDateTime2().Nullable()
					.WithColumn("coverageend").AsDateTime2().Nullable()
					.WithColumn("manifestjson").AsCustom("citext").Nullable()
					.WithColumn("checksum").AsCustom("citext").Nullable()
					.WithColumn("bytesize").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("sourceitemcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("storagereference").AsCustom("citext").Nullable()
					.WithColumn("classification").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("retentionyears").AsInt32().Nullable()
					.WithColumn("capturedbyuserid").AsString(128).Nullable()
					.WithColumn("capturedon").AsDateTime2().NotNullable()
					.WithColumn("originclient").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("supersededbyartifactid").AsString(36).Nullable()
					.WithColumn("supersededon").AsDateTime2().Nullable()
					.WithColumn("protectedenvelope").AsCustom("citext").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsevidenceartifacts_department_record_revision ON rmsevidenceartifacts (departmentid, recordid, revisionid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsevidenceartifacts_department_kind ON rmsevidenceartifacts (departmentid, kind, capturedon DESC);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("rmsevidenceartifacts").Exists())
				Delete.Table("rmsevidenceartifacts");
		}
	}
}
