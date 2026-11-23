using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>CAD email call updates; see the SQL Server M0330_AddCallEmailUpdates.</summary>
	[Migration(330)]
	public class M0330_AddCallEmailUpdatesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("callemailupdates").Exists())
			{
				Create.Table("callemailupdates")
					.WithColumn("callemailupdateid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("cadcallid").AsCustom("citext").NotNullable()
					.WithColumn("source").AsInt32().NotNullable()
					.WithColumn("intakechannelid").AsCustom("citext").Nullable()
					.WithColumn("messageid").AsCustom("citext").Nullable()
					.WithColumn("senton").AsDateTime2().Nullable()
					.WithColumn("receivedon").AsDateTime2().NotNullable()
					.WithColumn("outcome").AsInt32().NotNullable()
					.WithColumn("changedfields").AsCustom("citext").Nullable()
					.WithColumn("fieldhashes").AsCustom("citext").Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_callemailupdates_department_call ON callemailupdates (departmentid, callid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_callemailupdates_department_cadcallid ON callemailupdates (departmentid, cadcallid);");
			}

			if (Schema.Table("intakechannels").Exists() && !Schema.Table("intakechannels").Column("cadupdates").Exists())
			{
				Alter.Table("intakechannels")
					.AddColumn("cadupdates").AsBoolean().NotNullable().WithDefaultValue(false);
			}
		}

		public override void Down()
		{
			if (Schema.Table("intakechannels").Exists() && Schema.Table("intakechannels").Column("cadupdates").Exists())
				Delete.Column("cadupdates").FromTable("intakechannels");

			if (Schema.Table("callemailupdates").Exists())
				Delete.Table("callemailupdates");
		}
	}
}
