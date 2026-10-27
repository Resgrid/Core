def apply(up, down):
    # M0292: the three additive ResourceOrders columns (plan §12.2) — nullable / defaulted, no existing row changes.
    up[(292, False)] = [
        '// Plan §12.2: the only change to shipped structure. Nullable / defaulted, so no existing row changes and the Orders UI ignores them.',
        'if (!Schema.Table("ResourceOrders").Column("IncidentActivationId").Exists())',
        '\tAlter.Table("ResourceOrders").AddColumn("IncidentActivationId").AsString(36).Nullable();',
        'if (!Schema.Table("ResourceOrders").Column("IncidentResourceRequestId").Exists())',
        '\tAlter.Table("ResourceOrders").AddColumn("IncidentResourceRequestId").AsString(36).Nullable();',
        'if (!Schema.Table("ResourceOrders").Column("OriginSystem").Exists())',
        '\tAlter.Table("ResourceOrders").AddColumn("OriginSystem").AsInt32().NotNullable().WithDefaultValue(0);',
        'Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = \'IX_ResourceOrders_IncidentActivation\' AND object_id = OBJECT_ID(\'ResourceOrders\')) CREATE NONCLUSTERED INDEX [IX_ResourceOrders_IncidentActivation] ON [ResourceOrders] ([DepartmentId], [IncidentActivationId]) WHERE [IncidentActivationId] IS NOT NULL;");',
    ]
    up[(292, True)] = [
        '// Plan §12.2: the only change to shipped structure. Nullable / defaulted, so no existing row changes and the Orders UI ignores them.',
        'if (!Schema.Table("resourceorders").Column("incidentactivationid").Exists())',
        '\tAlter.Table("resourceorders").AddColumn("incidentactivationid").AsCustom("citext").Nullable();',
        'if (!Schema.Table("resourceorders").Column("incidentresourcerequestid").Exists())',
        '\tAlter.Table("resourceorders").AddColumn("incidentresourcerequestid").AsCustom("citext").Nullable();',
        'if (!Schema.Table("resourceorders").Column("originsystem").Exists())',
        '\tAlter.Table("resourceorders").AddColumn("originsystem").AsInt32().NotNullable().WithDefaultValue(0);',
        'Execute.Sql("CREATE INDEX IF NOT EXISTS ix_resourceorders_incidentactivation ON resourceorders (departmentid, incidentactivationid) WHERE incidentactivationid IS NOT NULL;");',
    ]
    down[(292, False)] = [
        'Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = \'IX_ResourceOrders_IncidentActivation\' AND object_id = OBJECT_ID(\'ResourceOrders\')) DROP INDEX [IX_ResourceOrders_IncidentActivation] ON [ResourceOrders];");',
        'if (Schema.Table("ResourceOrders").Column("OriginSystem").Exists())',
        '\tExecute.Sql("DECLARE @df sysname = (SELECT d.name FROM sys.default_constraints d JOIN sys.columns c ON c.default_object_id = d.object_id WHERE d.parent_object_id = OBJECT_ID(\'ResourceOrders\') AND c.name = \'OriginSystem\'); IF @df IS NOT NULL EXEC(\'ALTER TABLE [ResourceOrders] DROP CONSTRAINT [\' + @df + \']\'); ALTER TABLE [ResourceOrders] DROP COLUMN [OriginSystem];");',
        'if (Schema.Table("ResourceOrders").Column("IncidentResourceRequestId").Exists())',
        '\tDelete.Column("IncidentResourceRequestId").FromTable("ResourceOrders");',
        'if (Schema.Table("ResourceOrders").Column("IncidentActivationId").Exists())',
        '\tDelete.Column("IncidentActivationId").FromTable("ResourceOrders");',
    ]
    down[(292, True)] = [
        'Execute.Sql("DROP INDEX IF EXISTS ix_resourceorders_incidentactivation;");',
        'if (Schema.Table("resourceorders").Column("originsystem").Exists())',
        '\tDelete.Column("originsystem").FromTable("resourceorders");',
        'if (Schema.Table("resourceorders").Column("incidentresourcerequestid").Exists())',
        '\tDelete.Column("incidentresourcerequestid").FromTable("resourceorders");',
        'if (Schema.Table("resourceorders").Column("incidentactivationid").Exists())',
        '\tDelete.Column("incidentactivationid").FromTable("resourceorders");',
    ]
    # M0293 Pg: the range exclusion constraint as defense in depth over the night rows (plan §11.5). btree_gist may be
    # unavailable to the migration role on a managed server; the night-row unique index is the primary mechanism, so a
    # refusal is logged as a notice and the migration continues.
    up[(293, True)] = [
        '// Plan §11.5: defense in depth over the per-night rows (the primary mechanism in both dialects). The citext key is cast',
        '// to text because btree_gist has no citext operator class; a role that may not create btree_gist skips it with a notice.',
        'Execute.Sql(@"DO $$',
        'BEGIN',
        '  BEGIN',
        '    CREATE EXTENSION IF NOT EXISTS btree_gist;',
        '  EXCEPTION WHEN insufficient_privilege OR undefined_file THEN',
        '    RAISE NOTICE \'btree_gist unavailable; lodging exclusion constraint skipped (night rows still enforce it)\';',
        '  END;',
        '  IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = \'btree_gist\')',
        '     AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = \'ex_incidentlodgingassignments_bednight\') THEN',
        '    ALTER TABLE incidentlodgingassignments ADD CONSTRAINT ex_incidentlodgingassignments_bednight',
        '      EXCLUDE USING gist ((incidentlodgingbedid::text) WITH =, daterange(firstnight, lastnight, \'[]\') WITH &&)',
        '      WHERE (isdeleted = false AND status NOT IN (3, 5));',
        '  END IF;',
        'END $$;");',
    ]
