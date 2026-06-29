UPDATE wrk 
SET [VisibleOn] = @VisibleOn,
    [Status] = @Faulted
FROM [Quidjibo].[Work] wrk WITH (ROWLOCK, READPAST, UPDLOCK) 
WHERE [Id] = @Id