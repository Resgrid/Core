using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Lane display colors (map marker tinting per lane) and the PTT transmission log for
	/// on-demand incident voice channels (who keyed up, on which channel, start/end).
	/// </summary>
	[Migration(92)]
	public class M0092_AddLaneColorAndVoiceTransmissionLog : Migration
	{
		public override void Up()
		{
			if (Schema.Table("CommandStructureNodes").Exists() && !Schema.Table("CommandStructureNodes").Column("Color").Exists())
			{
				Alter.Table("CommandStructureNodes")
					.AddColumn("Color").AsString(32).Nullable();
			}

			if (Schema.Table("CommandDefinitionRoles").Exists() && !Schema.Table("CommandDefinitionRoles").Column("Color").Exists())
			{
				Alter.Table("CommandDefinitionRoles")
					.AddColumn("Color").AsString(32).Nullable();
			}

			if (Schema.Table("CommandDefinitionRoles").Exists() && !Schema.Table("CommandDefinitionRoles").Column("MinUnits").Exists())
			{
				Alter.Table("CommandDefinitionRoles")
					.AddColumn("MinUnits").AsInt32().NotNullable().WithDefaultValue(0);
			}

			// Per-lane runtime constraints, denormalized from the source template role at seeding so
			// assignment-time enforcement never needs the (mutable) definition.
			if (Schema.Table("CommandStructureNodes").Exists())
			{
				foreach (var column in new[] { "MinUnitPersonnel", "MaxUnitPersonnel", "MinUnits", "MaxUnits", "MinTimeInRole", "MaxTimeInRole" })
				{
					if (!Schema.Table("CommandStructureNodes").Column(column).Exists())
					{
						Alter.Table("CommandStructureNodes")
							.AddColumn(column).AsInt32().NotNullable().WithDefaultValue(0);
					}
				}

				if (!Schema.Table("CommandStructureNodes").Column("ForceRequirements").Exists())
				{
					Alter.Table("CommandStructureNodes")
						.AddColumn("ForceRequirements").AsBoolean().NotNullable().WithDefaultValue(false);
				}
			}

			if (!Schema.Table("VoiceTransmissionLogs").Exists())
			{
				Create.Table("VoiceTransmissionLogs")
					.WithColumn("VoiceTransmissionLogId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("DepartmentVoiceChannelId").AsString(128).Nullable()
					.WithColumn("UserId").AsString(450).Nullable()
					.WithColumn("StartedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("EndedOn").AsDateTime2().Nullable();

				Create.Index("IX_VoiceTransmissionLogs_Department_Call")
					.OnTable("VoiceTransmissionLogs")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("CallId").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("VoiceTransmissionLogs").Exists())
				Delete.Table("VoiceTransmissionLogs");

			if (Schema.Table("CommandStructureNodes").Exists() && Schema.Table("CommandStructureNodes").Column("Color").Exists())
				Delete.Column("Color").FromTable("CommandStructureNodes");

			if (Schema.Table("CommandDefinitionRoles").Exists() && Schema.Table("CommandDefinitionRoles").Column("Color").Exists())
				Delete.Column("Color").FromTable("CommandDefinitionRoles");

			if (Schema.Table("CommandDefinitionRoles").Exists() && Schema.Table("CommandDefinitionRoles").Column("MinUnits").Exists())
				Delete.Column("MinUnits").FromTable("CommandDefinitionRoles");

			if (Schema.Table("CommandStructureNodes").Exists())
			{
				foreach (var column in new[] { "MinUnitPersonnel", "MaxUnitPersonnel", "MinUnits", "MaxUnits", "MinTimeInRole", "MaxTimeInRole", "ForceRequirements" })
				{
					if (Schema.Table("CommandStructureNodes").Column(column).Exists())
						Delete.Column(column).FromTable("CommandStructureNodes");
				}
			}
		}
	}
}
