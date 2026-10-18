using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Department Import configuration (wizard plan sections 6.3 step 9 and 7.5): the administrator's decided values for
	/// the Phase 2 configuration domains (department record, profile and logo, module switches, terminology, map,
	/// numbering, permissions), one row per item and workspace. Suggestions are computed and never stored. String GUID
	/// keys and DepartmentId on every row. No foreign key to Departments: the department purge
	/// (DepartmentImportDepartmentCleanup) deletes the rows explicitly.
	/// </summary>
	[Migration(283)]
	public class M0283_AddImportConfigurationChanges : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ImportConfigurationChanges").Exists())
			{
				Create.Table("ImportConfigurationChanges")
					.WithColumn("Id").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("WorkspaceId").AsString(36).NotNullable()
					.WithColumn("ItemKey").AsString(128).NotNullable()
					.WithColumn("Origin").AsInt32().NotNullable()
					.WithColumn("OriginKey").AsString(128).Nullable()
					.WithColumn("Value").AsString(int.MaxValue).Nullable()
					.WithColumn("State").AsInt32().NotNullable()
					.WithColumn("ApprovedRunId").AsString(36).Nullable()
					.WithColumn("AppliedRunId").AsString(36).Nullable()
					.WithColumn("FailureCode").AsString(64).Nullable()
					.WithColumn("StaffAuthored").AsBoolean().NotNullable()
					.WithColumn("Revision").AsInt32().NotNullable()
					.WithColumn("CreatedBy").AsString(256).Nullable()
					.WithColumn("UpdatedBy").AsString(256).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("UpdatedOn").AsDateTime2().NotNullable()
					.WithColumn("AppliedOn").AsDateTime2().Nullable();
			}

			if (!Schema.Table("ImportConfigurationChanges").Index("UX_ImportConfigurationChanges_WorkspaceId_ItemKey").Exists())
			{
				Create.Index("UX_ImportConfigurationChanges_WorkspaceId_ItemKey").OnTable("ImportConfigurationChanges")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("WorkspaceId").Ascending()
					.OnColumn("ItemKey").Ascending()
					.WithOptions().Unique();
			}

			if (!Schema.Table("ImportConfigurationChanges").Index("IX_ImportConfigurationChanges_ApprovedRunId").Exists())
			{
				Create.Index("IX_ImportConfigurationChanges_ApprovedRunId").OnTable("ImportConfigurationChanges")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("ApprovedRunId").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("ImportConfigurationChanges").Exists())
				Delete.Table("ImportConfigurationChanges");
		}
	}
}
