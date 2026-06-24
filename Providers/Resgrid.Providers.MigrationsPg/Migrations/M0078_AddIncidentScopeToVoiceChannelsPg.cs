using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(78)]
	public class M0078_AddIncidentScopeToVoiceChannelsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("departmentvoicechannels").Column("callid").Exists())
			{
				Alter.Table("departmentvoicechannels")
					.AddColumn("callid").AsInt32().Nullable();
			}

			if (!Schema.Table("departmentvoicechannels").Column("isondemand").Exists())
			{
				Alter.Table("departmentvoicechannels")
					.AddColumn("isondemand").AsBoolean().NotNullable().WithDefaultValue(false);
			}

			if (!Schema.Table("departmentvoicechannels").Column("closedon").Exists())
			{
				Alter.Table("departmentvoicechannels")
					.AddColumn("closedon").AsDateTime2().Nullable();
			}
		}

		public override void Down()
		{
			Delete.Column("callid").FromTable("departmentvoicechannels");
			Delete.Column("isondemand").FromTable("departmentvoicechannels");
			Delete.Column("closedon").FromTable("departmentvoicechannels");
		}
	}
}
