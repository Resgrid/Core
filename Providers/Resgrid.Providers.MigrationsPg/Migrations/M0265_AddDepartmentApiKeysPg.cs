using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Department API keys: a department admin issues a key that lets another system call a fixed set of department
	/// v4 endpoints without a user's credentials. Only a keyed hash of the key is stored (unique, the lookup path).
	/// Every key expires and can be revoked. No foreign key to departments, like callnumbersequences.
	/// </summary>
	[Migration(265)]
	public class M0265_AddDepartmentApiKeysPg : Migration
	{
		private const string Table = "departmentapikeys";

		public override void Up()
		{
			if (!Schema.Table(Table).Exists())
			{
				Create.Table(Table)
					.WithColumn("departmentapikeyid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("keyprefix").AsString(20).NotNullable()
					.WithColumn("secrethash").AsString(64).NotNullable()
					.WithColumn("scopes").AsCustom("citext").NotNullable()
					.WithColumn("allowedipranges").AsCustom("citext").Nullable()
					.WithColumn("createdbyuserid").AsCustom("citext").NotNullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("expireson").AsDateTime2().NotNullable()
					.WithColumn("lastusedon").AsDateTime2().Nullable()
					.WithColumn("lastusedip").AsCustom("citext").Nullable()
					.WithColumn("revokedon").AsDateTime2().Nullable()
					.WithColumn("revokedbyuserid").AsCustom("citext").Nullable();
			}

			if (!Schema.Table(Table).Index("ux_departmentapikeys_secrethash").Exists())
			{
				Create.Index("ux_departmentapikeys_secrethash")
					.OnTable(Table)
					.OnColumn("secrethash").Ascending()
					.WithOptions().Unique();
			}

			if (!Schema.Table(Table).Index("ix_departmentapikeys_departmentid").Exists())
			{
				Create.Index("ix_departmentapikeys_departmentid")
					.OnTable(Table)
					.OnColumn("departmentid").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table(Table).Exists())
				Delete.Table(Table);
		}
	}
}
