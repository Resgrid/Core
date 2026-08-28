using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Advanced Data Protection (ADP) Phase 1 schemas: durable per-department protection policy
	/// (departmentdataprotectionpolicies.state is the single data-safety truth), wrapped department
	/// key versions (never plaintext key material), resumable bulk-migration cursors, independent
	/// per-channel egress policy, and department-owned member sensitive data moved off the global
	/// userprofiles row. All tables ship inert while every department is Disabled.
	/// CREATE INDEX CONCURRENTLY cannot run inside a transaction; every statement is
	/// existence-guarded and invalid indexes from interrupted builds are removed before retry.
	/// </summary>
	[Migration(124, TransactionBehavior.None)]
	public class M0124_AddDepartmentDataProtectionPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("departmentdataprotectionpolicies").Exists())
				Create.Table("departmentdataprotectionpolicies")
					.WithColumn("departmentdataprotectionpolicyid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("catalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("activemigrationkind").AsInt32().Nullable()
					.WithColumn("stepupwindowminutes").AsInt32().NotNullable().WithDefaultValue(15)
					.WithColumn("stepupwindowreason").AsCustom("citext").Nullable()
					.WithColumn("policyepoch").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("minimumclientversionsjson").AsCustom("citext").Nullable()
					.WithColumn("acknowledgementsjson").AsCustom("citext").Nullable()
					.WithColumn("acknowledgedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("acknowledgedon").AsDateTime2().Nullable()
					.WithColumn("enrollmentflagevaluationjson").AsCustom("citext").Nullable()
					.WithColumn("addonbillingreference").AsCustom("citext").Nullable()
					.WithColumn("migrationwindowstartlocal").AsCustom("citext").Nullable()
					.WithColumn("migrationwindowendlocal").AsCustom("citext").Nullable()
					.WithColumn("migrationwindowtimezone").AsCustom("citext").Nullable()
					.WithColumn("offboardingeffectiveon").AsDateTime2().Nullable()
					.WithColumn("offboardingsource").AsInt32().Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("updatedon").AsDateTime2().Nullable()
					.WithColumn("updatedbyuserid").AsCustom("citext").Nullable();

			if (!Schema.Table("departmentdataprotectionkeys").Exists())
				Create.Table("departmentdataprotectionkeys")
					.WithColumn("departmentdataprotectionkeyid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("version").AsInt32().NotNullable()
					.WithColumn("wrappedkey").AsCustom("citext").NotNullable()
					.WithColumn("providertype").AsCustom("citext").NotNullable()
					.WithColumn("providerkeyreference").AsCustom("citext").NotNullable()
					.WithColumn("providerkeyversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("activatedon").AsDateTime2().Nullable()
					.WithColumn("retiredon").AsDateTime2().Nullable();

			if (!Schema.Table("departmentdataprotectionmigrations").Exists())
				Create.Table("departmentdataprotectionmigrations")
					.WithColumn("departmentdataprotectionmigrationid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("kind").AsInt32().NotNullable()
					.WithColumn("catalogversion").AsInt32().NotNullable()
					.WithColumn("targetkeyversion").AsInt32().Nullable()
					.WithColumn("targettable").AsCustom("citext").NotNullable()
					.WithColumn("cursor").AsCustom("citext").Nullable()
					.WithColumn("rowstotal").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("rowsprocessed").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("rowsalreadyprotected").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("rowsanomalous").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("verificationstate").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("lasterrorcode").AsCustom("citext").Nullable()
					.WithColumn("correlationid").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("startedon").AsDateTime2().Nullable()
					.WithColumn("checkpointedon").AsDateTime2().Nullable()
					.WithColumn("completedon").AsDateTime2().Nullable();

			if (!Schema.Table("departmentprotecteddataegresspolicies").Exists())
				Create.Table("departmentprotecteddataegresspolicies")
					.WithColumn("departmentprotecteddataegresspolicyid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("pushmode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("emailmode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("smsmode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("voicemode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("pinchallengeexpiryminutes").AsInt32().NotNullable().WithDefaultValue(5)
					.WithColumn("pinmaxattempts").AsInt32().NotNullable().WithDefaultValue(3)
					.WithColumn("pinlockoutminutes").AsInt32().NotNullable().WithDefaultValue(15)
					.WithColumn("acknowledgementversion").AsCustom("citext").Nullable()
					.WithColumn("acknowledgedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("acknowledgedon").AsDateTime2().Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("updatedon").AsDateTime2().Nullable()
					.WithColumn("updatedbyuserid").AsCustom("citext").Nullable();

			if (!Schema.Table("departmentmembersensitivedata").Exists())
				Create.Table("departmentmembersensitivedata")
					.WithColumn("departmentmembersensitivedataid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("identificationnumber").AsCustom("citext").Nullable()
					.WithColumn("emergencycontactname").AsCustom("citext").Nullable()
					.WithColumn("emergencycontactphone").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("updatedon").AsDateTime2().Nullable();

			RemoveInvalidIndexes();

			// Raw IF NOT EXISTS statements so they execute after invalid-index cleanup; FluentMigrator
			// evaluates Schema.Index.Exists while collecting expressions, before the cleanup SQL runs.
			Execute.Sql("CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_departmentdataprotectionpolicies_departmentid ON departmentdataprotectionpolicies (departmentid);");
			Execute.Sql("CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_departmentdataprotectionkeys_department_version ON departmentdataprotectionkeys (departmentid, version);");
			Execute.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_departmentdataprotectionkeys_department_status ON departmentdataprotectionkeys (departmentid, status);");
			Execute.Sql("CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_departmentdataprotectionmigrations_active ON departmentdataprotectionmigrations (departmentid, kind, targettable) WHERE completedon IS NULL;");
			Execute.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_departmentdataprotectionmigrations_department_kind ON departmentdataprotectionmigrations (departmentid, kind, completedon);");
			Execute.Sql("CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_departmentprotecteddataegresspolicies_departmentid ON departmentprotecteddataegresspolicies (departmentid);");
			Execute.Sql("CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_departmentmembersensitivedata_department_user ON departmentmembersensitivedata (departmentid, userid);");
			Execute.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_departmentmembersensitivedata_userid ON departmentmembersensitivedata (userid);");
		}

		public override void Down()
		{
			// Down drops inert Phase 1 schema. NEVER run this against a department whose durable state
			// has left Disabled — departmentdataprotectionkeys rows are the only path to that
			// department's ciphertext.
			if (Schema.Table("departmentmembersensitivedata").Exists())
				Delete.Table("departmentmembersensitivedata");
			if (Schema.Table("departmentprotecteddataegresspolicies").Exists())
				Delete.Table("departmentprotecteddataegresspolicies");
			if (Schema.Table("departmentdataprotectionmigrations").Exists())
				Delete.Table("departmentdataprotectionmigrations");
			if (Schema.Table("departmentdataprotectionkeys").Exists())
				Delete.Table("departmentdataprotectionkeys");
			if (Schema.Table("departmentdataprotectionpolicies").Exists())
				Delete.Table("departmentdataprotectionpolicies");
		}

		private void RemoveInvalidIndexes()
		{
			Execute.Sql(@"
				DO $$
				DECLARE invalid_index record;
				BEGIN
					FOR invalid_index IN
						SELECT n.nspname AS schema_name, c.relname AS index_name
						FROM pg_class c
						JOIN pg_index i ON i.indexrelid = c.oid
						JOIN pg_namespace n ON n.oid = c.relnamespace
						WHERE n.nspname = current_schema()
						AND c.relname IN (
							'ux_departmentdataprotectionpolicies_departmentid',
							'ux_departmentdataprotectionkeys_department_version',
							'ix_departmentdataprotectionkeys_department_status',
							'ux_departmentdataprotectionmigrations_active',
							'ix_departmentdataprotectionmigrations_department_kind',
							'ux_departmentprotecteddataegresspolicies_departmentid',
							'ux_departmentmembersensitivedata_department_user',
							'ix_departmentmembersensitivedata_userid')
						AND NOT i.indisvalid
					LOOP
						EXECUTE format('DROP INDEX %I.%I', invalid_index.schema_name, invalid_index.index_name);
					END LOOP;
				END $$;");
		}
	}
}
