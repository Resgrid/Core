using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Lane display colors (map marker tinting per lane) and the PTT transmission log for
	/// on-demand incident voice channels (who keyed up, on which channel, start/end).
	/// </summary>
	[Migration(92)]
	public class M0092_AddLaneColorAndVoiceTransmissionLogPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("commandstructurenodes").Exists() && !Schema.Table("commandstructurenodes").Column("color").Exists())
			{
				Alter.Table("commandstructurenodes")
					.AddColumn("color").AsString(32).Nullable();
			}

			if (Schema.Table("commanddefinitionroles").Exists() && !Schema.Table("commanddefinitionroles").Column("color").Exists())
			{
				Alter.Table("commanddefinitionroles")
					.AddColumn("color").AsString(32).Nullable();
			}

			if (Schema.Table("commanddefinitionroles").Exists() && !Schema.Table("commanddefinitionroles").Column("minunits").Exists())
			{
				Alter.Table("commanddefinitionroles")
					.AddColumn("minunits").AsInt32().NotNullable().WithDefaultValue(0);
			}

			// Per-lane runtime constraints, denormalized from the source template role at seeding so
			// assignment-time enforcement never needs the (mutable) definition.
			if (Schema.Table("commandstructurenodes").Exists())
			{
				foreach (var column in new[] { "minunitpersonnel", "maxunitpersonnel", "minunits", "maxunits", "mintimeinrole", "maxtimeinrole" })
				{
					if (!Schema.Table("commandstructurenodes").Column(column).Exists())
					{
						Alter.Table("commandstructurenodes")
							.AddColumn(column).AsInt32().NotNullable().WithDefaultValue(0);
					}
				}

				if (!Schema.Table("commandstructurenodes").Column("forcerequirements").Exists())
				{
					Alter.Table("commandstructurenodes")
						.AddColumn("forcerequirements").AsBoolean().NotNullable().WithDefaultValue(false);
				}
			}

			if (!Schema.Table("VoiceTransmissionLogs".ToLower()).Exists())
			{
				Create.Table("VoiceTransmissionLogs".ToLower())
					.WithColumn("VoiceTransmissionLogId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("CallId".ToLower()).AsInt32().NotNullable()
					.WithColumn("DepartmentVoiceChannelId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("UserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("StartedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("EndedOn".ToLower()).AsDateTime2().Nullable();

				Create.Index("IX_VoiceTransmissionLogs_Department_Call".ToLower())
					.OnTable("VoiceTransmissionLogs".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("CallId".ToLower()).Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("VoiceTransmissionLogs".ToLower()).Exists())
				Delete.Table("VoiceTransmissionLogs".ToLower());

			if (Schema.Table("commandstructurenodes").Exists() && Schema.Table("commandstructurenodes").Column("color").Exists())
				Delete.Column("color").FromTable("commandstructurenodes");

			if (Schema.Table("commanddefinitionroles").Exists() && Schema.Table("commanddefinitionroles").Column("color").Exists())
				Delete.Column("color").FromTable("commanddefinitionroles");

			if (Schema.Table("commanddefinitionroles").Exists() && Schema.Table("commanddefinitionroles").Column("minunits").Exists())
				Delete.Column("minunits").FromTable("commanddefinitionroles");

			if (Schema.Table("commandstructurenodes").Exists())
			{
				foreach (var column in new[] { "minunitpersonnel", "maxunitpersonnel", "minunits", "maxunits", "mintimeinrole", "maxtimeinrole", "forcerequirements" })
				{
					if (Schema.Table("commandstructurenodes").Column(column).Exists())
						Delete.Column(column).FromTable("commandstructurenodes");
				}
			}
		}
	}
}
