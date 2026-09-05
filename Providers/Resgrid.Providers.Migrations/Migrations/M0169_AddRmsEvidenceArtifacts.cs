using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Immutable evidence artifacts (RMS plan sections 4.5 and 5.2, registry M0169, package RMS-3):
	/// RmsEvidenceArtifacts.
	/// <para>
	/// One table serves all six sources — readiness packets, run card activations, tracking fixes, promoted chat,
	/// inventory usage and certification snapshots — because the properties that matter are the same for each:
	/// provenance, coverage period, a checksummed manifest, a classification decided at capture, and a retention
	/// rule. The per-source rows live inside ManifestJson rather than a child table, so the checksum covers
	/// everything that was attested to and the two cannot drift apart.
	/// </para>
	/// <para>
	/// There is no update path. A correction inserts a new artifact and stamps SupersededByArtifactId on the old
	/// one, which stays readable — that is what makes "what did the crew have on the night" answerable later.
	/// </para>
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(169)]
	public class M0169_AddRmsEvidenceArtifacts : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsEvidenceArtifacts").Exists())
			{
				Create.Table("RmsEvidenceArtifacts")
					.WithColumn("RmsEvidenceArtifactId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RecordKind").AsInt32().NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("Kind").AsInt32().NotNullable()
					.WithColumn("Title").AsString(400).Nullable()
					.WithColumn("CaptureReason").AsString(500).Nullable()
					.WithColumn("SourceSubsystem").AsString(100).Nullable()
					.WithColumn("SourceEntityType").AsString(100).Nullable()
					.WithColumn("SourceEntityId").AsString(200).Nullable()
					.WithColumn("IdentifierScheme").AsString(100).Nullable()
					.WithColumn("SourceVersion").AsString(50).Nullable()
					.WithColumn("CoverageStart").AsDateTime2().Nullable()
					.WithColumn("CoverageEnd").AsDateTime2().Nullable()
					.WithColumn("ManifestJson").AsString(int.MaxValue).Nullable()
					.WithColumn("Checksum").AsString(80).Nullable()
					.WithColumn("ByteSize").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("SourceItemCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StorageReference").AsString(500).Nullable()
					.WithColumn("Classification").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RetentionYears").AsInt32().Nullable()
					.WithColumn("CapturedByUserId").AsString(128).Nullable()
					.WithColumn("CapturedOn").AsDateTime2().NotNullable()
					.WithColumn("OriginClient").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SupersededByArtifactId").AsString(36).Nullable()
					.WithColumn("SupersededOn").AsDateTime2().Nullable()
					.WithColumn("ProtectedEnvelope").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsEvidenceArtifacts_Department_Record_Revision").OnTable("RmsEvidenceArtifacts")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending().OnColumn("RevisionId").Ascending();
				Create.Index("IX_RmsEvidenceArtifacts_Department_Kind").OnTable("RmsEvidenceArtifacts")
					.OnColumn("DepartmentId").Ascending().OnColumn("Kind").Ascending().OnColumn("CapturedOn").Descending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("RmsEvidenceArtifacts").Exists())
				Delete.Table("RmsEvidenceArtifacts");
		}
	}
}
