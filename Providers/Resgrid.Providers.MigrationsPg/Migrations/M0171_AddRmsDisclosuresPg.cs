using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0171 (registry M0171, RMS-3): rmsdisclosurerequests and rmsdisclosureproductions.
	/// Lowercase unquoted identifiers, citext for text, partial unique index on the department request number.
	/// </summary>
	[Migration(171)]
	public class M0171_AddRmsDisclosuresPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsdisclosurerequests").Exists())
			{
				Create.Table("rmsdisclosurerequests")
					.WithColumn("rmsdisclosurerequestid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("requestnumber").AsCustom("citext").Nullable()
					.WithColumn("requestername").AsCustom("citext").Nullable()
					.WithColumn("requesterorganization").AsCustom("citext").Nullable()
					.WithColumn("requestercontact").AsCustom("citext").Nullable()
					.WithColumn("receivedon").AsDateTime2().NotNullable()
					.WithColumn("statutorydueon").AsDateTime2().Nullable()
					.WithColumn("jurisdictionprofile").AsCustom("citext").Nullable()
					.WithColumn("scopenarrative").AsCustom("citext").Nullable()
					.WithColumn("scopequeryjson").AsCustom("citext").Nullable()
					.WithColumn("state").AsInt32().NotNullable()
					.WithColumn("assignedtouserid").AsString(128).Nullable()
					.WithColumn("redactionprofile").AsCustom("citext").Nullable()
					.WithColumn("closedon").AsDateTime2().Nullable()
					.WithColumn("closedbyuserid").AsString(128).Nullable()
					.WithColumn("dispositionreason").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("modifiedbyuserid").AsString(128).Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsdisclosurerequests_department_state ON rmsdisclosurerequests (departmentid, state);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsdisclosurerequests_department_due ON rmsdisclosurerequests (departmentid, statutorydueon);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsdisclosurerequests_number ON rmsdisclosurerequests (departmentid, requestnumber) WHERE requestnumber IS NOT NULL AND deletedon IS NULL;");
			}

			if (!Schema.Table("rmsdisclosureproductions").Exists())
			{
				Create.Table("rmsdisclosureproductions")
					.WithColumn("rmsdisclosureproductionid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("disclosurerequestid").AsString(36).NotNullable()
					.WithColumn("productionnumber").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("redactionprofile").AsCustom("citext").Nullable()
					.WithColumn("producedsetjson").AsCustom("citext").Nullable()
					.WithColumn("artifactjson").AsCustom("citext").Nullable()
					.WithColumn("checksum").AsCustom("citext").Nullable()
					.WithColumn("bytesize").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("recordcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("withheldfieldsjson").AsCustom("citext").Nullable()
					.WithColumn("withheldfieldcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("preparedbyuserid").AsString(128).Nullable()
					.WithColumn("preparedon").AsDateTime2().NotNullable()
					.WithColumn("releasedbyuserid").AsString(128).Nullable()
					.WithColumn("releasedon").AsDateTime2().Nullable()
					.WithColumn("protectedenvelope").AsCustom("citext").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsdisclosureproductions_department_request ON rmsdisclosureproductions (departmentid, disclosurerequestid);");
			}
		}

		public override void Down()
		{
			foreach (var table in new[] { "rmsdisclosureproductions", "rmsdisclosurerequests" })
			{
				if (Schema.Table(table).Exists())
					Delete.Table(table);
			}
		}
	}
}
