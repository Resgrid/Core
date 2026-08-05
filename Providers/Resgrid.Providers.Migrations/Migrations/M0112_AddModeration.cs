using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(112)]
	public class M0112_AddModeration : Migration
	{
		public override void Up()
		{
			if (Schema.Table("ChatMessages").Exists() && !Schema.Table("ChatMessages").Column("IsModerated").Exists())
			{
				Alter.Table("ChatMessages")
					.AddColumn("IsModerated").AsBoolean().NotNullable().WithDefaultValue(false);
			}

			if (!Schema.Table("ModerationRequests").Exists())
			{
				Create.Table("ModerationRequests")
					.WithColumn("ModerationRequestId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ItemType").AsInt32().NotNullable()
					.WithColumn("ItemId").AsString(128).NotNullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("ChatChannelId").AsString(128).Nullable()
					.WithColumn("ContentAuthorUserId").AsString(450).Nullable()
					.WithColumn("ContentAuthorUnitId").AsInt32().Nullable()
					.WithColumn("ContentCreatedOn").AsDateTime2().Nullable()
					.WithColumn("OriginalSubject").AsString(int.MaxValue).Nullable()
					.WithColumn("OriginalText").AsString(int.MaxValue).Nullable()
					.WithColumn("OriginalFileName").AsString(int.MaxValue).Nullable()
					.WithColumn("OriginalContentType").AsString(256).Nullable()
					.WithColumn("OriginalContent").AsBinary(int.MaxValue).Nullable()
					.WithColumn("OriginalMetadataJson").AsString(int.MaxValue).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Disposition").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("CompletedByUserId").AsString(450).Nullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable()
					.WithColumn("AdminNote").AsString(int.MaxValue).Nullable();

				Create.Index("UX_ModerationRequests_Department_Item")
					.OnTable("ModerationRequests")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("ItemType").Ascending()
					.OnColumn("ItemId").Ascending()
					.WithOptions().Unique();

				Create.Index("IX_ModerationRequests_Department_Status_ModifiedOn")
					.OnTable("ModerationRequests")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("Status").Ascending()
					.OnColumn("ModifiedOn").Descending();

				Create.Index("IX_ModerationRequests_Department_Author")
					.OnTable("ModerationRequests")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("ContentAuthorUserId").Ascending();
			}

			if (!Schema.Table("ModerationReports").Exists())
			{
				Create.Table("ModerationReports")
					.WithColumn("ModerationReportId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("ModerationRequestId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ReportedByUserId").AsString(450).NotNullable()
					.WithColumn("ReporterGroupId").AsInt32().Nullable()
					.WithColumn("Reason").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Note").AsString(int.MaxValue).Nullable()
					.WithColumn("ReportedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

				Create.Index("UX_ModerationReports_Request_Reporter")
					.OnTable("ModerationReports")
					.OnColumn("ModerationRequestId").Ascending()
					.OnColumn("ReportedByUserId").Ascending()
					.WithOptions().Unique();

				Create.Index("IX_ModerationReports_Department_Group")
					.OnTable("ModerationReports")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("ReporterGroupId").Ascending();

				Create.Index("IX_ModerationReports_Department_Reporter")
					.OnTable("ModerationReports")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("ReportedByUserId").Ascending();
			}

			if (!Schema.Table("ModerationActions").Exists())
			{
				Create.Table("ModerationActions")
					.WithColumn("ModerationActionId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("ModerationRequestId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ActionType").AsInt32().NotNullable()
					.WithColumn("PerformedByUserId").AsString(450).Nullable()
					.WithColumn("PerformedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("Note").AsString(int.MaxValue).Nullable()
					.WithColumn("PreviousStatus").AsInt32().Nullable()
					.WithColumn("NewStatus").AsInt32().Nullable()
					.WithColumn("ActorRole").AsString(128).Nullable()
					.WithColumn("IpAddress").AsString(128).Nullable()
					.WithColumn("UserAgent").AsString(int.MaxValue).Nullable()
					.WithColumn("TraceId").AsString(256).Nullable()
					.WithColumn("ServerName").AsString(256).Nullable()
					.WithColumn("DetailsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("EvidenceText").AsString(int.MaxValue).Nullable()
					.WithColumn("EvidenceContent").AsBinary(int.MaxValue).Nullable()
					.WithColumn("EvidenceMetadataJson").AsString(int.MaxValue).Nullable();

				Create.Index("IX_ModerationActions_Request_PerformedOn")
					.OnTable("ModerationActions")
					.OnColumn("ModerationRequestId").Ascending()
					.OnColumn("PerformedOn").Ascending();
			}

			Create.ForeignKey("FK_ModerationReports_ModerationRequests")
				.FromTable("ModerationReports").ForeignColumn("ModerationRequestId")
				.ToTable("ModerationRequests").PrimaryColumn("ModerationRequestId");

			Create.ForeignKey("FK_ModerationActions_ModerationRequests")
				.FromTable("ModerationActions").ForeignColumn("ModerationRequestId")
				.ToTable("ModerationRequests").PrimaryColumn("ModerationRequestId");

			ImportLegacyFlags();
		}

		private void ImportLegacyFlags()
		{
			Execute.Sql(@"
INSERT INTO ModerationRequests
    (ModerationRequestId, DepartmentId, ItemType, ItemId, ChatChannelId, ContentAuthorUserId,
     ContentAuthorUnitId, ContentCreatedOn, OriginalText, OriginalFileName, OriginalContentType,
     OriginalContent, OriginalMetadataJson, Status, Disposition, CreatedOn, ModifiedOn,
     CompletedByUserId, CompletedOn, AdminNote)
SELECT MIN(f.ChatMessageFlagId), f.DepartmentId, 0, f.ChatMessageId, MIN(f.ChatChannelId),
       MIN(m.SenderUserId), MIN(m.SenderUnitId), MIN(m.SentOn),
       COALESCE(MIN(m.Body), (SELECT TOP 1 e.PriorBody FROM ChatMessageEdits e
                              WHERE e.ChatMessageId = f.ChatMessageId ORDER BY e.EditedOn DESC)),
       (SELECT TOP 1 ca.FileName FROM ChatAttachments ca
        WHERE ca.ChatMessageId = f.ChatMessageId ORDER BY ca.UploadedOn),
       (SELECT TOP 1 ca.ContentType FROM ChatAttachments ca
        WHERE ca.ChatMessageId = f.ChatMessageId ORDER BY ca.UploadedOn),
       (SELECT TOP 1 ca.Data FROM ChatAttachments ca
        WHERE ca.ChatMessageId = f.ChatMessageId ORDER BY ca.UploadedOn),
       MIN(m.MetadataJson),
       CASE WHEN SUM(CASE WHEN f.Status = 0 THEN 1 ELSE 0 END) > 0 THEN 0 ELSE 1 END,
       CASE WHEN SUM(CASE WHEN f.Status = 0 THEN 1 ELSE 0 END) > 0 THEN 0
            WHEN SUM(CASE WHEN f.Status = 3 THEN 1 ELSE 0 END) > 0 THEN 2 ELSE 1 END,
       MIN(f.FlaggedOn), MAX(COALESCE(f.ReviewedOn, f.FlaggedOn)), MAX(f.ReviewedByUserId),
       MAX(f.ReviewedOn), MAX(f.ResolutionNote)
FROM ChatMessageFlags f
LEFT JOIN ChatMessages m ON m.ChatMessageId = f.ChatMessageId
GROUP BY f.DepartmentId, f.ChatMessageId;

INSERT INTO ModerationReports
    (ModerationReportId, ModerationRequestId, DepartmentId, ReportedByUserId, ReporterGroupId,
     Reason, Note, ReportedOn)
SELECT MIN(f.ChatMessageFlagId),
       (SELECT MIN(f2.ChatMessageFlagId) FROM ChatMessageFlags f2
        WHERE f2.DepartmentId = f.DepartmentId AND f2.ChatMessageId = f.ChatMessageId),
       f.DepartmentId, f.FlaggedByUserId,
       (SELECT TOP 1 gm.DepartmentGroupId FROM DepartmentGroupMembers gm
        WHERE gm.DepartmentId = f.DepartmentId AND gm.UserId = f.FlaggedByUserId
        ORDER BY gm.DepartmentGroupMemberId),
       MIN(f.Reason), MIN(f.Note), MIN(f.FlaggedOn)
FROM ChatMessageFlags f
WHERE f.FlaggedByUserId IS NOT NULL
GROUP BY f.DepartmentId, f.ChatMessageId, f.FlaggedByUserId;

INSERT INTO ModerationRequests
    (ModerationRequestId, DepartmentId, ItemType, ItemId, CallId, ContentAuthorUserId,
     ContentCreatedOn, OriginalText, OriginalMetadataJson, Status, Disposition, CreatedOn, ModifiedOn)
SELECT CONCAT('callnote-', n.CallNoteId), c.DepartmentId, 2, CONVERT(varchar(32), n.CallNoteId),
       n.CallId, n.UserId, n.Timestamp, n.Note,
       CONCAT('{""source"":', n.Source, ',""latitude"":', COALESCE(CONVERT(varchar(64), n.Latitude), 'null'),
              ',""longitude"":', COALESCE(CONVERT(varchar(64), n.Longitude), 'null'), '}'),
       0, 0, COALESCE(n.FlaggedOn, n.Timestamp), COALESCE(n.FlaggedOn, n.Timestamp)
FROM CallNotes n
INNER JOIN Calls c ON c.CallId = n.CallId
WHERE n.IsFlagged = 1;

INSERT INTO ModerationReports
    (ModerationReportId, ModerationRequestId, DepartmentId, ReportedByUserId, ReporterGroupId,
     Reason, Note, ReportedOn)
SELECT CONCAT('callnote-', n.CallNoteId), CONCAT('callnote-', n.CallNoteId), c.DepartmentId,
       n.FlaggedByUserId,
       (SELECT TOP 1 gm.DepartmentGroupId FROM DepartmentGroupMembers gm
        WHERE gm.DepartmentId = c.DepartmentId AND gm.UserId = n.FlaggedByUserId
        ORDER BY gm.DepartmentGroupMemberId),
       0, n.FlaggedReason, COALESCE(n.FlaggedOn, n.Timestamp)
FROM CallNotes n
INNER JOIN Calls c ON c.CallId = n.CallId
WHERE n.IsFlagged = 1 AND n.FlaggedByUserId IS NOT NULL;

INSERT INTO ModerationRequests
    (ModerationRequestId, DepartmentId, ItemType, ItemId, CallId, ContentAuthorUserId,
     ContentCreatedOn, OriginalFileName, OriginalContentType, OriginalContent, OriginalMetadataJson,
     Status, Disposition, CreatedOn, ModifiedOn)
SELECT CONCAT('callimage-', a.CallAttachmentId), c.DepartmentId, 3,
       CONVERT(varchar(32), a.CallAttachmentId), a.CallId, a.UserId, a.Timestamp, a.FileName,
       'image/jpeg', a.Data,
       CONCAT('{""name"":""', COALESCE(REPLACE(a.Name, '""', '\""'), ''),
              '"",""size"":', COALESCE(CONVERT(varchar(32), a.Size), 'null'), '}'),
       0, 0, COALESCE(a.FlaggedOn, a.Timestamp, GETUTCDATE()),
       COALESCE(a.FlaggedOn, a.Timestamp, GETUTCDATE())
FROM CallAttachments a
INNER JOIN Calls c ON c.CallId = a.CallId
WHERE a.IsFlagged = 1 AND a.CallAttachmentType = 2;

INSERT INTO ModerationReports
    (ModerationReportId, ModerationRequestId, DepartmentId, ReportedByUserId, ReporterGroupId,
     Reason, Note, ReportedOn)
SELECT CONCAT('callimage-', a.CallAttachmentId), CONCAT('callimage-', a.CallAttachmentId),
       c.DepartmentId, a.FlaggedByUserId,
       (SELECT TOP 1 gm.DepartmentGroupId FROM DepartmentGroupMembers gm
        WHERE gm.DepartmentId = c.DepartmentId AND gm.UserId = a.FlaggedByUserId
        ORDER BY gm.DepartmentGroupMemberId),
       0, a.FlaggedReason, COALESCE(a.FlaggedOn, a.Timestamp, GETUTCDATE())
FROM CallAttachments a
INNER JOIN Calls c ON c.CallId = a.CallId
WHERE a.IsFlagged = 1 AND a.CallAttachmentType = 2 AND a.FlaggedByUserId IS NOT NULL;

INSERT INTO ModerationActions
    (ModerationActionId, ModerationRequestId, DepartmentId, ActionType, PerformedByUserId,
     PerformedOn, NewStatus, ActorRole, ServerName, DetailsJson, EvidenceText, EvidenceContent,
     EvidenceMetadataJson)
SELECT r.ModerationRequestId, r.ModerationRequestId, r.DepartmentId, 0,
       (SELECT TOP 1 rp.ReportedByUserId FROM ModerationReports rp
        WHERE rp.ModerationRequestId = r.ModerationRequestId ORDER BY rp.ReportedOn),
       r.CreatedOn, r.Status, 'LegacyImport', HOST_NAME(), '{""imported"":true}', r.OriginalText,
       r.OriginalContent, r.OriginalMetadataJson
FROM ModerationRequests r;");
		}

		public override void Down()
		{
			if (Schema.Table("ModerationActions").Exists())
				Delete.Table("ModerationActions");

			if (Schema.Table("ModerationReports").Exists())
				Delete.Table("ModerationReports");

			if (Schema.Table("ModerationRequests").Exists())
				Delete.Table("ModerationRequests");

			if (Schema.Table("ChatMessages").Exists() && Schema.Table("ChatMessages").Column("IsModerated").Exists())
				Delete.Column("IsModerated").FromTable("ChatMessages");
		}
	}
}
