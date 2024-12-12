WITH schdl AS 
(
    SELECT public.schedule.* 
    FROM   public.schedule 
    WHERE  visibleon < @ReceiveOn
			 AND enqueueon < @ReceiveOn
			 AND queue IN (@Queue1)
    ORDER BY id
    LIMIT @Take
)
UPDATE public.schedule
SET visibleon = @VisibleOn
FROM schdl
WHERE public.schedule.id = schdl.id
RETURNING *;
