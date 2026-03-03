using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(46)]
	public class M0046_AddingSsoAndSecurityPolicy : Migration
	{
		public override void Up()
		{
			// ── DepartmentSsoConfigs ──────────────────────────────────────────
			Create.Table("DepartmentSsoConfigs")
				.WithColumn("DepartmentSsoConfigId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("SsoProviderType").AsInt32().NotNullable()
				.WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("ClientId").AsString(512).Nullable()
				.WithColumn("EncryptedClientSecret").AsString(int.MaxValue).Nullable()
				.WithColumn("Authority").AsString(1024).Nullable()
				.WithColumn("MetadataUrl").AsString(1024).Nullable()
				.WithColumn("EntityId").AsString(512).Nullable()
				.WithColumn("AssertionConsumerServiceUrl").AsString(1024).Nullable()
				.WithColumn("EncryptedIdpCertificate").AsString(int.MaxValue).Nullable()
				.WithColumn("EncryptedSigningCertificate").AsString(int.MaxValue).Nullable()
				.WithColumn("AttributeMappingJson").AsString(int.MaxValue).Nullable()
				.WithColumn("AllowLocalLogin").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("AutoProvisionUsers").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("DefaultRankId").AsInt32().Nullable()
				.WithColumn("ScimEnabled").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("EncryptedScimBearerToken").AsString(int.MaxValue).Nullable()
				.WithColumn("CreatedByUserId").AsString(128).NotNullable()
				.WithColumn("CreatedOn").AsDateTime().NotNullable()
				.WithColumn("UpdatedByUserId").AsString(128).Nullable()
				.WithColumn("UpdatedOn").AsDateTime().Nullable();

			Create.Index("IX_DepartmentSsoConfigs_DepartmentId")
				.OnTable("DepartmentSsoConfigs")
				.OnColumn("DepartmentId");

			Create.Index("IX_DepartmentSsoConfigs_EntityId")
				.OnTable("DepartmentSsoConfigs")
				.OnColumn("EntityId");

			// ── DepartmentSecurityPolicies ────────────────────────────────────
			Create.Table("DepartmentSecurityPolicies")
				.WithColumn("DepartmentSecurityPolicyId").AsInt32().NotNullable().PrimaryKey().Identity()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("RequireMfa").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("RequireSso").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("SessionTimeoutMinutes").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("MaxConcurrentSessions").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("AllowedIpRanges").AsString(2048).Nullable()
				.WithColumn("PasswordExpirationDays").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("MinPasswordLength").AsInt32().NotNullable().WithDefaultValue(8)
				.WithColumn("RequirePasswordComplexity").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("DataClassificationLevel").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("CreatedOn").AsDateTime().NotNullable()
				.WithColumn("UpdatedOn").AsDateTime().Nullable();

			Create.Index("IX_DepartmentSecurityPolicies_DepartmentId")
				.OnTable("DepartmentSecurityPolicies")
				.OnColumn("DepartmentId");

			// ── DepartmentMembers SSO columns ─────────────────────────────────
			Alter.Table("DepartmentMembers")
				.AddColumn("ExternalSsoId").AsString(512).Nullable()
				.AddColumn("SsoLinkedOn").AsDateTime().Nullable()
				.AddColumn("LastSsoLoginOn").AsDateTime().Nullable();

			Create.Index("IX_DepartmentMembers_ExternalSsoId")
				.OnTable("DepartmentMembers")
				.OnColumn("ExternalSsoId");
		}

		public override void Down()
		{
			Delete.Index("IX_DepartmentMembers_ExternalSsoId").OnTable("DepartmentMembers");
			Delete.Column("ExternalSsoId").FromTable("DepartmentMembers");
			Delete.Column("SsoLinkedOn").FromTable("DepartmentMembers");
			Delete.Column("LastSsoLoginOn").FromTable("DepartmentMembers");

			Delete.Index("IX_DepartmentSecurityPolicies_DepartmentId").OnTable("DepartmentSecurityPolicies");
			Delete.Table("DepartmentSecurityPolicies");

			Delete.Index("IX_DepartmentSsoConfigs_EntityId").OnTable("DepartmentSsoConfigs");
			Delete.Index("IX_DepartmentSsoConfigs_DepartmentId").OnTable("DepartmentSsoConfigs");
			Delete.Table("DepartmentSsoConfigs");
		}
	}
}

