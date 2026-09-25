using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(239)]
	public class M0239_AddAdminAssistDiagnosticsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("adminassistdiagnosticruns").Exists())
				Create.Table("adminassistdiagnosticruns")
					.WithColumn("id").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("flow").AsString(32).NotNullable()
					.WithColumn("createdonutc").AsDateTime().NotNullable()
					.WithColumn("revision").AsInt64().NotNullable()
					.WithColumn("deleted").AsBoolean().WithDefaultValue(false).NotNullable()
					.WithColumn("content").AsString(int.MaxValue).NotNullable()
					.WithColumn("isprotected").AsBoolean().WithDefaultValue(false).NotNullable()
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();
			if (!Schema.Table("adminassistdiagnosticruns").Index("ix_adminassistdiagnosticruns_scope").Exists())
				Create.Index("ix_adminassistdiagnosticruns_scope").OnTable("adminassistdiagnosticruns").OnColumn("departmentid").Ascending().OnColumn("userid").Ascending().OnColumn("createdonutc").Ascending();
			if (!Schema.Table("adminassistdiagnosticleases").Exists())
				Create.Table("adminassistdiagnosticleases")
					.WithColumn("id").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("expiresonutc").AsDateTime().NotNullable();
			if (!Schema.Table("adminassistdiagnosticleases").Index("ix_adminassistdiagnosticleases_scope").Exists())
				Create.Index("ix_adminassistdiagnosticleases_scope").OnTable("adminassistdiagnosticleases").OnColumn("departmentid").Ascending().OnColumn("userid").Ascending().OnColumn("expiresonutc").Ascending();
		}
		public override void Down() => throw new System.NotSupportedException("Disable troubleshooting; protected diagnostic schema follows the approved data lifecycle.");
	}
}
