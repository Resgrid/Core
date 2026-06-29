UPDATE wrk 
SET [ExpireOn] = NULL, 
    [VisibleOn] = Null, 
    [Status] = @Complete 
FROM  [Quidjibo].[Work] wrk WITH (ROWLOCK, UPDLOCK)
WHERE [Id] = @Id