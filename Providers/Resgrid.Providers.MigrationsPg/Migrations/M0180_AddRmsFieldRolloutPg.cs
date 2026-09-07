using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Field Records media policy and rollout telemetry (RMS plan RMS-1D, registry M0180): the per-attachment
	/// record of whether photo coordinates survived upload, and the bounded per-app rollout event stream the
	/// adoption dashboards read. Events carry identifiers, counts and outcome codes only — never record content.
	/// Guarded for safe retry.
	/// </summary>
	[Migration(180)]
	public class M0180_AddRmsFieldRolloutPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("rmsrecordattachments").Exists() && !Schema.Table("rmsrecordattachments").Column("medialocationretained").Exists())
				Alter.Table("rmsrecordattachments").AddColumn("medialocationretained").AsBoolean().NotNullable().WithDefaultValue(false);

			if (!Schema.Table("rmsfieldrolloutevents").Exists())
			{
				Create.Table("rmsfieldrolloutevents")
					.WithColumn("rmsfieldrollouteventid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("originclient").AsInt32().NotNullable()
					.WithColumn("appversion").AsString(32).Nullable()
					.WithColumn("clientcapability").AsString(32).Nullable()
					.WithColumn("eventtype").AsString(32).NotNullable()
					.WithColumn("outcome").AsString(48).NotNullable()
					.WithColumn("definitionkey").AsString(64).Nullable()
					.WithColumn("definitionversion").AsInt32().Nullable()
					.WithColumn("recordid").AsString(36).Nullable()
					.WithColumn("userid").AsString(128).Nullable()
					.WithColumn("durationms").AsInt64().Nullable()
					.WithColumn("itemcount").AsInt32().Nullable()
					.WithColumn("occurredon").AsDateTime2().NotNullable()
					.WithColumn("recordedon").AsDateTime2().NotNullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsfieldrolloutevents_window ON rmsfieldrolloutevents (departmentid, occurredon);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsfieldrolloutevents_app ON rmsfieldrolloutevents (departmentid, originclient, eventtype);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("rmsfieldrolloutevents").Exists())
				Delete.Table("rmsfieldrolloutevents");
			if (Schema.Table("rmsrecordattachments").Exists() && Schema.Table("rmsrecordattachments").Column("medialocationretained").Exists())
				Delete.Column("medialocationretained").FromTable("rmsrecordattachments");
		}
	}
}
