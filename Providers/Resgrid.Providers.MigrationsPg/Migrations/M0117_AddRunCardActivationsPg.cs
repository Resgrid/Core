using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// RunCardActivations: audit trail of run card activations against calls (card,
	/// alarm level, mode, auto-dispatch flag and the serialized recommendation result
	/// with per-pick reasons, shortfalls and move-up recommendations).
	/// </summary>
	[Migration(117)]
	public class M0117_AddRunCardActivationsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RunCardActivations".ToLower()).Exists())
			{
				Create.Table("RunCardActivations".ToLower())
					.WithColumn("RunCardActivationId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("CallId".ToLower()).AsInt32().NotNullable()
					.WithColumn("RunCardId".ToLower()).AsInt32().NotNullable()
					.WithColumn("AlarmLevel".ToLower()).AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("ModeUsed".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("WasAutoDispatched".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ResultJson".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("CreatedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("CreatedByUserId".ToLower()).AsCustom("citext").Nullable();

				Create.Index("IX_RunCardActivations_CallId".ToLower())
					.OnTable("RunCardActivations".ToLower())
					.OnColumn("CallId".ToLower()).Ascending();

				Create.Index("IX_RunCardActivations_DepartmentId".ToLower())
					.OnTable("RunCardActivations".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("RunCardActivations".ToLower()).Exists())
				Delete.Table("RunCardActivations".ToLower());
		}
	}
}
