using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// RunCardActivations: audit trail of run card activations against calls (card,
	/// alarm level, mode, auto-dispatch flag and the serialized recommendation result
	/// with per-pick reasons, shortfalls and move-up recommendations).
	/// </summary>
	[Migration(117)]
	public class M0117_AddRunCardActivations : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RunCardActivations").Exists())
			{
				Create.Table("RunCardActivations")
					.WithColumn("RunCardActivationId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("RunCardId").AsInt32().NotNullable()
					.WithColumn("AlarmLevel").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("ModeUsed").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("WasAutoDispatched").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ResultJson").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("CreatedByUserId").AsString(450).Nullable();

				Create.Index("IX_RunCardActivations_CallId")
					.OnTable("RunCardActivations")
					.OnColumn("CallId").Ascending();

				Create.Index("IX_RunCardActivations_DepartmentId")
					.OnTable("RunCardActivations")
					.OnColumn("DepartmentId").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("RunCardActivations").Exists())
				Delete.Table("RunCardActivations");
		}
	}
}
