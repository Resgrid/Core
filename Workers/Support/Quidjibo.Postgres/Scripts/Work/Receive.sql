DELETE FROM public.work
WHERE createdon < @DeleteOn
       AND (expireon IS NULL OR expireon < @DeleteOn);

WITH wrk AS 
(
    SELECT public.work.* 
    FROM   public.work 
    WHERE  Status < 2
             AND attempts < @MaxAttempts 
             AND visibleon < @ReceiveOn
             AND queue IN (@Queue1)
    ORDER BY id
    LIMIT @Take
)
UPDATE public.work 
SET worker = @Worker,
    visibleon = @VisibleOn,
    status = @InFlight, 
    attempts = public.work.attempts + 1
FROM wrk
WHERE public.work.id = wrk.id
RETURNING *;
