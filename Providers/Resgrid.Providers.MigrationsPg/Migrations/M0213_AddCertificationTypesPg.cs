using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0213 (Workforce &amp; Business Operations plan, Phase D): typed certification catalog
	/// columns with the Code backfill and filtered unique index, role certification requirements, department
	/// certification settings, unit certifications, and the typed-record columns on personnelcertifications with the
	/// exact-text type backfill. Same number, lower-case identifiers, guarded for safe retry.
	/// </summary>
	[Migration(213)]
	public class M0213_AddCertificationTypesPg : Migration
	{
		public override void Up()
		{
			var types = "departmentcertificationtypes";
			if (!Schema.Table(types).Column("code").Exists())
				Alter.Table(types).AddColumn("code").AsString(50).Nullable();
			if (!Schema.Table(types).Column("category").Exists())
				Alter.Table(types).AddColumn("category").AsInt32().NotNullable().WithDefaultValue(10);
			if (!Schema.Table(types).Column("appliesto").Exists())
				Alter.Table(types).AddColumn("appliesto").AsInt32().NotNullable().WithDefaultValue(0);
			if (!Schema.Table(types).Column("description").Exists())
				Alter.Table(types).AddColumn("description").AsCustom("text").Nullable();
			if (!Schema.Table(types).Column("issuingauthority").Exists())
				Alter.Table(types).AddColumn("issuingauthority").AsString(200).Nullable();
			if (!Schema.Table(types).Column("defaultvaliditymonths").Exists())
				Alter.Table(types).AddColumn("defaultvaliditymonths").AsInt32().Nullable();
			if (!Schema.Table(types).Column("neverexpires").Exists())
				Alter.Table(types).AddColumn("neverexpires").AsBoolean().NotNullable().WithDefaultValue(false);
			if (!Schema.Table(types).Column("renewalcredithoursrequired").Exists())
				Alter.Table(types).AddColumn("renewalcredithoursrequired").AsDecimal(9, 2).Nullable();
			if (!Schema.Table(types).Column("requiresverification").Exists())
				Alter.Table(types).AddColumn("requiresverification").AsBoolean().NotNullable().WithDefaultValue(false);
			if (!Schema.Table(types).Column("isactive").Exists())
				Alter.Table(types).AddColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true);
			if (!Schema.Table(types).Column("isdeleted").Exists())
				Alter.Table(types).AddColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false);
			if (!Schema.Table(types).Column("addedon").Exists())
				Alter.Table(types).AddColumn("addedon").AsDateTime2().Nullable();
			if (!Schema.Table(types).Column("addedbyuserid").Exists())
				Alter.Table(types).AddColumn("addedbyuserid").AsString(128).Nullable();
			if (!Schema.Table(types).Column("editedon").Exists())
				Alter.Table(types).AddColumn("editedon").AsDateTime2().Nullable();
			if (!Schema.Table(types).Column("editedbyuserid").Exists())
				Alter.Table(types).AddColumn("editedbyuserid").AsString(128).Nullable();

			Execute.Sql("UPDATE departmentcertificationtypes SET code = LEFT(BTRIM(type), 50) WHERE code IS NULL OR BTRIM(code) = '';");
			Execute.Sql("UPDATE departmentcertificationtypes SET code = 'TYPE-' || departmentcertificationtypeid::text WHERE code IS NULL OR BTRIM(code) = '';");
			Execute.Sql(
				"UPDATE departmentcertificationtypes t SET code = LEFT(d.code, 38) || '-' || t.departmentcertificationtypeid::text " +
				"FROM (SELECT departmentcertificationtypeid, code, ROW_NUMBER() OVER (PARTITION BY departmentid, code ORDER BY departmentcertificationtypeid) AS rn FROM departmentcertificationtypes) d " +
				"WHERE d.departmentcertificationtypeid = t.departmentcertificationtypeid AND d.rn > 1;");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_departmentcertificationtypes_code ON departmentcertificationtypes (departmentid, code) WHERE isdeleted = FALSE;");

			if (!Schema.Table("personnelrolecertificationrequirements").Exists())
			{
				Create.Table("personnelrolecertificationrequirements")
					.WithColumn("personnelrolecertificationrequirementid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("personnelroleid").AsInt32().NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("departmentcertificationtypeid").AsInt32().NotNullable()
					.WithColumn("ismandatory").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("anyofgroup").AsInt32().Nullable()
					.WithColumn("allowtrainee").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("gracedaysoverride").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_personnelrolecertificationrequirements_role ON personnelrolecertificationrequirements (personnelroleid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_personnelrolecertificationrequirements_department ON personnelrolecertificationrequirements (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_personnelrolecertificationrequirements_type ON personnelrolecertificationrequirements (departmentcertificationtypeid);");
				Create.ForeignKey("fk_personnelrolecertificationrequirements_role").FromTable("personnelrolecertificationrequirements").ForeignColumn("personnelroleid").ToTable("personnelroles").PrimaryColumn("personnelroleid");
				Create.ForeignKey("fk_personnelrolecertificationrequirements_type").FromTable("personnelrolecertificationrequirements").ForeignColumn("departmentcertificationtypeid").ToTable("departmentcertificationtypes").PrimaryColumn("departmentcertificationtypeid");
			}

			if (!Schema.Table("departmentcertificationsettings").Exists())
			{
				Create.Table("departmentcertificationsettings")
					.WithColumn("departmentid").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("enforcementmode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("roleremovalgracedays").AsInt32().NotNullable().WithDefaultValue(30)
					.WithColumn("notifyleaddayscsv").AsString(100).NotNullable().WithDefaultValue("60,30,14,7,1")
					.WithColumn("notifycertificationholder").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("treatpendingverificationasvalid").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("sendadmindigest").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("updatedon").AsDateTime2().NotNullable()
					.WithColumn("updatedbyuserid").AsString(128).Nullable();
			}

			if (!Schema.Table("unitcertifications").Exists())
			{
				Create.Table("unitcertifications")
					.WithColumn("unitcertificationid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("unitid").AsInt32().NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("departmentcertificationtypeid").AsInt32().NotNullable()
					.WithColumn("number").AsCustom("text").Nullable()
					.WithColumn("issuedby").AsCustom("text").Nullable()
					.WithColumn("issuedon").AsDateTime2().Nullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("statuschangedon").AsDateTime2().Nullable()
					.WithColumn("statuschangedbyuserid").AsString(128).Nullable()
					.WithColumn("statusreason").AsString(500).Nullable()
					.WithColumn("notes").AsCustom("text").Nullable()
					.WithColumn("filename").AsCustom("text").Nullable()
					.WithColumn("filetype").AsString(200).Nullable()
					.WithColumn("filesize").AsInt32().Nullable()
					.WithColumn("data").AsCustom("bytea").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_unitcertifications_unit ON unitcertifications (unitid, isdeleted);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_unitcertifications_department ON unitcertifications (departmentid, isdeleted, expireson);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_unitcertifications_type ON unitcertifications (departmentcertificationtypeid);");
				Create.ForeignKey("fk_unitcertifications_unit").FromTable("unitcertifications").ForeignColumn("unitid").ToTable("units").PrimaryColumn("unitid");
				Create.ForeignKey("fk_unitcertifications_type").FromTable("unitcertifications").ForeignColumn("departmentcertificationtypeid").ToTable("departmentcertificationtypes").PrimaryColumn("departmentcertificationtypeid");
			}

			var certs = "personnelcertifications";
			if (!Schema.Table(certs).Column("departmentcertificationtypeid").Exists())
				Alter.Table(certs).AddColumn("departmentcertificationtypeid").AsInt32().Nullable();
			if (!Schema.Table(certs).Column("status").Exists())
				Alter.Table(certs).AddColumn("status").AsInt32().NotNullable().WithDefaultValue(0);
			if (!Schema.Table(certs).Column("statuschangedon").Exists())
				Alter.Table(certs).AddColumn("statuschangedon").AsDateTime2().Nullable();
			if (!Schema.Table(certs).Column("statuschangedbyuserid").Exists())
				Alter.Table(certs).AddColumn("statuschangedbyuserid").AsString(128).Nullable();
			if (!Schema.Table(certs).Column("statusreason").Exists())
				Alter.Table(certs).AddColumn("statusreason").AsString(500).Nullable();
			if (!Schema.Table(certs).Column("verifiedbyuserid").Exists())
				Alter.Table(certs).AddColumn("verifiedbyuserid").AsString(128).Nullable();
			if (!Schema.Table(certs).Column("verifiedon").Exists())
				Alter.Table(certs).AddColumn("verifiedon").AsDateTime2().Nullable();
			if (!Schema.Table(certs).Column("isdeleted").Exists())
				Alter.Table(certs).AddColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false);

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_personnelcertifications_type ON personnelcertifications (departmentcertificationtypeid, isdeleted);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_personnelcertifications_department_expiry ON personnelcertifications (departmentid, isdeleted, expireson);");

			Execute.Sql(
				"UPDATE personnelcertifications c SET departmentcertificationtypeid = t.departmentcertificationtypeid " +
				"FROM departmentcertificationtypes t " +
				"WHERE t.departmentid = c.departmentid AND t.type = c.type AND t.isdeleted = FALSE " +
				"AND c.departmentcertificationtypeid IS NULL AND c.isprotected = FALSE AND c.type IS NOT NULL AND c.type <> '';");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS unitcertifications;");
			Execute.Sql("DROP TABLE IF EXISTS departmentcertificationsettings;");
			Execute.Sql("DROP TABLE IF EXISTS personnelrolecertificationrequirements;");
			foreach (var column in new[] { "departmentcertificationtypeid", "status", "statuschangedon", "statuschangedbyuserid", "statusreason", "verifiedbyuserid", "verifiedon", "isdeleted" })
				Execute.Sql($"ALTER TABLE personnelcertifications DROP COLUMN IF EXISTS {column};");
			Execute.Sql("DROP INDEX IF EXISTS ux_departmentcertificationtypes_code;");
			foreach (var column in new[] { "code", "category", "appliesto", "description", "issuingauthority", "defaultvaliditymonths", "neverexpires", "renewalcredithoursrequired", "requiresverification", "isactive", "isdeleted", "addedon", "addedbyuserid", "editedon", "editedbyuserid" })
				Execute.Sql($"ALTER TABLE departmentcertificationtypes DROP COLUMN IF EXISTS {column};");
		}
	}
}
