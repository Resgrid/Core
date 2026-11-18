using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Releasing a unit or a member from a call that stays open (Belgian EMS: the ambulance arrives first and stands the MUG
	/// down, which goes back to radio availability for the next call):
	///  * CallDispatchUnits.ReleasedOn/ReleasedByUserId mark the unit's dispatch as no longer live while keeping it on the call's
	///    record, so the Call Unit Times report still has the dispatch time and the stand-down. Null on every existing row.
	///  * CallPersonnelReleases: one row per call and member released from it. A member can be on a call with no dispatch row of
	///    their own (a dispatched group or role, or a status set against the call), so the release is a row of its own. Unique
	///    per call and member; DepartmentId for the department purge (UnlinkedDepartmentCleanup); no foreign keys.
	/// The user columns are provenance and must survive the member leaving. Existence-guarded for safe retry.
	/// </summary>
	[Migration(316)]
	public class M0316_AddCallResourceReleases : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('CallDispatchUnits', 'ReleasedOn') IS NULL ALTER TABLE [CallDispatchUnits] ADD [ReleasedOn] datetime2 NULL;");
			Execute.Sql("IF COL_LENGTH('CallDispatchUnits', 'ReleasedByUserId') IS NULL ALTER TABLE [CallDispatchUnits] ADD [ReleasedByUserId] nvarchar(128) NULL;");

			if (!Schema.Table("CallPersonnelReleases").Exists())
			{
				Create.Table("CallPersonnelReleases")
					.WithColumn("CallPersonnelReleaseId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("ReleasedOn").AsDateTime2().NotNullable()
					.WithColumn("ReleasedByUserId").AsString(128).Nullable();

				Create.Index("IX_CallPersonnelReleases_Department").OnTable("CallPersonnelReleases").OnColumn("DepartmentId").Ascending();
				Create.Index("UX_CallPersonnelReleases_Call_User").OnTable("CallPersonnelReleases")
					.OnColumn("CallId").Ascending().OnColumn("UserId").Ascending().WithOptions().Unique();
			}
		}

		public override void Down()
		{
			if (Schema.Table("CallPersonnelReleases").Exists())
				Delete.Table("CallPersonnelReleases");

			Execute.Sql("IF COL_LENGTH('CallDispatchUnits', 'ReleasedByUserId') IS NOT NULL ALTER TABLE [CallDispatchUnits] DROP COLUMN [ReleasedByUserId];");
			Execute.Sql("IF COL_LENGTH('CallDispatchUnits', 'ReleasedOn') IS NOT NULL ALTER TABLE [CallDispatchUnits] DROP COLUMN [ReleasedOn];");
		}
	}
}
