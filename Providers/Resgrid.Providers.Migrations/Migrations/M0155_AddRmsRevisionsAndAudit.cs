using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Records (RMS) immutable revisions and access audit (RMS plan sections 4.8/5.2, registry M0155).
	/// RmsRevisions holds one complete, checksummed, server-authored snapshot per finalize/amend/void
	/// transition, pinned to the definition version it was written under and referencing the prior
	/// revision; diffs are computed from two snapshots and never stored. RmsAccessAudits records every
	/// read/search/change/sign/print/export/submit/share/support/denied action with actor, purpose and
	/// correlation, and never a protected value. Existence-guarded for safe retry.
	/// </summary>
	[Migration(155)]
	public class M0155_AddRmsRevisionsAndAudit : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsRevisions").Exists())
			{
				Create.Table("RmsRevisions")
					.WithColumn("RmsRevisionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RecordKind").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("RevisionNumber").AsInt32().NotNullable()
					.WithColumn("Transition").AsInt32().NotNullable()
					.WithColumn("PriorRevisionId").AsString(36).Nullable()
					.WithColumn("DefinitionKey").AsString(100).NotNullable()
					.WithColumn("DefinitionVersion").AsInt32().NotNullable()
					.WithColumn("SnapshotJson").AsString(int.MaxValue).NotNullable()
					.WithColumn("Checksum").AsString(128).NotNullable()
					.WithColumn("ActorUserId").AsString(128).NotNullable()
					.WithColumn("ActorRoleSnapshot").AsString(200).Nullable()
					.WithColumn("ReasonCode").AsString(50).Nullable()
					.WithColumn("ReasonText").AsString(int.MaxValue).Nullable()
					.WithColumn("AttestationStatementVersion").AsString(20).Nullable()
					.WithColumn("AttestedOn").AsDateTime2().Nullable()
					.WithColumn("OriginClient").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable();

				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsRevisions_Department_Record_Number ON RmsRevisions (DepartmentId, RecordId, RevisionNumber);");
				Create.Index("IX_RmsRevisions_Department_Record").OnTable("RmsRevisions")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending();
			}

			if (!Schema.Table("RmsAccessAudits").Exists())
			{
				Create.Table("RmsAccessAudits")
					.WithColumn("RmsAccessAuditId").AsInt64().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("RecordId").AsString(36).Nullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("Action").AsInt32().NotNullable()
					.WithColumn("ActorUserId").AsString(128).Nullable()
					.WithColumn("Purpose").AsString(200).Nullable()
					.WithColumn("CorrelationId").AsString(36).Nullable()
					.WithColumn("OriginClient").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IpAddress").AsString(64).Nullable()
					.WithColumn("Successful").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("OccurredOn").AsDateTime2().NotNullable()
					.WithColumn("DetailJson").AsString(int.MaxValue).Nullable();

				Create.Index("IX_RmsAccessAudits_Department_Record_Occurred").OnTable("RmsAccessAudits")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending().OnColumn("OccurredOn").Descending();
				Create.Index("IX_RmsAccessAudits_Department_Occurred").OnTable("RmsAccessAudits")
					.OnColumn("DepartmentId").Ascending().OnColumn("OccurredOn").Descending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("RmsAccessAudits").Exists())
				Delete.Table("RmsAccessAudits");

			if (Schema.Table("RmsRevisions").Exists())
				Delete.Table("RmsRevisions");
		}
	}
}
