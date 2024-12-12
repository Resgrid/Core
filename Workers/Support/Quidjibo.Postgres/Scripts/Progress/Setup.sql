DO $$ BEGIN
CREATE TABLE IF NOT EXISTS public.progress(
  id uuid NOT NULL,
  sequence bigserial,
  workid uuid,
  correlationid uuid NOT NULL,
  name character varying(250) NOT NULL,
  queue character varying(250) NOT NULL,
  note character varying(250) NOT NULL,
  value integer NOT NULL,
  recordedon timestamp without time zone
);

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'progress' and constraint_type = 'PRIMARY KEY') THEN
ALTER TABLE public.progress 
  ADD PRIMARY KEY (id);
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'progress' and constraint_type = 'UNIQUE') THEN
ALTER TABLE public.progress 
  ADD CONSTRAINT progress_sequence_key UNIQUE(sequence);
END IF;
END $$;
