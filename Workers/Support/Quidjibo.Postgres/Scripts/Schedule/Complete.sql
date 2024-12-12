UPDATE public.schedule
SET enqueuedon = @EnqueuedOn, 
    enqueueon = @EnqueueOn 
WHERE id = @Id
