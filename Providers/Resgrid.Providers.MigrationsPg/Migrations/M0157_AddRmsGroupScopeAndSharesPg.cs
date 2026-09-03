using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Records (RMS) cross-group visibility (RMS plan section 5.7.1, registry M0157):
	/// rmsrecordgroupscopes (materialized visibility set) and rmsrecordshares (explicit, audited
	/// grants). PostgreSQL twin of the SQL Server migration. Existence-guarded for safe retry.
	/// </summary>
	[Migration(157)]
	public class M0157_AddRmsGroupScopeAndSharesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsrecordgroupscopes").Exists())
			{
				Create.Table("rmsrecordgroupscopes")
					.WithColumn("rmsrecordgroupscopeid").AsInt64().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("recordid").AsCustom("citext").NotNullable()
					.WithColumn("departmentgroupid").AsInt32().NotNullable()
					.WithColumn("anchortype").AsInt32().NotNullable()
					.WithColumn("createdon").AsDateTime2().NotNullable();

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsrecordgroupscopes_record_group_anchor ON rmsrecordgroupscopes (departmentid, recordid, departmentgroupid, anchortype);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordgroupscopes_department_group_record ON rmsrecordgroupscopes (departmentid, departmentgroupid, recordid);");
			}

			if (!Schema.Table("rmsrecordshares").Exists())
			{
				Create.Table("rmsrecordshares")
					.WithColumn("rmsrecordshareid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("recordid").AsCustom("citext").NotNullable()
					.WithColumn("departmentgroupid").AsInt32().NotNullable()
					.WithColumn("grantedbyuserid").AsCustom("citext").NotNullable()
					.WithColumn("grantedon").AsDateTime2().NotNullable()
					.WithColumn("reason").AsCustom("citext").Nullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("revokedon").AsDateTime2().Nullable()
					.WithColumn("revokedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordshares_department_record ON rmsrecordshares (departmentid, recordid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordshares_department_group ON rmsrecordshares (departmentid, departmentgroupid);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("rmsrecordshares").Exists())
				Delete.Table("rmsrecordshares");

			if (Schema.Table("rmsrecordgroupscopes").Exists())
				Delete.Table("rmsrecordgroupscopes");
		}
	}
}
