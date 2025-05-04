DO $$ BEGIN
  --
  -- CREATE TABLE IF NOT EXISTS public."__Efmigrationshistory"
  --
    CREATE TABLE IF NOT EXISTS public."__Efmigrationshistory"(
        "MigrationId" character varying(150) NOT NULL,
        "ProductVersion" character varying(32) NOT NULL
        );

    IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = '__Efmigrationshistory' and constraint_type = 'PRIMARY KEY') then
    ALTER TABLE public."__Efmigrationshistory"
        ADD PRIMARY KEY ("MigrationId");
    END IF;

  IF NOT EXISTS(SELECT "MigrationId" FROM public."__Efmigrationshistory" WHERE "MigrationId" = '20210904153137_CreateOpenIddictModels') THEN

  --
  -- CREATE TABLE IF NOT EXISTS public."OpenIddictScopes"
  --
  CREATE TABLE IF NOT EXISTS public."OpenIddictScopes"(
    "Id" uuid NOT NULL,
    "ConcurrencyToken" character varying(50),
    "Description" character varying,
    "Descriptions" character varying,
    "DisplayName" character varying,
    "DisplayNames" character varying,
    "Name" character varying(200),
    "Properties" character varying,
    "Resources" character varying
  );

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'OpenIddictScopes' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public."OpenIddictScopes"
    ADD PRIMARY KEY ("Id");
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'OpenIddictScopes' and constraint_type = 'UNIQUE') then
  ALTER TABLE public."OpenIddictScopes"
    ADD CONSTRAINT "OpenIddictScopes_Name_key" UNIQUE("Name");
END IF;

  --
  -- CREATE TABLE IF NOT EXISTS public."OpenIddictApplications"
  --
  CREATE TABLE IF NOT EXISTS public."OpenIddictApplications"(
    "Id" uuid NOT NULL,
    "ClientId" character varying(100),
    "ClientSecret" character varying,
    "ConcurrencyToken" character varying(50),
    "ConsentType" character varying(50),
    "DisplayName" character varying,
    "DisplayNames" character varying,
    "Permissions" character varying,
    "PostLogoutRedirectUris" character varying,
    "Properties" character varying,
    "RedirectUris" character varying,
    "Requirements" character varying,
    "ClientType" character varying(50),
    "ApplicationType" character varying,
    "JsonWebKeySet" character varying,
    "Settings" character varying
  );

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'OpenIddictApplications' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public."OpenIddictApplications"
    ADD PRIMARY KEY ("Id");
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'OpenIddictApplications' and constraint_type = 'UNIQUE') then
  ALTER TABLE public."OpenIddictApplications"
    ADD CONSTRAINT "OpenIddictApplications_ClientId_key" UNIQUE("ClientId");
END IF;

  --
  -- CREATE TABLE IF NOT EXISTS public."OpenIddictAuthorizations"
  --
  CREATE TABLE IF NOT EXISTS public."OpenIddictAuthorizations"(
    "Id" uuid NOT NULL,
    "ApplicationId" uuid,
    "ConcurrencyToken" character varying(50),
    "CreationDate" timestamp without time zone,
    "Properties" character varying,
    "Scopes" character varying,
    "Status" character varying(50),
    "Subject" character varying(400),
    "Type" character varying(50)
  );

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'OpenIddictAuthorizations' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public."OpenIddictAuthorizations"
    ADD PRIMARY KEY ("Id");
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'OpenIddictAuthorizations' and constraint_type = 'FOREIGN KEY') then
  ALTER TABLE public."OpenIddictAuthorizations"
    ADD CONSTRAINT "FK_OpenIddictAuthorizations_OpenIddictApplications_ApplicationId" FOREIGN KEY ("ApplicationId")
      REFERENCES public."OpenIddictApplications"("Id") ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;
