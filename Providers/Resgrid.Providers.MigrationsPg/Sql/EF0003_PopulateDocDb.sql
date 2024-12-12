DO $$ BEGIN
--
-- CREATE TABLE IF NOT EXISTS "public"."unitlocations"
--
CREATE TABLE IF NOT EXISTS public.unitlocations(
  id serial,
  departmentid integer,
  unitid integer NOT NULL,
  oid text,
  "timestamp" timestamp without time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'::text),
  data jsonb NOT NULL
);

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'unitlocations' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public.unitlocations 
    ADD PRIMARY KEY (id);
END IF;


--
-- CREATE TABLE IF NOT EXISTS "public"."personnellocations"
--
CREATE TABLE IF NOT EXISTS public.personnellocations(
  id serial,
  departmentid integer,
  userid text NOT NULL,
  oid text,
  "timestamp" timestamp without time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'::text),
  data jsonb NOT NULL
);

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'personnellocations' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public.personnellocations 
    ADD PRIMARY KEY (id);
END IF;


--
-- CREATE TABLE IF NOT EXISTS "public"."maplayers"
--
CREATE TABLE IF NOT EXISTS public.maplayers(
  id serial,
  departmentid integer,
  oid text
  data jsonb NOT NULL
);

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'maplayers' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public.maplayers 
    ADD PRIMARY KEY (id);
END IF;
END $$;
