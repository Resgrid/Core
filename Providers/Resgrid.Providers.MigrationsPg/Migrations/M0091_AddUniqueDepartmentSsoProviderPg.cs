using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>Enforces one SSO configuration per provider type for each department.</summary>
	[Migration(91)]
	public class M0091_AddUniqueDepartmentSsoProviderPg : Migration
	{
		private const string TableName = "departmentssoconfigs";
		private const string IndexName = "ux_departmentssoconfigs_departmentid_ssoprovidertype";

		public override void Up()
		{
			if (Schema.Table(TableName).Exists() && !Schema.Table(TableName).Index(IndexName).Exists())
			{
				Create.Index(IndexName)
					.OnTable(TableName)
					.OnColumn("departmentid").Ascending()
					.OnColumn("ssoprovidertype").Ascending()
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