END IF;

  --
  -- CREATE TABLE IF NOT EXISTS public."OpenIddictTokens"
  --
  CREATE TABLE IF NOT EXISTS public."OpenIddictTokens"(
    "Id" uuid NOT NULL,
    "ApplicationId" uuid,
    "AuthorizationId" uuid,
    "ConcurrencyToken" character varying(50),
    "CreationDate" timestamp without time zone,
    "ExpirationDate" timestamp without time zone,
    "Payload" character varying,
    "Properties" character varying,
    "RedemptionDate" timestamp without time zone,
    "ReferenceId" character varying(100),
    "Status" character varying(50),
    "Subject" character varying(400),
    "Type" character varying(50)
  );

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'OpenIddictTokens' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public."OpenIddictTokens"
    ADD PRIMARY KEY ("Id");
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'OpenIddictTokens' and constraint_type = 'UNIQUE') then
  ALTER TABLE public."OpenIddictTokens"
    ADD CONSTRAINT "OpenIddictTokens_ReferenceId_key" UNIQUE("ReferenceId");
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'OpenIddictTokens' and constraint_type = 'FOREIGN KEY' and constraint_name = 'FK_OpenIddictTokens_OpenIddictApplications_ApplicationId') then
  ALTER TABLE public."OpenIddictTokens"
    ADD CONSTRAINT "FK_OpenIddictTokens_OpenIddictApplications_ApplicationId" FOREIGN KEY ("ApplicationId")
      REFERENCES public."OpenIddictApplications"("Id") ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'OpenIddictTokens' and constraint_type = 'FOREIGN KEY' and constraint_name = 'FK_OpenIddictTokens_OpenIddictAuthorizations_AuthorizationId') then
  ALTER TABLE public."OpenIddictTokens"
    ADD CONSTRAINT "FK_OpenIddictTokens_OpenIddictAuthorizations_AuthorizationId" FOREIGN KEY ("AuthorizationId")
      REFERENCES public."OpenIddictAuthorizations"("Id") ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;
END IF;

  --
  -- CREATE TABLE IF NOT EXISTS public."AspNetUsers"
  --
  CREATE TABLE IF NOT EXISTS public."AspNetUsers"(
    "Id" character varying(450) NOT NULL,
    "UserName" character varying(256),
    "NormalizedUserName" character varying(256),
    "Email" character varying(256),
    "NormalizedEmail" character varying(256),
    "EmailConfirmed" boolean NOT NULL,
    "PasswordHash" character varying,
    "SecurityStamp" character varying,
    "ConcurrencyStamp" character varying,
    "PhoneNumber" character varying,
    "PhoneNumberConfirmed" boolean NOT NULL,
    "TwoFactorEnabled" boolean NOT NULL,
    "LockoutEnd" timestamp with time zone,
    "LockoutEnabled" boolean NOT NULL,
    "AccessFailedCount" integer NOT NULL
  );

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'AspNetUsers' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public."AspNetUsers"
    ADD PRIMARY KEY ("Id");
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'AspNetUsers' and constraint_type = 'UNIQUE') then
  ALTER TABLE public."AspNetUsers"
    ADD CONSTRAINT "AspNetUsers_NormalizedUserName_key" UNIQUE("NormalizedUserName");
END IF;

  --
  -- CREATE TABLE IF NOT EXISTS public."AspNetUserTokens"
  --
  CREATE TABLE IF NOT EXISTS public."AspNetUserTokens"(
    "UserId" character varying(450) NOT NULL,
    "LoginProvider" character varying(450) NOT NULL,
    "Name" character varying(450) NOT NULL,
    "Value" character varying
  );

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'AspNetUserTokens' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public."AspNetUserTokens"
    ADD PRIMARY KEY ("UserId", "LoginProvider", "Name");
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'AspNetUserTokens' and constraint_type = 'FOREIGN KEY') then
  ALTER TABLE public."AspNetUserTokens"
    ADD CONSTRAINT "FK_AspNetUserTokens_AspNetUsers_UserId" FOREIGN KEY ("UserId")
      REFERENCES public."AspNetUsers"("Id") ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;
END IF;

  --
  -- CREATE TABLE IF NOT EXISTS public."AspNetUserLogins"
  --
  CREATE TABLE IF NOT EXISTS public."AspNetUserLogins"(
    "LoginProvider" character varying(450) NOT NULL,
    "ProviderKey" character varying(450) NOT NULL,
    "ProviderDisplayName" character varying,
    "UserId" character varying(450) NOT NULL
  );

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'AspNetUserLogins' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public."AspNetUserLogins"
    ADD PRIMARY KEY ("LoginProvider", "ProviderKey");
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'AspNetUserLogins' and constraint_type = 'FOREIGN KEY') then
  ALTER TABLE public."AspNetUserLogins"
    ADD CONSTRAINT "FK_AspNetUserLogins_AspNetUsers_UserId" FOREIGN KEY ("UserId")
      REFERENCES public."AspNetUsers"("Id") ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;
