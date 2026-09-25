using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(239)]
	public class M0239_AddAdminAssistDiagnostics : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("AdminAssistDiagnosticRuns").Exists())
				Create.Table("AdminAssistDiagnosticRuns")
					.WithColumn("Id").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("Flow").AsString(32).NotNullable()
					.WithColumn("CreatedOnUtc").AsDateTime().NotNullable()
					.WithColumn("Revision").AsInt64().NotNullable()
					.WithColumn("Deleted").AsBoolean().WithDefaultValue(false).NotNullable()
					.WithColumn("Content").AsString(int.MaxValue).NotNullable()
					.WithColumn("IsProtected").AsBoolean().WithDefaultValue(false).NotNullable()
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
			if (!Schema.Table("AdminAssistDiagnosticRuns").Index("IX_AdminAssistDiagnosticRuns_Scope").Exists())
				Create.Index("IX_AdminAssistDiagnosticRuns_Scope").OnTable("AdminAssistDiagnosticRuns").OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending().OnColumn("CreatedOnUtc").Ascending();
			if (!Schema.Table("AdminAssistDiagnosticLeases").Exists())
				Create.Table("AdminAssistDiagnosticLeases")
					.WithColumn("Id").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("ExpiresOnUtc").AsDateTime().NotNullable();
			if (!Schema.Table("AdminAssistDiagnosticLeases").Index("IX_AdminAssistDiagnosticLeases_Scope").Exists())
				Create.Index("IX_AdminAssistDiagnosticLeases_Scope").OnTable("AdminAssistDiagnosticLeases").OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending().OnColumn("ExpiresOnUtc").Ascending();
		}
		public override void Down() => throw new System.NotSupportedException("Disable troubleshooting; protected diagnostic schema follows the approved data lifecycle.");
	}
}
