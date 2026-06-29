WITH schdl AS 
(
    SELECT TOP(@Take) [Quidjibo].[Schedule].* 
    FROM   [Quidjibo].[Schedule] WITH (ROWLOCK, READPAST, UPDLOCK) 
    WHERE  [VisibleOn] < @ReceiveOn
			 AND [EnqueueOn] < @ReceiveOn
			 AND [Queue] IN (@Queue1)
    ORDER BY [Sequence]
)
UPDATE schdl 
SET [VisibleOn] = @VisibleOn
OUTPUT inserted.*