﻿INSERT INTO public.progress 
(
    id,
    workid,
    correlationid,
    name,
    queue,
    note,
    value,
    recordedon
)
VALUES
(
    @Id,
    @WorkId,
    @CorrelationId,
    @Name, 
    @Queue, 
    @Note, 
    @Value, 
    @RecordedOn
);