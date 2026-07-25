using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	// TransactionBehavior.None is required for CREATE UNIQUE INDEX CONCURRENTLY on the live units
	// table (it cannot run inside a transaction). Statements self-commit, so all creates below are
	// guarded with existence checks to stay safe on a re-run after a partial apply.
	[Migration(101, TransactionBehavior.None)]
	public class M0101_AddUnitTrackingPg : Migration
	{
		private const string DevicesTable = "unittrackingdevices";
		private const string CredentialsTable = "unittrackingcredentials";

		public override void Up()
		{
			// The units unique constraint is independent of the devices table, so it runs
			// unconditionally (each statement is self-guarded). With TransactionBehavior.None a
			// re-run after a partial apply (e.g. the duplicate check failed after the devices
			// table self-committed) would otherwise skip it because the devices table exists.
			// ALTER TABLE ADD CONSTRAINT UNIQUE takes a SHARE ROW EXCLUSIVE lock on the live units
			// table and fails if duplicates exist. Pre-validate duplicates, build the index online
			// with CONCURRENTLY, then attach it as a constraint (brief lock) so it can still serve
			// as the foreign key target below.
			Execute.Sql(@"
				DO $$
				BEGIN
					IF EXISTS (SELECT 1 FROM units GROUP BY departmentid, unitid HAVING COUNT(*) > 1) THEN
						RAISE EXCEPTION 'uq_units_departmentid_unitid: duplicate (departmentid, unitid) rows exist in units; remove them before rerunning this migration';
					END IF;
				END $$;");

			// Drop a leftover INVALID index from a previously-failed CREATE INDEX CONCURRENTLY
			// build before the existence check below. The check only tests the index name, so an
			// invalid index would skip the create and then fail the ADD CONSTRAINT ... USING INDEX
			// attach. Only invalid indexes are dropped: a valid one may already back the constraint
			// (which cannot be dropped independently) on a re-run after a later step failed.
			Execute.Sql(@"
				DO $$
				BEGIN
					IF EXISTS (
						SELECT 1
						FROM pg_class c
						JOIN pg_index i ON i.indexrelid = c.oid
						JOIN pg_namespace n ON n.oid = c.relnamespace
						WHERE c.relname = 'uq_units_departmentid_unitid'
						AND n.nspname = current_schema()
						AND NOT i.indisvalid
					) THEN
						DROP INDEX uq_units_departmentid_unitid;
					END IF;
				END $$;");

			if (!Schema.Table("units").Index("uq_units_departmentid_unitid").Exists())
			{
				Execute.Sql(@"
					CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS uq_units_departmentid_unitid
					ON units (departmentid, unitid);");
			}

			if (!Schema.Table("units").Constraint("uq_units_departmentid_unitid").Exists())
			{
				Execute.Sql(@"
					ALTER TABLE units
					ADD CONSTRAINT uq_units_departmentid_unitid UNIQUE USING INDEX uq_units_departmentid_unitid;");
			}

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
