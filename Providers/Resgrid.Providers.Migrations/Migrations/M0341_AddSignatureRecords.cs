using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// S0, the one signing layer (plan §21.3): SignatureRecords, the generalized RmsSignature. A signature binds to an artifact
	/// hash (never a live row), stores the exact statement, its level (1 attest, 2 step-up) and method (session, step-up,
	/// passkey), and can be voided but not removed. No foreign keys: UnlinkedDepartmentCleanup removes the department's rows.
	/// Records keeps RmsSignatures (decision 21). Existence-guarded for safe retry.
	/// </summary>
	[Migration(341)]
	public class M0341_AddSignatureRecords : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("SignatureRecords").Exists())
			{
				Create.Table("SignatureRecords")
					.WithColumn("SignatureRecordId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("SubjectKind").AsInt32().NotNullable()
					.WithColumn("SubjectId").AsString(128).NotNullable()
					.WithColumn("RevisionId").AsString(128).Nullable()
					.WithColumn("SignerUserId").AsString(128).NotNullable()
					.WithColumn("SignerNameSnapshot").AsString(256).Nullable()
					.WithColumn("SignerRoleSnapshot").AsString(1000).Nullable()
					.WithColumn("Intent").AsInt32().NotNullable()
					.WithColumn("StatementVersion").AsString(64).Nullable()
					.WithColumn("StatementText").AsString(2000).Nullable()
					.WithColumn("Level").AsInt32().NotNullable()
					.WithColumn("Method").AsInt32().NotNullable()
					.WithColumn("ArtifactSha256").AsString(64).NotNullable()
					.WithColumn("SignedOn").AsDateTime2().NotNullable()
					.WithColumn("IpAddress").AsString(64).Nullable()
					.WithColumn("UserAgent").AsString(1000).Nullable()
					.WithColumn("PasskeyCredentialId").AsString(128).Nullable()
					.WithColumn("StepUpEvidenceReference").AsString(256).Nullable()
					.WithColumn("VoidedOn").AsDateTime2().Nullable()
					.WithColumn("VoidedByUserId").AsString(128).Nullable()
					.WithColumn("VoidReason").AsString(1000).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable();
			}

			if (!Schema.Table("SignatureRecords").Index("IX_SignatureRecords_Subject").Exists())
				Create.Index("IX_SignatureRecords_Subject").OnTable("SignatureRecords")
					.OnColumn("DepartmentId").Ascending().OnColumn("SubjectKind").Ascending().OnColumn("SubjectId").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("SignatureRecords").Exists())
				Delete.Table("SignatureRecords");
		}
	}
}
