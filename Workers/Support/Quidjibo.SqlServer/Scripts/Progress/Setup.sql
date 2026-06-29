IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[Quidjibo].[Progress]') AND type in (N'U'))
BEGIN
CREATE TABLE [Quidjibo].[Progress](
    [Id] [UNIQUEIDENTIFIER] NOT NULL,
    [Sequence] [bigint] IDENTITY(1,1) NOT NULL,
    [WorkId] [uniqueidentifier] NULL,
    [CorrelationId] [uniqueidentifier] NOT NULL,
    [Name] [nvarchar](250) NOT NULL,
    [Queue] [nvarchar](250) NOT NULL,
    [Note] [nvarchar](250) NOT NULL,
    [Value] [int] NOT NULL,
    [RecordedOn] [datetime] NULL,
CONSTRAINT [PK_Progress] PRIMARY KEY NONCLUSTERED 
(
    [Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY],
 CONSTRAINT [IX_Progress_Sequence] UNIQUE CLUSTERED 
(
    [Sequence] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[Quidjibo].[Progress]') AND name = N'IX_Progress_CorrelationId')
CREATE NONCLUSTERED INDEX [IX_Progress_CorrelationId] ON [Quidjibo].[Progress]
(
	[CorrelationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
