DO $$ BEGIN
CREATE TABLE IF NOT EXISTS public.work(
  id uuid NOT NULL,
  sequence bigserial,
  scheduleid uuid,
  correlationid uuid NOT NULL,
  name character varying(250) NOT NULL,
  worker character varying(250),
  queue character varying(250) NOT NULL,
  status integer NOT NULL,
  attempts integer NOT NULL,
  createdon timestamp without time zone,
  expireon timestamp without time zone,
  visibleon timestamp without time zone,
  payload bytea NOT NULL
);

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'work' and constraint_type = 'PRIMARY KEY') THEN
ALTER TABLE public.work 
  ADD PRIMARY KEY (id);
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'work' and constraint_type = 'UNIQUE') THEN
ALTER TABLE public.work 
  ADD CONSTRAINT work_sequence_key UNIQUE(sequence);
END IF;
END $$;
