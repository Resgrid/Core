SELECT id, workid, correlationid, name, queue, note, value, recordedon
FROM public.progress
WHERE correlationid = @correlationid
