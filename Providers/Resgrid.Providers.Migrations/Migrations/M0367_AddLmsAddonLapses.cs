using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// LMS add-on lapse tracking (protocols-training-knowledge-plan §17.3 lapse rules 1, 12 and 13):
	/// <list type="bullet">
	/// <item>LmsAddonStates: what the gate last saw of a department's add-on (packs, paid-through date, renewal off, the open lapse),
	/// one row per department. The Billing API reports only running intervals, so this tells a failed renewal (dunning grace) from
	/// one that ended;</item>
	/// <item>LmsAddonLapses: each lapse period, at most one open per department (a filtered unique index); the reactivation
	/// re-base shifts paused due dates by them.</item>
	/// </list>
	/// No foreign keys; UnlinkedDepartmentCleanup removes the rows. Existence-guarded for safe retry.
	/// </summary>
	[Migration(367)]
	public class M0367_AddLmsAddonLapses : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("LmsAddonStates").Exists())
			{
				Create.Table("LmsAddonStates")
					.WithColumn("LmsAddonStateId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Packs").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PaidThroughUtc").AsDateTime2().Nullable()
					.WithColumn("RenewalCancelled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("LastActiveOn").AsDateTime2().Nullable()
					.WithColumn("LapsedSinceUtc").AsDateTime2().Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().NotNullable();
			}

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsAddonStates_Department' AND object_id = OBJECT_ID('dbo.LmsAddonStates')) " +
				"CREATE UNIQUE INDEX [UX_LmsAddonStates_Department] ON [dbo].[LmsAddonStates] ([DepartmentId]);");

			if (!Schema.Table("LmsAddonLapses").Exists())
			{
				Create.Table("LmsAddonLapses")
					.WithColumn("LmsAddonLapseId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("StartedOn").AsDateTime2().NotNullable()
					.WithColumn("EndedOn").AsDateTime2().Nullable()
					.WithColumn("PaidThroughUtc").AsDateTime2().Nullable()
					.WithColumn("Reason").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Packs").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("DetectedOn").AsDateTime2().NotNullable();
			}

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsAddonLapses_Department_Started' AND object_id = OBJECT_ID('dbo.LmsAddonLapses')) " +
				"CREATE INDEX [IX_LmsAddonLapses_Department_Started] ON [dbo].[LmsAddonLapses] ([DepartmentId], [StartedOn]);");
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsAddonLapses_Open' AND object_id = OBJECT_ID('dbo.LmsAddonLapses')) " +
				"CREATE UNIQUE INDEX [UX_LmsAddonLapses_Open] ON [dbo].[LmsAddonLapses] ([DepartmentId]) WHERE [EndedOn] IS NULL;");
		}

		public override void Down()
		{
			if (Schema.Table("LmsAddonLapses").Exists())
				Delete.Table("LmsAddonLapses");
			if (Schema.Table("LmsAddonStates").Exists())
				Delete.Table("LmsAddonStates");
		}
	}
}
