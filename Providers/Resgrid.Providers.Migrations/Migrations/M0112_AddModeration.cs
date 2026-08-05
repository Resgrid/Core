using FluentMigrator;
using System;
using System.Data;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(112, TransactionBehavior.None)]
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

			}

			if (!Schema.Table("ModerationRequests").Index("UX_ModerationRequests_Department_Item").Exists())
				Create.Index("UX_ModerationRequests_Department_Item")
					.OnTable("ModerationRequests")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("ItemType").Ascending()
					.OnColumn("ItemId").Ascending()
					.WithOptions().Unique();

			if (!Schema.Table("ModerationRequests").Index("IX_ModerationRequests_Department_Status_ModifiedOn").Exists())
				Create.Index("IX_ModerationRequests_Department_Status_ModifiedOn")
					.OnTable("ModerationRequests")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("Status").Ascending()
					.OnColumn("ModifiedOn").Descending();

			if (!Schema.Table("ModerationRequests").Index("IX_ModerationRequests_Department_Author").Exists())
				Create.Index("IX_ModerationRequests_Department_Author")
					.OnTable("ModerationRequests")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("ContentAuthorUserId").Ascending();

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

			}

			if (!Schema.Table("ModerationReports").Index("UX_ModerationReports_Request_Reporter").Exists())
				Create.Index("UX_ModerationReports_Request_Reporter")
					.OnTable("ModerationReports")
					.OnColumn("ModerationRequestId").Ascending()
					.OnColumn("ReportedByUserId").Ascending()
					.WithOptions().Unique();

			if (!Schema.Table("ModerationReports").Index("IX_ModerationReports_Department_Group").Exists())
				Create.Index("IX_ModerationReports_Department_Group")
					.OnTable("ModerationReports")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("ReporterGroupId").Ascending();

			if (!Schema.Table("ModerationReports").Index("IX_ModerationReports_Department_Reporter").Exists())
				Create.Index("IX_ModerationReports_Department_Reporter")
					.OnTable("ModerationReports")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("ReportedByUserId").Ascending();

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

			}

			if (!Schema.Table("ModerationActions").Index("IX_ModerationActions_Request_PerformedOn").Exists())
				Create.Index("IX_ModerationActions_Request_PerformedOn")
					.OnTable("ModerationActions")
					.OnColumn("ModerationRequestId").Ascending()
					.OnColumn("PerformedOn").Ascending();

			if (!Schema.Table("ModerationReports").Constraint("FK_ModerationReports_ModerationRequests").Exists())
			{
				Create.ForeignKey("FK_ModerationReports_ModerationRequests")
					.FromTable("ModerationReports").ForeignColumn("ModerationRequestId")
					.ToTable("ModerationRequests").PrimaryColumn("ModerationRequestId");
			}

			if (!Schema.Table("ModerationActions").Constraint("FK_ModerationActions_ModerationRequests").Exists())
			{
				Create.ForeignKey("FK_ModerationActions_ModerationRequests")
					.FromTable("ModerationActions").ForeignColumn("ModerationRequestId")
					.ToTable("ModerationRequests").PrimaryColumn("ModerationRequestId");
			}

			Execute.WithConnection((connection, _) =>
			{
				using var command = connection.CreateCommand();
				command.CommandText = @"SELECT TOP (1) 1
FROM ModerationRequests r
WHERE NOT EXISTS
(
    SELECT 1
    FROM ModerationActions a
    WHERE a.ModerationRequestId = r.ModerationRequestId
      AND a.ActorRole = 'LegacyImport'
);";

				if (command.ExecuteScalar() == null)
					ImportLegacyFlags(connection);
			});
		}

		private const int LegacyImportBatchSize = 500;

		private static void ImportLegacyFlags(IDbConnection connection)
		{
			ExecuteBatches(connection, @"
DECLARE @Batch TABLE (ChatMessageId int NOT NULL PRIMARY KEY);

INSERT INTO @Batch (ChatMessageId)
SELECT DISTINCT TOP (@BatchSize) f.ChatMessageId
FROM ChatMessageFlags f
WHERE f.ChatMessageId > @LastId
ORDER BY f.ChatMessageId;

;WITH LegacyRequests AS
(
    SELECT CONVERT(varchar(128), MIN(f.ChatMessageFlagId)) AS ModerationRequestId,
           f.DepartmentId, CONVERT(varchar(128), f.ChatMessageId) AS ItemId,
           MIN(f.ChatChannelId) AS ChatChannelId, MIN(m.SenderUserId) AS ContentAuthorUserId,
           MIN(m.SenderUnitId) AS ContentAuthorUnitId, MIN(m.SentOn) AS ContentCreatedOn,
           COALESCE(MIN(m.Body), (SELECT TOP 1 e.PriorBody FROM ChatMessageEdits e
                                  WHERE e.ChatMessageId = f.ChatMessageId ORDER BY e.EditedOn DESC)) AS OriginalText,
           (SELECT TOP 1 ca.FileName FROM ChatAttachments ca
            WHERE ca.ChatMessageId = f.ChatMessageId ORDER BY ca.UploadedOn) AS OriginalFileName,
           (SELECT TOP 1 ca.ContentType FROM ChatAttachments ca
            WHERE ca.ChatMessageId = f.ChatMessageId ORDER BY ca.UploadedOn) AS OriginalContentType,
           (SELECT TOP 1 ca.Data FROM ChatAttachments ca
            WHERE ca.ChatMessageId = f.ChatMessageId ORDER BY ca.UploadedOn) AS OriginalContent,
           MIN(m.MetadataJson) AS OriginalMetadataJson,
           CASE WHEN SUM(CASE WHEN f.Status = 0 THEN 1 ELSE 0 END) > 0 THEN 0 ELSE 1 END AS Status,
           CASE WHEN SUM(CASE WHEN f.Status = 0 THEN 1 ELSE 0 END) > 0 THEN 0
                WHEN SUM(CASE WHEN f.Status = 3 THEN 1 ELSE 0 END) > 0 THEN 2 ELSE 1 END AS Disposition,
           MIN(f.FlaggedOn) AS CreatedOn, MAX(COALESCE(f.ReviewedOn, f.FlaggedOn)) AS ModifiedOn,
           MAX(f.ReviewedByUserId) AS CompletedByUserId, MAX(f.ReviewedOn) AS CompletedOn,
           MAX(f.ResolutionNote) AS AdminNote
    FROM ChatMessageFlags f
    INNER JOIN @Batch b ON b.ChatMessageId = f.ChatMessageId
    LEFT JOIN ChatMessages m ON m.ChatMessageId = f.ChatMessageId
    GROUP BY f.DepartmentId, f.ChatMessageId
)
INSERT INTO ModerationRequests
    (ModerationRequestId, DepartmentId, ItemType, ItemId, ChatChannelId, ContentAuthorUserId,
     ContentAuthorUnitId, ContentCreatedOn, OriginalText, OriginalFileName, OriginalContentType,
     OriginalContent, OriginalMetadataJson, Status, Disposition, CreatedOn, ModifiedOn,
     CompletedByUserId, CompletedOn, AdminNote)
SELECT l.ModerationRequestId, l.DepartmentId, 0, l.ItemId, l.ChatChannelId,
       l.ContentAuthorUserId, l.ContentAuthorUnitId, l.ContentCreatedOn, l.OriginalText,
       l.OriginalFileName, l.OriginalContentType, l.OriginalContent, l.OriginalMetadataJson,
       l.Status, l.Disposition, l.CreatedOn, l.ModifiedOn, l.CompletedByUserId, l.CompletedOn,
       l.AdminNote
FROM LegacyRequests l
WHERE NOT EXISTS
(
    SELECT 1 FROM ModerationRequests r
    WHERE r.ModerationRequestId = l.ModerationRequestId
);

;WITH LegacyReports AS
(
    SELECT CONVERT(varchar(128), MIN(f.ChatMessageFlagId)) AS ModerationReportId,
           CONVERT(varchar(128), (SELECT MIN(f2.ChatMessageFlagId) FROM ChatMessageFlags f2
                                  WHERE f2.DepartmentId = f.DepartmentId
                                    AND f2.ChatMessageId = f.ChatMessageId)) AS ModerationRequestId,
           f.DepartmentId, f.FlaggedByUserId AS ReportedByUserId,
           (SELECT TOP 1 gm.DepartmentGroupId FROM DepartmentGroupMembers gm
            WHERE gm.DepartmentId = f.DepartmentId AND gm.UserId = f.FlaggedByUserId
            ORDER BY gm.DepartmentGroupMemberId) AS ReporterGroupId,
           MIN(f.Reason) AS Reason, MIN(f.Note) AS Note, MIN(f.FlaggedOn) AS ReportedOn
    FROM ChatMessageFlags f
    INNER JOIN @Batch b ON b.ChatMessageId = f.ChatMessageId
    WHERE f.FlaggedByUserId IS NOT NULL
    GROUP BY f.DepartmentId, f.ChatMessageId, f.FlaggedByUserId
)
INSERT INTO ModerationReports
    (ModerationReportId, ModerationRequestId, DepartmentId, ReportedByUserId, ReporterGroupId,
     Reason, Note, ReportedOn)
SELECT l.ModerationReportId, l.ModerationRequestId, l.DepartmentId, l.ReportedByUserId,
       l.ReporterGroupId, l.Reason, l.Note, l.ReportedOn
FROM LegacyReports l
WHERE NOT EXISTS
(
    SELECT 1 FROM ModerationReports r
    WHERE r.ModerationReportId = l.ModerationReportId
       OR (r.ModerationRequestId = l.ModerationRequestId
           AND r.ReportedByUserId = l.ReportedByUserId)
);

INSERT INTO ModerationActions
    (ModerationActionId, ModerationRequestId, DepartmentId, ActionType, PerformedByUserId,
     PerformedOn, NewStatus, ActorRole, ServerName, DetailsJson, EvidenceText, EvidenceContent,
     EvidenceMetadataJson)
SELECT r.ModerationRequestId, r.ModerationRequestId, r.DepartmentId, 0,
       (SELECT TOP 1 rp.ReportedByUserId FROM ModerationReports rp
        WHERE rp.ModerationRequestId = r.ModerationRequestId ORDER BY rp.ReportedOn),
       r.CreatedOn, r.Status, 'LegacyImport', HOST_NAME(), '{""imported"":true}', r.OriginalText,
       r.OriginalContent, r.OriginalMetadataJson
FROM ModerationRequests r
INNER JOIN @Batch b ON r.ItemType = 0
    AND r.ItemId = CONVERT(varchar(128), b.ChatMessageId)
WHERE NOT EXISTS
(
    SELECT 1 FROM ModerationActions a
    WHERE a.ModerationActionId = r.ModerationRequestId
);

SELECT MAX(CONVERT(bigint, ChatMessageId)) FROM @Batch;");

			ExecuteBatches(connection, @"
DECLARE @Batch TABLE (CallNoteId int NOT NULL PRIMARY KEY);

INSERT INTO @Batch (CallNoteId)
SELECT TOP (@BatchSize) n.CallNoteId
FROM CallNotes n
WHERE n.IsFlagged = 1 AND n.CallNoteId > @LastId
ORDER BY n.CallNoteId;

INSERT INTO ModerationRequests
    (ModerationRequestId, DepartmentId, ItemType, ItemId, CallId, ContentAuthorUserId,
     ContentCreatedOn, OriginalText, OriginalMetadataJson, Status, Disposition, CreatedOn, ModifiedOn)
SELECT CONCAT('callnote-', n.CallNoteId), c.DepartmentId, 2, CONVERT(varchar(32), n.CallNoteId),
       n.CallId, n.UserId, n.Timestamp, n.Note,
       (SELECT n.Source AS [source], n.Latitude AS [latitude], n.Longitude AS [longitude]
        FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),
       0, 0, COALESCE(n.FlaggedOn, n.Timestamp), COALESCE(n.FlaggedOn, n.Timestamp)
FROM CallNotes n
INNER JOIN @Batch b ON b.CallNoteId = n.CallNoteId
INNER JOIN Calls c ON c.CallId = n.CallId
WHERE NOT EXISTS
(
    SELECT 1 FROM ModerationRequests r
    WHERE r.ModerationRequestId = CONCAT('callnote-', n.CallNoteId)
);

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
INNER JOIN @Batch b ON b.CallNoteId = n.CallNoteId
INNER JOIN Calls c ON c.CallId = n.CallId
WHERE n.FlaggedByUserId IS NOT NULL
  AND NOT EXISTS
  (
      SELECT 1 FROM ModerationReports r
      WHERE r.ModerationReportId = CONCAT('callnote-', n.CallNoteId)
         OR (r.ModerationRequestId = CONCAT('callnote-', n.CallNoteId)
             AND r.ReportedByUserId = n.FlaggedByUserId)
  );

INSERT INTO ModerationActions
    (ModerationActionId, ModerationRequestId, DepartmentId, ActionType, PerformedByUserId,
     PerformedOn, NewStatus, ActorRole, ServerName, DetailsJson, EvidenceText, EvidenceContent,
     EvidenceMetadataJson)
SELECT r.ModerationRequestId, r.ModerationRequestId, r.DepartmentId, 0,
       (SELECT TOP 1 rp.ReportedByUserId FROM ModerationReports rp
        WHERE rp.ModerationRequestId = r.ModerationRequestId ORDER BY rp.ReportedOn),
       r.CreatedOn, r.Status, 'LegacyImport', HOST_NAME(), '{""imported"":true}', r.OriginalText,
       r.OriginalContent, r.OriginalMetadataJson
FROM ModerationRequests r
INNER JOIN @Batch b ON r.ItemType = 2
    AND r.ItemId = CONVERT(varchar(128), b.CallNoteId)
WHERE NOT EXISTS
(
    SELECT 1 FROM ModerationActions a
    WHERE a.ModerationActionId = r.ModerationRequestId
);

SELECT MAX(CONVERT(bigint, CallNoteId)) FROM @Batch;");

			ExecuteBatches(connection, @"
DECLARE @Batch TABLE (CallAttachmentId int NOT NULL PRIMARY KEY);

INSERT INTO @Batch (CallAttachmentId)
SELECT TOP (@BatchSize) a.CallAttachmentId
FROM CallAttachments a
WHERE a.IsFlagged = 1 AND a.CallAttachmentType = 2 AND a.CallAttachmentId > @LastId
ORDER BY a.CallAttachmentId;

INSERT INTO ModerationRequests
    (ModerationRequestId, DepartmentId, ItemType, ItemId, CallId, ContentAuthorUserId,
     ContentCreatedOn, OriginalFileName, OriginalContentType, OriginalContent, OriginalMetadataJson,
     Status, Disposition, CreatedOn, ModifiedOn)
SELECT CONCAT('callimage-', a.CallAttachmentId), c.DepartmentId, 3,
       CONVERT(varchar(32), a.CallAttachmentId), a.CallId, a.UserId, a.Timestamp, a.FileName,
       'image/jpeg', a.Data,
       (SELECT COALESCE(a.Name, '') AS [name], a.Size AS [size]
        FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),
       0, 0, COALESCE(a.FlaggedOn, a.Timestamp, GETUTCDATE()),
       COALESCE(a.FlaggedOn, a.Timestamp, GETUTCDATE())
FROM CallAttachments a
INNER JOIN @Batch b ON b.CallAttachmentId = a.CallAttachmentId
INNER JOIN Calls c ON c.CallId = a.CallId
WHERE NOT EXISTS
(
    SELECT 1 FROM ModerationRequests r
    WHERE r.ModerationRequestId = CONCAT('callimage-', a.CallAttachmentId)
);

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
INNER JOIN @Batch b ON b.CallAttachmentId = a.CallAttachmentId
INNER JOIN Calls c ON c.CallId = a.CallId
WHERE a.FlaggedByUserId IS NOT NULL
  AND NOT EXISTS
  (
      SELECT 1 FROM ModerationReports r
      WHERE r.ModerationReportId = CONCAT('callimage-', a.CallAttachmentId)
         OR (r.ModerationRequestId = CONCAT('callimage-', a.CallAttachmentId)
             AND r.ReportedByUserId = a.FlaggedByUserId)
  );

INSERT INTO ModerationActions
    (ModerationActionId, ModerationRequestId, DepartmentId, ActionType, PerformedByUserId,
     PerformedOn, NewStatus, ActorRole, ServerName, DetailsJson, EvidenceText, EvidenceContent,
     EvidenceMetadataJson)
SELECT r.ModerationRequestId, r.ModerationRequestId, r.DepartmentId, 0,
       (SELECT TOP 1 rp.ReportedByUserId FROM ModerationReports rp
        WHERE rp.ModerationRequestId = r.ModerationRequestId ORDER BY rp.ReportedOn),
       r.CreatedOn, r.Status, 'LegacyImport', HOST_NAME(), '{""imported"":true}', r.OriginalText,
       r.OriginalContent, r.OriginalMetadataJson
FROM ModerationRequests r
INNER JOIN @Batch b ON r.ItemType = 3
    AND r.ItemId = CONVERT(varchar(128), b.CallAttachmentId)
WHERE NOT EXISTS
(
    SELECT 1 FROM ModerationActions a
    WHERE a.ModerationActionId = r.ModerationRequestId
);

SELECT MAX(CONVERT(bigint, CallAttachmentId)) FROM @Batch;");
		}

		private static void ExecuteBatches(IDbConnection connection, string commandText)
		{
			long lastId = 0;

			while (true)
			{
				using var transaction = connection.BeginTransaction();

				try
				{
					using var command = connection.CreateCommand();
					command.Transaction = transaction;
					command.CommandText = commandText;
					AddParameter(command, "@BatchSize", DbType.Int32, LegacyImportBatchSize);
					AddParameter(command, "@LastId", DbType.Int64, lastId);

					var result = command.ExecuteScalar();
					if (result == null || result == DBNull.Value)
					{
						transaction.Commit();
						return;
					}

					var nextId = Convert.ToInt64(result);
					if (nextId <= lastId)
						throw new InvalidOperationException("Legacy moderation import did not advance its batch key.");

					transaction.Commit();
					lastId = nextId;
				}
				catch
				{
					transaction.Rollback();
					throw;
				}
			}
		}

		private static void AddParameter(IDbCommand command, string name, DbType type, object value)
		{
			var parameter = command.CreateParameter();
			parameter.ParameterName = name;
			parameter.DbType = type;
			parameter.Value = value;
			command.Parameters.Add(parameter);
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
