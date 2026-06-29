UPDATE schdl
SET [EnqueuedOn] = @EnqueuedOn, 
    [EnqueueOn] = @EnqueueOn 
FROM  [Quidjibo].[Schedule] schdl WITH (ROWLOCK, UPDLOCK)
WHERE [Id] = @Id