DO $$ BEGIN
  --
  -- CREATE TABLE IF NOT EXISTS "public"."__efmigrationshistory"
  --
    CREATE TABLE IF NOT EXISTS public.__efmigrationshistory(
                                                               migrationid character varying(150) NOT NULL,
        productversion character varying(32) NOT NULL
        );
    
    
    IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = '__efmigrationshistory' and constraint_type = 'PRIMARY KEY') then
    ALTER TABLE public.__efmigrationshistory
        ADD PRIMARY KEY (migrationid);
    END IF;
      
      
  IF EXISTS(SELECT migrationid FROM public.__efmigrationshistory WHERE migrationid = '20210904153137_CreateOpenIddictModels') THEN
    
  --
  -- CREATE TABLE IF NOT EXISTS "public"."openiddictscopes"
  --
  CREATE TABLE IF NOT EXISTS public.openiddictscopes(
    id uuid NOT NULL,
    concurrencytoken character varying(50),
    description character varying,
    descriptions character varying,
    displayname character varying,
    displaynames character varying,
    name character varying(200),
    properties character varying,
    resources character varying
  );

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'openiddictscopes' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public.openiddictscopes 
    ADD PRIMARY KEY (id);
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'openiddictscopes' and constraint_type = 'UNIQUE') then
  ALTER TABLE public.openiddictscopes 
    ADD CONSTRAINT openiddictscopes_name_key UNIQUE(name);
END IF;

  
  --
  -- CREATE TABLE IF NOT EXISTS "public"."openiddictapplications"
  --
  CREATE TABLE IF NOT EXISTS public.openiddictapplications(
    id uuid NOT NULL,
    clientid character varying(100),
    clientsecret character varying,
    concurrencytoken character varying(50),
    consenttype character varying(50),
    displayname character varying,
    displaynames character varying,
    permissions character varying,
    postlogoutredirecturis character varying,
    properties character varying,
    redirecturis character varying,
    requirements character varying,
    clienttype character varying(50),
    applicationtype character varying,
    jsonwebkeyset character varying,
    settings character varying
  );
  

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'openiddictapplications' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public.openiddictapplications 
    ADD PRIMARY KEY (id);
END IF;


IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'openiddictapplications' and constraint_type = 'UNIQUE') then
  ALTER TABLE public.openiddictapplications 
    ADD CONSTRAINT openiddictapplications_clientid_key UNIQUE(clientid);
END IF;

  
  --
  -- CREATE TABLE IF NOT EXISTS "public"."openiddictauthorizations"
  --
  CREATE TABLE IF NOT EXISTS public.openiddictauthorizations(
    id uuid NOT NULL,
    applicationid uuid,
    concurrencytoken character varying(50),
    creationdate timestamp without time zone,
    properties character varying,
    scopes character varying,
    status character varying(50),
    subject character varying(400),
    type character varying(50)
  );
  

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'openiddictauthorizations' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public.openiddictauthorizations 
    ADD PRIMARY KEY (id);
END IF;

  
IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'openiddictauthorizations' and constraint_type = 'FOREIGN KEY') then
  ALTER TABLE public.openiddictauthorizations 
    ADD CONSTRAINT fk_openiddictauthorizations_openiddictapplications_applicationi FOREIGN KEY (applicationid)
      REFERENCES public.openiddictapplications(id) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;
END IF;

  --
  -- CREATE TABLE IF NOT EXISTS "public"."openiddicttokens"
  --
  CREATE TABLE IF NOT EXISTS public.openiddicttokens(
    id uuid NOT NULL,
    applicationid uuid,
    authorizationid uuid,
    concurrencytoken character varying(50),
    creationdate timestamp without time zone,
    expirationdate timestamp without time zone,
    payload character varying,
    properties character varying,
    redemptiondate timestamp without time zone,
    referenceid character varying(100),
    status character varying(50),
    subject character varying(400),
    type character varying(50)
  );
  

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'openiddicttokens' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public.openiddicttokens 
    ADD PRIMARY KEY (id);
END IF;


IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'openiddicttokens' and constraint_type = 'UNIQUE') then
  ALTER TABLE public.openiddicttokens 
    ADD CONSTRAINT openiddicttokens_referenceid_key UNIQUE(referenceid);
END IF;
  

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'openiddicttokens' and constraint_type = 'FOREIGN KEY' and constraint_name = 'fk_openiddicttokens_openiddictapplications_applicationid') then
  ALTER TABLE public.openiddicttokens 
    ADD CONSTRAINT fk_openiddicttokens_openiddictapplications_applicationid FOREIGN KEY (applicationid)
      REFERENCES public.openiddictapplications(id) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'openiddicttokens' and constraint_type = 'FOREIGN KEY' and constraint_name = 'fk_openiddicttokens_openiddictauthorizations_authorizationid') then
  ALTER TABLE public.openiddicttokens 
    ADD CONSTRAINT fk_openiddicttokens_openiddictauthorizations_authorizationid FOREIGN KEY (authorizationid)
      REFERENCES public.openiddictauthorizations(id) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;
END IF;


  --
  -- CREATE TABLE IF NOT EXISTS "public"."aspnetusers"
  --
  CREATE TABLE IF NOT EXISTS public.aspnetusers(
    id character varying(450) NOT NULL,
    username character varying(256),
    normalizedusername character varying(256),
    email character varying(256),
    normalizedemail character varying(256),
    emailconfirmed boolean NOT NULL,
    passwordhash character varying,
    securitystamp character varying,
    concurrencystamp character varying,
    phonenumber character varying,
    phonenumberconfirmed boolean NOT NULL,
    twofactorenabled boolean NOT NULL,
    lockoutend timestamp with time zone,
    lockoutenabled boolean NOT NULL,
    accessfailedcount integer NOT NULL
  );
  

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'aspnetusers' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public.aspnetusers 
    ADD PRIMARY KEY (id);
END IF;


IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'aspnetusers' and constraint_type = 'UNIQUE') then
  ALTER TABLE public.aspnetusers 
    ADD CONSTRAINT aspnetusers_normalizedusername_key UNIQUE(normalizedusername);
END IF;

  
  --
  -- CREATE TABLE IF NOT EXISTS "public"."aspnetusertokens"
  --
  CREATE TABLE IF NOT EXISTS public.aspnetusertokens(
    userid character varying(450) NOT NULL,
    loginprovider character varying(450) NOT NULL,
    name character varying(450) NOT NULL,
    value character varying
  );
  

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'aspnetusertokens' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public.aspnetusertokens 
    ADD PRIMARY KEY (userid, loginprovider, name);
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'aspnetusertokens' and constraint_type = 'FOREIGN KEY') then
  ALTER TABLE public.aspnetusertokens 
    ADD CONSTRAINT fk_aspnetusertokens_aspnetusers_userid FOREIGN KEY (userid)
      REFERENCES public.aspnetusers(id) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;
END IF;

  
  --
  -- CREATE TABLE IF NOT EXISTS "public"."aspnetuserlogins"
  --
  CREATE TABLE IF NOT EXISTS public.aspnetuserlogins(
    loginprovider character varying(450) NOT NULL,
    providerkey character varying(450) NOT NULL,
    providerdisplayname character varying,
    userid character varying(450) NOT NULL
  );
  

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'aspnetuserlogins' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public.aspnetuserlogins 
    ADD PRIMARY KEY (loginprovider, providerkey);
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'aspnetuserlogins' and constraint_type = 'FOREIGN KEY') then
  ALTER TABLE public.aspnetuserlogins 
    ADD CONSTRAINT fk_aspnetuserlogins_aspnetusers_userid FOREIGN KEY (userid)
      REFERENCES public.aspnetusers(id) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;
