UPDATE wrk 
SET visibleon = @VisibleOn,
    status = @Faulted
FROM public.work wrk
WHERE id = @Id
