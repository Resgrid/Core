BEGIN TRANSACTION;


IF OBJECT_ID(N'[AuditEvents]') IS NULL
BEGIN
    CREATE TABLE [AuditEvents]
    (
        [AuditEventId] BIGINT IDENTITY(1,1) NOT NULL,
        [InsertedDate] DATETIME NOT NULL DEFAULT(GETUTCDATE()),
        [LastUpdatedDate] DATETIME NULL,
        [JsonData] NVARCHAR(MAX) NOT NULL,
        [Module] NVARCHAR(200) NOT NULL,
        [EventType] NVARCHAR(100) NOT NULL,
        [Successful] NVARCHAR(5) NOT NULL,
        [Object] NVARCHAR(256) NOT NULL,
        [ObjectId] NVARCHAR(128) NOT NULL,
		[ObjectDepartmentId] NVARCHAR(128) NULL,
        [UserId] NVARCHAR(128) NOT NULL,
		[DepartmentId] NVARCHAR(128) NOT NULL,
        CONSTRAINT PK_AuditEvents PRIMARY KEY (AuditEventId)
    )
END;


COMMIT;
