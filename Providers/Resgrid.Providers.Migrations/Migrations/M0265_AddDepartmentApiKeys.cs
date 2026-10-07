using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Department API keys: a department admin issues a key that lets another system call a fixed set of department
	/// v4 endpoints without a user's credentials. Only a keyed hash of the key is stored (unique, the lookup path).
	/// Every key expires and can be revoked. No foreign key to Departments, like CallNumberSequences: the department
	/// purge deletes the rows explicitly.
	/// </summary>
	[Migration(265)]
	public class M0265_AddDepartmentApiKeys : Migration
	{
		private const string Table = "DepartmentApiKeys";

		public override void Up()
		{
			if (!Schema.Table(Table).Exists())
			{
				Create.Table(Table)
					.WithColumn("DepartmentApiKeyId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(100).NotNullable()
					.WithColumn("KeyPrefix").AsString(20).NotNullable()
					.WithColumn("SecretHash").AsString(64).NotNullable()
					.WithColumn("Scopes").AsString(500).NotNullable()
					.WithColumn("AllowedIpRanges").AsString(1000).Nullable()
					.WithColumn("CreatedByUserId").AsString(128).NotNullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ExpiresOn").AsDateTime2().NotNullable()
					.WithColumn("LastUsedOn").AsDateTime2().Nullable()
					.WithColumn("LastUsedIp").AsString(64).Nullable()
					.WithColumn("RevokedOn").AsDateTime2().Nullable()
					.WithColumn("RevokedByUserId").AsString(128).Nullable();
			}

			if (!Schema.Table(Table).Index("UX_DepartmentApiKeys_SecretHash").Exists())
			{
				Create.Index("UX_DepartmentApiKeys_SecretHash")
					.OnTable(Table)
					.OnColumn("SecretHash").Ascending()
					.WithOptions().Unique();
			}

			if (!Schema.Table(Table).Index("IX_DepartmentApiKeys_DepartmentId").Exists())
			{
				Create.Index("IX_DepartmentApiKeys_DepartmentId")
					.OnTable(Table)
					.OnColumn("DepartmentId").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table(Table).Exists())
				Delete.Table(Table);
		}
	}
}
