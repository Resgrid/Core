using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(242)]
	public class M0242_AddAdminAssistPlans : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("AdminAssistPlans").Exists())
				Create.Table("AdminAssistPlans")
					.WithColumn("Id").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("CreatedOnUtc").AsDateTime().NotNullable()
					.WithColumn("UpdatedOnUtc").AsDateTime().NotNullable()
					.WithColumn("ClosedOnUtc").AsDateTime().Nullable()
					.WithColumn("Revision").AsInt64().NotNullable()
					.WithColumn("Status").AsString(32).NotNullable()
					.WithColumn("Shared").AsBoolean().WithDefaultValue(false).NotNullable()
					.WithColumn("Deleted").AsBoolean().WithDefaultValue(false).NotNullable()
					.WithColumn("Content").AsString(int.MaxValue).NotNullable()
					.WithColumn("IsProtected").AsBoolean().WithDefaultValue(false).NotNullable()
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
			if (!Schema.Table("AdminAssistPlans").Index("IX_AdminAssistPlans_Scope").Exists())
				Create.Index("IX_AdminAssistPlans_Scope").OnTable("AdminAssistPlans").OnColumn("DepartmentId").Ascending().OnColumn("UpdatedOnUtc").Descending();
		}
		public override void Down() => throw new System.NotSupportedException("Disable plans; protected proposals follow the approved data lifecycle.");
	}
}
