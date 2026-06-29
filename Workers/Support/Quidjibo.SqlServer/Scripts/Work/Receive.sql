DELETE TOP(100) FROM [Quidjibo].[Work] WITH (ROWLOCK, READPAST, UPDLOCK)
WHERE [CreatedOn] < @DeleteOn 
       AND ([ExpireOn] IS NULL OR [ExpireOn] < @DeleteOn);

WITH wrk AS 
(
    SELECT TOP (@Take) [Quidjibo].[Work].* 
    FROM   [Quidjibo].[Work] WITH (ROWLOCK, READPAST, UPDLOCK) 
    WHERE  [Status] < 2
             AND [Attempts] < @MaxAttempts 
             AND [VisibleOn] < @ReceiveOn
             AND [Queue] IN (@Queue1)
    ORDER BY [Sequence]
)
UPDATE wrk 
SET [Worker] = @Worker,
    [VisibleOn] = @VisibleOn,
    [Status] = @InFlight, 
    [Attempts] = [Attempts] + 1
OUTPUT inserted.*