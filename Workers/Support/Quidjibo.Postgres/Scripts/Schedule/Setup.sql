DO $$ BEGIN
CREATE TABLE IF NOT EXISTS public.schedule(
  id uuid NOT NULL,
  sequence bigserial,
  name character varying(250) NOT NULL,
  queue character varying(250) NOT NULL,
  cronexpression character varying(50) NOT NULL,
  createdon timestamp without time zone,
  enqueueon timestamp without time zone,
  enqueuedon timestamp without time zone,
  visibleon timestamp without time zone,
  payload bytea NOT NULL
);

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'schedule' and constraint_type = 'PRIMARY KEY') THEN
ALTER TABLE public.schedule 
  ADD PRIMARY KEY (id);
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'schedule' and constraint_type = 'UNIQUE') THEN
ALTER TABLE public.schedule 
  ADD CONSTRAINT schedule_sequence_key UNIQUE(sequence);
END IF;
END $$;
