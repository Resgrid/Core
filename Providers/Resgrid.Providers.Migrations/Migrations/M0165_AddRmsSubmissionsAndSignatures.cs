using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Reporting-destination submissions and officer attestation (RMS plan sections 5.3/5.5, registry M0165,
	/// RMS-2). RmsSubmissions: one row per exchange with a destination, immutable payload/response artifacts with
	/// checksums, scoped idempotency key (unique), lease columns for worker 41. RmsSignatures: signer, role,
	/// intent, statement version, method, time and the revision checksum the signature binds to.
	/// </summary>
	[Migration(165)]
	public class M0165_AddRmsSubmissionsAndSignatures : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsSubmissions").Exists())
			{
				Create.Table("RmsSubmissions")
					.WithColumn("RmsSubmissionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RecordKind").AsInt32().NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("Destination").AsString(50).NotNullable()
					.WithColumn("DestinationVersion").AsString(20).Nullable()
					.WithColumn("IdempotencyKey").AsString(64).NotNullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("MaxAttempts").AsInt32().NotNullable().WithDefaultValue(5)
					.WithColumn("NextAttemptOn").AsDateTime2().Nullable()
					.WithColumn("LeaseOwner").AsString(100).Nullable()
					.WithColumn("LeaseExpiresOn").AsDateTime2().Nullable()
					.WithColumn("PayloadJson").AsString(int.MaxValue).Nullable()
					.WithColumn("PayloadChecksum").AsString(64).Nullable()
					.WithColumn("ResponseJson").AsString(int.MaxValue).Nullable()
					.WithColumn("ResponseChecksum").AsString(64).Nullable()
					.WithColumn("ResponseStatusCode").AsInt32().Nullable()
					.WithColumn("ExternalId").AsString(100).Nullable()
					.WithColumn("ExternalStatus").AsString(50).Nullable()
					.WithColumn("ErrorSummary").AsString(int.MaxValue).Nullable()
					.WithColumn("QueuedOn").AsDateTime2().NotNullable()
					.WithColumn("SentOn").AsDateTime2().Nullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsSubmissions_IdempotencyKey ON RmsSubmissions (IdempotencyKey);");
				Create.Index("IX_RmsSubmissions_State_NextAttempt").OnTable("RmsSubmissions")
					.OnColumn("State").Ascending().OnColumn("NextAttemptOn").Ascending();
				Create.Index("IX_RmsSubmissions_Department_Record").OnTable("RmsSubmissions")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending();
			}

			if (!Schema.Table("RmsSignatures").Exists())
			{
				Create.Table("RmsSignatures")
					.WithColumn("RmsSignatureId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RecordKind").AsInt32().NotNullable()
					.WithColumn("RevisionId").AsString(36).NotNullable()
					.WithColumn("SignerUserId").AsString(128).NotNullable()
					.WithColumn("SignerNameSnapshot").AsString(200).Nullable()
					.WithColumn("SignerRoleSnapshot").AsString(200).Nullable()
					.WithColumn("Intent").AsInt32().NotNullable()
					.WithColumn("StatementVersion").AsString(20).Nullable()
					.WithColumn("StatementText").AsString(int.MaxValue).Nullable()
					.WithColumn("Method").AsInt32().NotNullable()
					.WithColumn("SignedOn").AsDateTime2().NotNullable()
					.WithColumn("IpAddress").AsString(64).Nullable()
					.WithColumn("ArtifactChecksum").AsString(64).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsSignatures_Department_Record").OnTable("RmsSignatures")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending();
				Create.Index("IX_RmsSignatures_Department_Revision").OnTable("RmsSignatures")
					.OnColumn("DepartmentId").Ascending().OnColumn("RevisionId").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("RmsSignatures").Exists())
				Delete.Table("RmsSignatures");
			if (Schema.Table("RmsSubmissions").Exists())
				Delete.Table("RmsSubmissions");
		}
	}
}
