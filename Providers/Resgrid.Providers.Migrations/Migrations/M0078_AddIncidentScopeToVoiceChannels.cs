using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(78)]
	public class M0078_AddIncidentScopeToVoiceChannels : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("DepartmentVoiceChannels").Column("CallId").Exists())
			{
				Alter.Table("DepartmentVoiceChannels")
					.AddColumn("CallId").AsInt32().Nullable();
			}

			if (!Schema.Table("DepartmentVoiceChannels").Column("IsOnDemand").Exists())
			{
				Alter.Table("DepartmentVoiceChannels")
					.AddColumn("IsOnDemand").AsBoolean().NotNullable().WithDefaultValue(false);
			}

			if (!Schema.Table("DepartmentVoiceChannels").Column("ClosedOn").Exists())
			{
				Alter.Table("DepartmentVoiceChannels")
					.AddColumn("ClosedOn").AsDateTime2().Nullable();
			}
		}

		public override void Down()
		{
			Delete.Column("CallId").FromTable("DepartmentVoiceChannels");
			Delete.Column("IsOnDemand").FromTable("DepartmentVoiceChannels");
			Delete.Column("ClosedOn").FromTable("DepartmentVoiceChannels");
		}
	}
}
