using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Records (RMS) child tables (registry M0151): RmsRecordParticipants (person snapshot, role, unit
	/// assignment, participation times, source), RmsRecordUnitResponses (unit snapshot and
	/// dispatched/en-route/on-scene/released/in-quarters times with retained prefill provenance) and
	/// RmsRecordAttachments (authorized file metadata, checksum, scan state, metadata-strip decision,
	/// bytes). Draft rows carry RevisionId NULL; finalization copies them under the new RevisionId so a
	/// later profile or apparatus change never rewrites a finalized Record. DepartmentId and
	/// ProtectionId are on every child row (plan section 5.9.1: a child cannot inherit the AAD tuple
	/// through a join). Existence-guarded for safe retry.
	/// </summary>
	[Migration(151)]
	public class M0151_AddRmsParticipantsUnitsAttachments : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsRecordParticipants").Exists())
			{
				Create.Table("RmsRecordParticipants")
					.WithColumn("RmsRecordParticipantId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("DisplayNameSnapshot").AsString(200).Nullable()
					.WithColumn("GroupIdSnapshot").AsInt32().Nullable()
					.WithColumn("GroupNameSnapshot").AsString(200).Nullable()
					.WithColumn("UnitId").AsInt32().Nullable()
					.WithColumn("Role").AsString(50).Nullable()
					.WithColumn("ParticipationStart").AsDateTime2().Nullable()
					.WithColumn("ParticipationEnd").AsDateTime2().Nullable()
					.WithColumn("SourceKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsRecordParticipants_Department_Record").OnTable("RmsRecordParticipants")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending();
				Create.Index("IX_RmsRecordParticipants_Department_User").OnTable("RmsRecordParticipants")
					.OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending();
			}

			if (!Schema.Table("RmsRecordUnitResponses").Exists())
			{
				Create.Table("RmsRecordUnitResponses")
					.WithColumn("RmsRecordUnitResponseId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("UnitId").AsInt32().NotNullable()
					.WithColumn("UnitNameSnapshot").AsString(200).Nullable()
					.WithColumn("UnitTypeSnapshot").AsString(200).Nullable()
					.WithColumn("StationGroupIdSnapshot").AsInt32().Nullable()
					.WithColumn("Dispatched").AsDateTime2().Nullable()
					.WithColumn("Enroute").AsDateTime2().Nullable()
					.WithColumn("OnScene").AsDateTime2().Nullable()
					.WithColumn("Released").AsDateTime2().Nullable()
					.WithColumn("InQuarters").AsDateTime2().Nullable()
					.WithColumn("TimesSourceKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PrefillJson").AsString(int.MaxValue).Nullable()
					.WithColumn("Ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsRecordUnitResponses_Department_Record").OnTable("RmsRecordUnitResponses")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending();
				Create.Index("IX_RmsRecordUnitResponses_Department_Unit").OnTable("RmsRecordUnitResponses")
					.OnColumn("DepartmentId").Ascending().OnColumn("UnitId").Ascending();
			}

			if (!Schema.Table("RmsRecordAttachments").Exists())
			{
				Create.Table("RmsRecordAttachments")
					.WithColumn("RmsRecordAttachmentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("FileName").AsString(int.MaxValue).Nullable()
					.WithColumn("ContentType").AsString(200).Nullable()
					.WithColumn("ByteSize").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("Checksum").AsString(128).Nullable()
					.WithColumn("Data").AsBinary(int.MaxValue).Nullable()
					.WithColumn("StorageReference").AsString(500).Nullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("UploadedByUserId").AsString(128).NotNullable()
					.WithColumn("UploadedOn").AsDateTime2().NotNullable()
					.WithColumn("ScanState").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("MetadataStripped").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsRecordAttachments_Department_Record").OnTable("RmsRecordAttachments")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("RmsRecordAttachments").Exists())
				Delete.Table("RmsRecordAttachments");

			if (Schema.Table("RmsRecordUnitResponses").Exists())
				Delete.Table("RmsRecordUnitResponses");

			if (Schema.Table("RmsRecordParticipants").Exists())
				Delete.Table("RmsRecordParticipants");
		}
	}
}