END IF;
 
  
  --
  -- CREATE TABLE IF NOT EXISTS "public"."aspnetuserclaims"
  --
  CREATE TABLE IF NOT EXISTS public.aspnetuserclaims(
    id serial,
    userid character varying(450) NOT NULL,
    claimtype character varying,
    claimvalue character varying
  );
  

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'aspnetuserclaims' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public.aspnetuserclaims 
    ADD PRIMARY KEY (id);
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'aspnetuserclaims' and constraint_type = 'FOREIGN KEY') then
  ALTER TABLE public.aspnetuserclaims 
    ADD CONSTRAINT fk_aspnetuserclaims_aspnetusers_userid FOREIGN KEY (userid)
      REFERENCES public.aspnetusers(id) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;
END IF;
  
  --
  -- CREATE TABLE IF NOT EXISTS "public"."aspnetroles"
  --
  CREATE TABLE IF NOT EXISTS public.aspnetroles(
    id character varying(450) NOT NULL,
    name character varying(256),
    normalizedname character varying(256),
    concurrencystamp character varying
  );
  


IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'aspnetroles' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public.aspnetroles 
    ADD PRIMARY KEY (id);
END IF;


IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'aspnetroles' and constraint_type = 'UNIQUE') then
  ALTER TABLE public.aspnetroles 
    ADD CONSTRAINT aspnetroles_normalizedname_key UNIQUE(normalizedname);
END IF;
  
  --
  -- CREATE TABLE IF NOT EXISTS "public"."aspnetuserroles"
  --
  CREATE TABLE IF NOT EXISTS public.aspnetuserroles(
    userid character varying(450) NOT NULL,
    roleid character varying(450) NOT NULL
  );
  
IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'aspnetuserroles' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public.aspnetuserroles 
    ADD PRIMARY KEY (userid, roleid);
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'aspnetuserroles' and constraint_type = 'FOREIGN KEY' and constraint_name = 'fk_aspnetuserroles_aspnetroles_roleid') then
  ALTER TABLE public.aspnetuserroles 
    ADD CONSTRAINT fk_aspnetuserroles_aspnetroles_roleid FOREIGN KEY (roleid)
      REFERENCES public.aspnetroles(id) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;
END IF;
  
IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'aspnetuserroles' and constraint_type = 'FOREIGN KEY' and constraint_name = 'fk_aspnetuserroles_aspnetusers_userid') then
  ALTER TABLE public.aspnetuserroles 
    ADD CONSTRAINT fk_aspnetuserroles_aspnetusers_userid FOREIGN KEY (userid)
      REFERENCES public.aspnetusers(id) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;
END IF;

  
  --
  -- CREATE TABLE IF NOT EXISTS "public"."aspnetroleclaims"
  --
  CREATE TABLE IF NOT EXISTS public.aspnetroleclaims(
    id serial,
    roleid character varying(450) NOT NULL,
    claimtype character varying,
    claimvalue character varying
  );
  

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'aspnetroleclaims' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public.aspnetroleclaims 
    ADD PRIMARY KEY (id);
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'aspnetroleclaims' and constraint_type = 'FOREIGN KEY') then
  ALTER TABLE public.aspnetroleclaims 
    ADD CONSTRAINT fk_aspnetroleclaims_aspnetroles_roleid FOREIGN KEY (roleid)
      REFERENCES public.aspnetroles(id) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;
END IF;
  
  
  
  INSERT INTO public.__efmigrationshistory (migrationid, productversion)
  SELECT '20210904153137_CreateOpenIddictModels' AS migrationid,'5.0.9' AS productversion FROM public.__efmigrationshistory
  WHERE NOT EXISTS(
              SELECT migrationid FROM public.__efmigrationshistory WHERE migrationid = '20210904153137_CreateOpenIddictModels'
      )
  LIMIT 1;
  
  INSERT INTO public.__efmigrationshistory (migrationid, productversion)
  SELECT '20240412153137_UpdateOpenIddictModelsToV5' AS migrationid,'5.0.9' AS productversion FROM public.__efmigrationshistory
  WHERE NOT EXISTS(
              SELECT migrationid FROM public.__efmigrationshistory WHERE migrationid = '20240412153137_UpdateOpenIddictModelsToV5'
      )
  LIMIT 1;
  
  END IF;
END $$;
