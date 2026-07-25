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
  eventid text,
  receivedon timestamp without time zone,
  sourcetype integer,
  sourceid text,
  sourcepriority integer NOT NULL DEFAULT 0,
  data jsonb NOT NULL
);

ALTER TABLE public.unitlocations ADD COLUMN IF NOT EXISTS eventid text;
ALTER TABLE public.unitlocations ADD COLUMN IF NOT EXISTS receivedon timestamp without time zone;
ALTER TABLE public.unitlocations ADD COLUMN IF NOT EXISTS sourcetype integer;
ALTER TABLE public.unitlocations ADD COLUMN IF NOT EXISTS sourceid text;
ALTER TABLE public.unitlocations ADD COLUMN IF NOT EXISTS sourcepriority integer NOT NULL DEFAULT 0;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'unitlocations' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public.unitlocations 
    ADD PRIMARY KEY (id);
END IF;

CREATE UNIQUE INDEX IF NOT EXISTS ux_unitlocations_eventid
  ON public.unitlocations (eventid)
  WHERE eventid IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_unitlocations_department_unit_timestamp
  ON public.unitlocations (departmentid, unitid, "timestamp" DESC);

CREATE INDEX IF NOT EXISTS ix_unitlocations_department_unit_source_timestamp
  ON public.unitlocations (departmentid, unitid, sourcetype, sourceid, "timestamp" DESC);

CREATE INDEX IF NOT EXISTS ix_unitlocations_retention
  ON public.unitlocations (departmentid, sourcetype, "timestamp");


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
  oid text,
  data jsonb NOT NULL
);

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'maplayers' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public.maplayers 
    ADD PRIMARY KEY (id);
END IF;
END $$;
