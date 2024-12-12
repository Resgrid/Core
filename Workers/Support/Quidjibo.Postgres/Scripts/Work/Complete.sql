UPDATE public.work 
SET expireon = NULL, 
    visibleon = Null, 
    status = @Complete 
WHERE id = @Id
