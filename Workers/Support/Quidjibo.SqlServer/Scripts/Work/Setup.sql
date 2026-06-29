IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[Quidjibo].[Work]') AND type in (N'U'))
BEGIN
CREATE TABLE [Quidjibo].[Work](
    [Id] [uniqueidentifier] NOT NULL,
	[Sequence] [bigint] IDENTITY(1,1) NOT NULL,
    [ScheduleId] [uniqueidentifier] NULL,
    [CorrelationId] [uniqueidentifier] NOT NULL,
    [Name] [nvarchar](250) NOT NULL,
	[Worker] [nvarchar](250) NULL,
    [Queue] [nvarchar](250) NOT NULL,
    [Status] [int] NOT NULL,
    [Attempts] [int] NOT NULL,
    [CreatedOn] [datetime] NULL,
    [ExpireOn] [datetime] NULL,
    [VisibleOn] [datetime] NULL,
    [Payload] [varbinary](max) NOT NULL
 CONSTRAINT [PK_Work] PRIMARY KEY NONCLUSTERED 
(
    [Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY],
 CONSTRAINT [IX_Work_Sequence] UNIQUE CLUSTERED 
(
    [Sequence] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[Quidjibo].[Work]') AND name = N'IX_Work_Receive')
CREATE NONCLUSTERED INDEX [IX_Work_Receive] ON [Quidjibo].[Work]
(
	[Queue],
	[Status],
	[Attempts],
	[VisibleOn]
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[Quidjibo].[Work]') AND name = N'IX_Work_Cleanup')
CREATE NONCLUSTERED INDEX [IX_Work_Cleanup] ON [Quidjibo].[Work]
(
	[CreatedOn] ASC,
	[ExpireOn] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
