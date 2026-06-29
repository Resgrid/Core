UPDATE schdl
SET [EnqueuedOn] = @EnqueuedOn, 
    [EnqueueOn] = @EnqueueOn 
FROM  [Quidjibo].[Schedule] schdl WITH (ROWLOCK, READPAST, UPDLOCK) 
WHERE [Id] = @Id