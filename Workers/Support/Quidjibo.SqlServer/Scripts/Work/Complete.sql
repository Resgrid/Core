UPDATE wrk 
SET [ExpireOn] = NULL, 
    [VisibleOn] = Null, 
    [Status] = @Complete 
FROM  [Quidjibo].[Work] wrk WITH (ROWLOCK, READPAST, UPDLOCK) 
WHERE [Id] = @Id