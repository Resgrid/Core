using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>Enforces one SSO configuration per provider type for each department.</summary>
	[Migration(91)]
	public class M0091_AddUniqueDepartmentSsoProvider : Migration
	{
		private const string TableName = "DepartmentSsoConfigs";
		private const string IndexName = "UX_DepartmentSsoConfigs_DepartmentId_SsoProviderType";

		public override void Up()
		{
			if (Schema.Table(TableName).Exists() && !Schema.Table(TableName).Index(IndexName).Exists())
			{
				Create.Index(IndexName)
					.OnTable(TableName)
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("SsoProviderType").Ascending()
					.WithOptions().Unique();
			}
		}

		public override void Down()
		{
			if (Schema.Table(TableName).Exists() && Schema.Table(TableName).Index(IndexName).Exists())
				Delete.Index(IndexName).OnTable(TableName);
		}
	}
}