END IF;

  --
  -- CREATE TABLE IF NOT EXISTS public."AspNetUserClaims"
  --
  CREATE TABLE IF NOT EXISTS public."AspNetUserClaims"(
    "Id" serial,
    "UserId" character varying(450) NOT NULL,
    "ClaimType" character varying,
    "ClaimValue" character varying
  );

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'AspNetUserClaims' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public."AspNetUserClaims"
    ADD PRIMARY KEY ("Id");
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'AspNetUserClaims' and constraint_type = 'FOREIGN KEY') then
  ALTER TABLE public."AspNetUserClaims"
    ADD CONSTRAINT "FK_AspNetUserClaims_AspNetUsers_UserId" FOREIGN KEY ("UserId")
      REFERENCES public."AspNetUsers"("Id") ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;
END IF;

  --
  -- CREATE TABLE IF NOT EXISTS public."AspNetRoles"
  --
  CREATE TABLE IF NOT EXISTS public."AspNetRoles"(
    "Id" character varying(450) NOT NULL,
    "Name" character varying(256),
    "NormalizedName" character varying(256),
    "ConcurrencyStamp" character varying
  );

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'AspNetRoles' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public."AspNetRoles"
    ADD PRIMARY KEY ("Id");
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'AspNetRoles' and constraint_type = 'UNIQUE') then
  ALTER TABLE public."AspNetRoles"
    ADD CONSTRAINT "AspNetRoles_NormalizedName_key" UNIQUE("NormalizedName");
END IF;

  --
  -- CREATE TABLE IF NOT EXISTS public."AspNetUserRoles"
  --
  CREATE TABLE IF NOT EXISTS public."AspNetUserRoles"(
    "UserId" character varying(450) NOT NULL,
    "RoleId" character varying(450) NOT NULL
  );

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'AspNetUserRoles' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public."AspNetUserRoles"
    ADD PRIMARY KEY ("UserId", "RoleId");
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'AspNetUserRoles' and constraint_type = 'FOREIGN KEY' and constraint_name = 'FK_AspNetUserRoles_AspNetRoles_RoleId') then
  ALTER TABLE public."AspNetUserRoles"
    ADD CONSTRAINT "FK_AspNetUserRoles_AspNetRoles_RoleId" FOREIGN KEY ("RoleId")
      REFERENCES public."AspNetRoles"("Id") ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'AspNetUserRoles' and constraint_type = 'FOREIGN KEY' and constraint_name = 'FK_AspNetUserRoles_AspNetUsers_UserId') then
  ALTER TABLE public."AspNetUserRoles"
    ADD CONSTRAINT "FK_AspNetUserRoles_AspNetUsers_UserId" FOREIGN KEY ("UserId")
      REFERENCES public."AspNetUsers"("Id") ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;
END IF;

  --
  -- CREATE TABLE IF NOT EXISTS public."AspNetRoleClaims"
  --
  CREATE TABLE IF NOT EXISTS public."AspNetRoleClaims"(
    "Id" serial,
    "RoleId" character varying(450) NOT NULL,
    "ClaimType" character varying,
    "ClaimValue" character varying
  );

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'AspNetRoleClaims' and constraint_type = 'PRIMARY KEY') then
  ALTER TABLE public."AspNetRoleClaims"
    ADD PRIMARY KEY ("Id");
END IF;

IF NOT exists (select constraint_name from information_schema.table_constraints where table_name = 'AspNetRoleClaims' and constraint_type = 'FOREIGN KEY') then
  ALTER TABLE public."AspNetRoleClaims"
    ADD CONSTRAINT "FK_AspNetRoleClaims_AspNetRoles_RoleId" FOREIGN KEY ("RoleId")
      REFERENCES public."AspNetRoles"("Id") ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;
END IF;

  INSERT INTO public."__Efmigrationshistory" ("MigrationId", "ProductVersion")
  SELECT '20210904153137_CreateOpenIddictModels' AS "MigrationId",'5.0.9' AS "ProductVersion" FROM public."__Efmigrationshistory"
  WHERE NOT EXISTS(
              SELECT "MigrationId" FROM public."__Efmigrationshistory" WHERE "MigrationId" = '20210904153137_CreateOpenIddictModels'
      )
  LIMIT 1;

  INSERT INTO public."__Efmigrationshistory" ("MigrationId", "ProductVersion")
  SELECT '20240412153137_UpdateOpenIddictModelsToV5' AS "MigrationId",'5.0.9' AS "ProductVersion" FROM public."__Efmigrationshistory"
  WHERE NOT EXISTS(
              SELECT "MigrationId" FROM public."__Efmigrationshistory" WHERE "MigrationId" = '20240412153137_UpdateOpenIddictModelsToV5'
      )
  LIMIT 1;

  END IF;
END $$;