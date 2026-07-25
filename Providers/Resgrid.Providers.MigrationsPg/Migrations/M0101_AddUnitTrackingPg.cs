using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(101)]
	public class M0101_AddUnitTrackingPg : Migration
	{
		private const string DevicesTable = "unittrackingdevices";
		private const string CredentialsTable = "unittrackingcredentials";

		public override void Up()
		{
			if (!Schema.Table(DevicesTable).Exists())
			{
				Create.Table(DevicesTable)
					.WithColumn("unittrackingdeviceid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("unitid").AsInt32().NotNullable()
					.WithColumn("displayname").AsCustom("citext").Nullable()
					.WithColumn("manufacturerkey").AsCustom("citext").Nullable()
					.WithColumn("modelkey").AsCustom("citext").Nullable()
					.WithColumn("transporttype").AsInt32().NotNullable()
					.WithColumn("protocolkey").AsCustom("citext").Nullable()
					.WithColumn("payloadadapterkey").AsCustom("citext").Nullable()
					.WithColumn("deviceidentifier").AsCustom("citext").Nullable()
					.WithColumn("secondaryidentifier").AsCustom("citext").Nullable()
					.WithColumn("isenabled").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("sourcepriority").AsInt32().NotNullable().WithDefaultValue(100)
					.WithColumn("allowedsourcecidrs").AsCustom("text").Nullable()
					.WithColumn("lastseenon").AsDateTime2().Nullable()
					.WithColumn("lastpositionon").AsDateTime2().Nullable()
					.WithColumn("lastreceivedon").AsDateTime2().Nullable()
					.WithColumn("laststatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("lasterrorcode").AsCustom("citext").Nullable()
					.WithColumn("firmwareversion").AsCustom("citext").Nullable()
					.WithColumn("createdbyuserid").AsCustom("citext").NotNullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("updatedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("updatedon").AsDateTime2().Nullable();

			if (!Schema.Table("units").Constraint("uq_units_departmentid_unitid").Exists())
			{
				Create.UniqueConstraint("uq_units_departmentid_unitid")
					.OnTable("units")
					.Columns("departmentid", "unitid");
			}

			Create.ForeignKey("fk_unittrackingdevices_units_department_unit")
				.FromTable(DevicesTable).ForeignColumns("departmentid", "unitid")
				.ToTable("units").PrimaryColumns("departmentid", "unitid");
			}

			if (!Schema.Table(DevicesTable).Index("ix_unittrackingdevices_department_unit_deleted").Exists())
			{
				Create.Index("ix_unittrackingdevices_department_unit_deleted")
					.OnTable(DevicesTable)
					.OnColumn("departmentid").Ascending()
					.OnColumn("unitid").Ascending()
					.OnColumn("isdeleted").Ascending();
			}

			if (!Schema.Table(DevicesTable).Index("ix_unittrackingdevices_department_enabled_deleted").Exists())
			{
				Create.Index("ix_unittrackingdevices_department_enabled_deleted")
					.OnTable(DevicesTable)
					.OnColumn("departmentid").Ascending()
					.OnColumn("isenabled").Ascending()
					.OnColumn("isdeleted").Ascending();
			}

			if (!Schema.Table(DevicesTable).Index("ix_unittrackingdevices_lastseenon").Exists())
			{
				Create.Index("ix_unittrackingdevices_lastseenon")
					.OnTable(DevicesTable)
					.OnColumn("lastseenon").Ascending();
			}

			Execute.Sql(@"
				CREATE UNIQUE INDEX IF NOT EXISTS ux_unittrackingdevices_protocol_deviceidentifier
				ON unittrackingdevices (COALESCE(protocolkey, ''), deviceidentifier)
				WHERE deviceidentifier IS NOT NULL AND isdeleted = false;");

			if (!Schema.Table(CredentialsTable).Exists())
			{
				Create.Table(CredentialsTable)
					.WithColumn("unittrackingcredentialid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("unittrackingdeviceid").AsCustom("citext").NotNullable()
					.WithColumn("authmode").AsInt32().NotNullable()
					.WithColumn("headername").AsCustom("citext").Nullable()
					.WithColumn("basicusername").AsCustom("citext").Nullable()
					.WithColumn("keyprefix").AsCustom("citext").NotNullable()
					.WithColumn("secrethash").AsString(64).NotNullable()
					.WithColumn("validfrom").AsDateTime2().NotNullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("revokedon").AsDateTime2().Nullable()
					.WithColumn("lastusedon").AsDateTime2().Nullable()
					.WithColumn("createdbyuserid").AsCustom("citext").NotNullable()
					.WithColumn("createdon").AsDateTime2().NotNullable();

				Create.ForeignKey("fk_unittrackingcredentials_devices")
					.FromTable(CredentialsTable).ForeignColumn("unittrackingdeviceid")
					.ToTable(DevicesTable).PrimaryColumn("unittrackingdeviceid");
			}

			if (!Schema.Table(CredentialsTable).Index("ux_unittrackingcredentials_secrethash").Exists())
			{
				Create.Index("ux_unittrackingcredentials_secrethash")
					.OnTable(CredentialsTable)
					.OnColumn("secrethash").Ascending()
					.WithOptions().Unique();
			}

			if (!Schema.Table(CredentialsTable).Index("ix_unittrackingcredentials_device_revoked_expires").Exists())
			{
				Create.Index("ix_unittrackingcredentials_device_revoked_expires")
					.OnTable(CredentialsTable)
					.OnColumn("unittrackingdeviceid").Ascending()
					.OnColumn("revokedon").Ascending()
					.OnColumn("expireson").Ascending();
			}

			if (!Schema.Table(CredentialsTable).Index("ix_unittrackingcredentials_keyprefix").Exists())
			{
				Create.Index("ix_unittrackingcredentials_keyprefix")
					.OnTable(CredentialsTable)
					.OnColumn("keyprefix").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table(CredentialsTable).Exists())
				Delete.Table(CredentialsTable);

			if (Schema.Table(DevicesTable).Exists())
				Delete.Table(DevicesTable);

			if (Schema.Table("units").Constraint("uq_units_departmentid_unitid").Exists())
				Delete.UniqueConstraint("uq_units_departmentid_unitid").FromTable("units");
		}
	}
}
