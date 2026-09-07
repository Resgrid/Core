using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Records (RMS-1B) typed values (plan section 5.3, registry M0159): one discriminated table, one row per scalar / repeating-group cell / multi-select option, exactly one column group populated (check constraint plus the service guard), repeating rows in RmsRecordValueGroups, explicit equality/range indexes filtered to unprotected rows, inert ADP columns.
	/// PostgreSQL twin of the SQL Server migration; lower-case identifiers, citext keys, existence-guarded.
	/// </summary>
	[Migration(159)]
	public class M0159_AddRmsRecordValuesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsrecordvaluegroups").Exists())
			{
				Create.Table("rmsrecordvaluegroups")
					.WithColumn("rmsrecordvaluegroupid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("recordid").AsCustom("citext").NotNullable()
					.WithColumn("recordkind").AsInt32().NotNullable()
					.WithColumn("revisionid").AsCustom("citext").Nullable()
					.WithColumn("rmsrecorddefinitionversionid").AsCustom("citext").Nullable()
					.WithColumn("sectionkey").AsCustom("citext").NotNullable()
					.WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("clientrowkey").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordvaluegroups_department_record_revision ON rmsrecordvaluegroups (departmentid, recordid, revisionid);");
			}

			if (!Schema.Table("rmsrecordvalues").Exists())
			{
				Create.Table("rmsrecordvalues")
					.WithColumn("rmsrecordvalueid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("recordid").AsCustom("citext").NotNullable()
					.WithColumn("recordkind").AsInt32().NotNullable()
					.WithColumn("revisionid").AsCustom("citext").Nullable()
					.WithColumn("rmsrecorddefinitionversionid").AsCustom("citext").Nullable()
					.WithColumn("fieldkey").AsCustom("citext").NotNullable()
					.WithColumn("rmsrecordvaluegroupid").AsCustom("citext").Nullable()
					.WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("valuetype").AsInt32().NotNullable()
					.WithColumn("textvalue").AsCustom("citext").Nullable()
					.WithColumn("longtextvalue").AsCustom("text").Nullable()
					.WithColumn("numbervalue").AsDecimal(28, 10).Nullable()
					.WithColumn("boolvalue").AsBoolean().Nullable()
					.WithColumn("datetimevalue").AsDateTime2().Nullable()
					.WithColumn("datetimeoffsetminutes").AsInt32().Nullable()
					.WithColumn("durationseconds").AsInt64().Nullable()
					.WithColumn("unitcode").AsCustom("citext").Nullable()
					.WithColumn("canonicalnumbervalue").AsDecimal(28, 10).Nullable()
					.WithColumn("canonicalunitcode").AsCustom("citext").Nullable()
					.WithColumn("currencycode").AsCustom("citext").Nullable()
					.WithColumn("referencetype").AsCustom("citext").Nullable()
					.WithColumn("referenceid").AsCustom("citext").Nullable()
					.WithColumn("referencesnapshotjson").AsCustom("text").Nullable()
					.WithColumn("optionkey").AsCustom("citext").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedenvelope").AsCustom("text").Nullable()
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordvalues_department_record_revision ON rmsrecordvalues (departmentid, recordid, revisionid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordvalues_department_version_field_text ON rmsrecordvalues (departmentid, rmsrecorddefinitionversionid, fieldkey, textvalue) WHERE isprotected = false;");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordvalues_department_version_field_number ON rmsrecordvalues (departmentid, rmsrecorddefinitionversionid, fieldkey, numbervalue) WHERE isprotected = false;");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordvalues_department_version_field_datetime ON rmsrecordvalues (departmentid, rmsrecorddefinitionversionid, fieldkey, datetimevalue) WHERE isprotected = false;");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordvalues_department_revision ON rmsrecordvalues (departmentid, revisionid);");
				Execute.Sql("ALTER TABLE rmsrecordvalues ADD CONSTRAINT ck_rmsrecordvalues_onecolumngroup CHECK ((CASE WHEN textvalue IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN longtextvalue IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN numbervalue IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN boolvalue IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN datetimevalue IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN durationseconds IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN referenceid IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN optionkey IS NOT NULL THEN 1 ELSE 0 END) = CASE WHEN protectedenvelope IS NULL THEN 1 ELSE 0 END);");
			}

		}

		public override void Down()
		{
			if (Schema.Table("rmsrecordvalues").Exists())
				Delete.Table("rmsrecordvalues");
			if (Schema.Table("rmsrecordvaluegroups").Exists())
				Delete.Table("rmsrecordvaluegroups");
		}
	}
}
