using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(46)]
	public class M0046_AddingSsoAndSecurityPolicyPg : Migration
	{
		public override void Up()
		{
			// ── DepartmentSsoConfigs ──────────────────────────────────────────
			Create.Table("DepartmentSsoConfigs".ToLower())
				.WithColumn("DepartmentSsoConfigId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
				.WithColumn("SsoProviderType".ToLower()).AsInt32().NotNullable()
				.WithColumn("IsEnabled".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("ClientId".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("EncryptedClientSecret".ToLower()).AsCustom("text").Nullable()
				.WithColumn("Authority".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("MetadataUrl".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("EntityId".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("AssertionConsumerServiceUrl".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("EncryptedIdpCertificate".ToLower()).AsCustom("text").Nullable()
				.WithColumn("EncryptedSigningCertificate".ToLower()).AsCustom("text").Nullable()
				.WithColumn("AttributeMappingJson".ToLower()).AsCustom("text").Nullable()
				.WithColumn("AllowLocalLogin".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("AutoProvisionUsers".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("DefaultRankId".ToLower()).AsInt32().Nullable()
				.WithColumn("ScimEnabled".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("EncryptedScimBearerToken".ToLower()).AsCustom("text").Nullable()
				.WithColumn("CreatedByUserId".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("CreatedOn".ToLower()).AsDateTime().NotNullable()
				.WithColumn("UpdatedByUserId".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("UpdatedOn".ToLower()).AsDateTime().Nullable();

			Create.Index("IX_DepartmentSsoConfigs_DepartmentId".ToLower())
				.OnTable("DepartmentSsoConfigs".ToLower())
				.OnColumn("DepartmentId".ToLower());

			Create.Index("IX_DepartmentSsoConfigs_EntityId".ToLower())
				.OnTable("DepartmentSsoConfigs".ToLower())
				.OnColumn("EntityId".ToLower());

			// ── DepartmentSecurityPolicies ────────────────────────────────────
			Create.Table("DepartmentSecurityPolicies".ToLower())
				.WithColumn("DepartmentSecurityPolicyId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
				.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
				.WithColumn("RequireMfa".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("RequireSso".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("SessionTimeoutMinutes".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("MaxConcurrentSessions".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("AllowedIpRanges".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("PasswordExpirationDays".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("MinPasswordLength".ToLower()).AsInt32().NotNullable().WithDefaultValue(8)
				.WithColumn("RequirePasswordComplexity".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("DataClassificationLevel".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("CreatedOn".ToLower()).AsDateTime().NotNullable()
				.WithColumn("UpdatedOn".ToLower()).AsDateTime().Nullable();

			Create.Index("IX_DepartmentSecurityPolicies_DepartmentId".ToLower())
				.OnTable("DepartmentSecurityPolicies".ToLower())
				.OnColumn("DepartmentId".ToLower());

			// ── DepartmentMembers SSO columns ─────────────────────────────────
			Alter.Table("DepartmentMembers".ToLower())
				.AddColumn("ExternalSsoId".ToLower()).AsCustom("citext").Nullable()
				.AddColumn("SsoLinkedOn".ToLower()).AsDateTime().Nullable()
				.AddColumn("LastSsoLoginOn".ToLower()).AsDateTime().Nullable();

			Create.Index("IX_DepartmentMembers_ExternalSsoId".ToLower())
				.OnTable("DepartmentMembers".ToLower())
				.OnColumn("ExternalSsoId".ToLower());
		}

		public override void Down()
		{
			Delete.Index("IX_DepartmentMembers_ExternalSsoId".ToLower()).OnTable("DepartmentMembers".ToLower());
			Delete.Column("ExternalSsoId".ToLower()).FromTable("DepartmentMembers".ToLower());
			Delete.Column("SsoLinkedOn".ToLower()).FromTable("DepartmentMembers".ToLower());
			Delete.Column("LastSsoLoginOn".ToLower()).FromTable("DepartmentMembers".ToLower());

			Delete.Index("IX_DepartmentSecurityPolicies_DepartmentId".ToLower()).OnTable("DepartmentSecurityPolicies".ToLower());
			Delete.Table("DepartmentSecurityPolicies".ToLower());

			Delete.Index("IX_DepartmentSsoConfigs_EntityId".ToLower()).OnTable("DepartmentSsoConfigs".ToLower());
			Delete.Index("IX_DepartmentSsoConfigs_DepartmentId".ToLower()).OnTable("DepartmentSsoConfigs".ToLower());
			Delete.Table("DepartmentSsoConfigs".ToLower());
		}
	}
}

