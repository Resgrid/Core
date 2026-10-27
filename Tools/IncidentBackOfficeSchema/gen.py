import io, os, sys, re, collections
sys.path.insert(0, os.path.dirname(__file__))
from schema import TABLES, C

CORE = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
MODEL_DIR = os.path.join(CORE, 'Core', 'Resgrid.Model', 'IncidentLogistics')
MIG_DIR = os.path.join(CORE, 'Providers', 'Resgrid.Providers.Migrations', 'Migrations')
MIGPG_DIR = os.path.join(CORE, 'Providers', 'Resgrid.Providers.MigrationsPg', 'Migrations')
REPO_DIR = os.path.join(CORE, 'Repositories', 'Resgrid.Repositories.DataRepository')

CATALOG = {'A': 34, 'B': 35, 'C': 36, 'D': None}
MIGRATIONS = collections.OrderedDict([
    (290, ('AddIncidentBackOfficeFoundation', 'Incident Back Office LI-A Foundation (large-incident-logistics-finance-plan.md rev 4.1, §9.0–§9.2, §9.5 enrollment, §9.17 intelligence, §11; registry §4C): activations with complexes and exercise mode, authority profiles and snapshots, standing team rosters, operational periods, plan assembly manifests and weather blocks, objectives, the position catalog, organization nodes and position assignments, participants with protected detail (ADP catalog 34) and qualification snapshots, the snapshot / metric / freshness / projection-cursor read models and the position-routing log. No column on any IC table.')),
    (292, ('AddIncidentLogisticsResources', 'Incident Back Office LI-B Logistics, resources (plan §9.3–§9.5, §12.2): resources, components, status history, movements, timeout extensions, mission packages, requests with approvals, fills and tracked ETA updates, receipts, check-ins and demob gates; plus the three additive ResourceOrders columns (IncidentActivationId, IncidentResourceRequestId, OriginSystem).')),
    (293, ('AddIncidentLogisticsSupport', 'Incident Back Office LI-B Logistics, support (plan §9.6–§9.11, §9.15, §11.5): facilities with capacity, agreements and services; lodging properties, blocks, units, beds, the per-night inventory that enforces one occupant per bed-night, assignments and rooming lists; meal and potable-water planning; support-service templates, tickets and assets; transport, travel and fuel; the communications assignment ledger (ADP catalog 35).')),
    (294, ('AddIncidentVendorsAndSelfService', 'Incident Back Office LI-B vendors (plan §9.12, §9.28): the department support plan, areas and vendor preferences, children of the shipped InventoryVendors row (locations, capabilities, availability windows, compliance documents, protected tax / remittance detail), incident vendor engagements, capacity holds and commitments, quotes, and participant / vendor self-service links (ADP catalog 35).')),
    (295, ('AddIncidentBusiness', 'Incident Back Office LI-C Business (plan §9.12.2–§9.13, §9.23, §11.4): vendor agreements and payment plans, requisitions and approvals, commitments, purchase receipts, invoice references and payment packages; budgets, lines, funding authorizations, spending delegations, funding codes and cost groups; time records with the DTR-import unique constraint, adjustments, equipment use and expenses; rate snapshots, the cost ledger, accrual snapshots, projections, cost-share agreements, rules, apportionment runs and allocations (ADP catalog 36).')),
    (296, ('AddIncidentClaimsRecoveryAndIntake', 'Incident Back Office LI-C Business, continued (plan §9.14, §9.16, §9.26, §9.27): the claim case ledger, the closeout gate, disaster declarations, recovery projects and deadlines, and the evidence intake inbox with its files (ADP catalog 36).')),
    (297, ('AddIncidentCoordination', 'Incident Back Office LI-D Coordination (plan §9.25, §9.25.3a): boards, lanes and interagency share grants; lifeline status history, open actions, decisions in flight and situational features. No protected column: boards are shared with partner agencies.')),
])

def ctype(c):
    t = c.type
    nn = c.null
    if t in ('ref', 'user', 'ic', 'text') or t.startswith('s'):
        return 'string'
    if t == 'bin': return 'byte[]'
    m = {'int': 'int', 'long': 'long', 'bool': 'bool', 'dt': 'DateTime', 'date': 'DateTime', 'money': 'decimal', 'qty': 'decimal', 'pct': 'decimal'}[t]
    return m + ('?' if nn else '')

def sqltype(c, pg):
    t = c.type
    if t in ('ref',): return 'AsCustom("citext")' if pg else 'AsString(36)'
    if t in ('user', 'ic'): return 'AsCustom("citext")' if pg else 'AsString(128)'
    if t == 'text': return 'AsCustom("citext")' if pg else 'AsString(int.MaxValue)'
    if t == 'bin': return 'AsCustom("bytea")' if pg else 'AsBinary(int.MaxValue)'
    if t.startswith('s'):
        return 'AsCustom("citext")' if pg else 'AsString(%d)' % int(t[1:])
    return {'int': 'AsInt32()', 'long': 'AsInt64()', 'bool': 'AsBoolean()', 'dt': 'AsDateTime2()', 'date': 'AsDate()',
            'money': 'AsDecimal(18, 2)', 'qty': 'AsDecimal(18, 4)', 'pct': 'AsDecimal(9, 4)'}[t]

def full_cols(t):
    cols = [C(t.key, 'ref', doc=None), C('DepartmentId', 'int')]
    names = {c.name for c in t.cols}
    if t.scope == 'act' and 'IncidentActivationId' not in names:
        cols.append(C('IncidentActivationId', 'ref'))
    cols += t.cols
    if t.soft: cols.append(C('IsDeleted', 'bool'))
    if t.ver: cols.append(C('RowVersion', 'int', default=1))
    if t.adp: cols += [C('IsProtected', 'bool'), C('ProtectedCatalogVersion', 'int', True)]
    cols += [C('AddedOn', 'dt'), C('AddedByUserId', 'user', True)]
    if t.mutable: cols += [C('EditedOn', 'dt', True), C('EditedByUserId', 'user', True)]
    return cols

def has_activation(t):
    return any(c.name == 'IncidentActivationId' for c in full_cols(t))

def gen_tables():
    return [t for t in TABLES if t.scope != 'billing']

# ------------------------------------------------------------------ models
def gen_models():
    os.makedirs(MODEL_DIR, exist_ok=True)
    files = collections.OrderedDict()
    for t in gen_tables():
        files.setdefault(t.file, []).append(t)
    for fname, tables in files.items():
        out = []
        out.append('using System;')
        out.append('using System.Collections.Generic;')
        out.append('using System.ComponentModel.DataAnnotations;')
        out.append('using System.ComponentModel.DataAnnotations.Schema;')
        out.append('using Newtonsoft.Json;')
        out.append('')
        out.append('namespace Resgrid.Model.IncidentLogistics')
        out.append('{')
        out.append('\t// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with')
        mig_names = sorted({t.mig for t in tables})
        out.append('\t// migration%s %s (both dialects).' % ('s' if len(mig_names) > 1 else '', ', '.join('M%04d' % m for m in mig_names)))
        for t in tables:
            out.append('')
            out.append('\t/// <summary>%s</summary>' % esc(t.doc))
            ifaces = ['IEntity', 'IIncidentLedgerEntity']
            cols = full_cols(t)
            names = {c.name for c in cols}
            if 'IncidentActivationId' in names: ifaces.append('IIncidentScopedEntity')
            if t.soft: ifaces.append('IIncidentSoftDeletable')
            if t.ver: ifaces.append('IIncidentVersioned')
            if t.adp: ifaces.append('IIncidentProtectable')
            if t.mutable: ifaces.append('IIncidentEditable')
            out.append('\tpublic class %s : %s' % (t.entity, ', '.join(ifaces)))
            out.append('\t{')
            for c in cols:
                doc = c.doc
                if c.enum:
                    doc = ('<see cref="%s"/>.' % c.enum) + ((' ' + esc(c.doc)) if c.doc else '')
                elif doc:
                    doc = esc(doc)
                if c.prot:
                    doc = (doc + ' ' if doc else '') + 'ADP-protected (%s, %s family).' % (c.prot[0], c.prot[1])
                if doc:
                    out.append('\t\t/// <summary>%s</summary>' % doc)
                if c.name in (t.key, 'DepartmentId'):
                    out.append('\t\t[Required]')
                default = ''
                if c.name == 'RowVersion': default = ' = 1;'
                elif c.default is True: default = ' = true;'
                out.append('\t\tpublic %s %s { get; set; }%s' % (ctype(c), c.name, default))
            out.append('')
            out.append('\t\t[NotMapped] public string TableName => "%s";' % t.table)
            out.append('\t\t[NotMapped] public string IdName => "%s";' % t.key)
            out.append('\t\t[NotMapped] public int IdType => 1;')
            out.append('\t\t[NotMapped] [JsonIgnore] public object IdValue { get => %s; set => %s = (string)value; }' % (t.key, t.key))
            out.append('\t\t[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };')
            out.append('\t}')
        out.append('}')
        write(os.path.join(MODEL_DIR, fname + '.cs'), '\r\n'.join(out) + '\r\n')

def esc(s):
    return (s or '').replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')

# ------------------------------------------------------------------ migrations
def filt(t, spec, pg):
    if spec is None: return ''
    parts = []
    tokens = spec.split('+')
    for tok in tokens:
        if tok == 'live':
            assert t.soft, t.table
            parts.append('isdeleted = FALSE' if pg else '[IsDeleted] = 0')
        elif tok == 'catalog':
            parts.append('incidentactivationid IS NULL' if pg else '[IncidentActivationId] IS NULL')
        else:
            parts.append(('%s IS NOT NULL' % tok.lower()) if pg else ('[%s] IS NOT NULL' % tok))
    return ' WHERE ' + ' AND '.join(parts)

def indexes(t):
    res = []
    if has_activation(t) and t.scope == 'act':
        res.append(('IX_%s_Activation' % t.table, ['DepartmentId', 'IncidentActivationId'], False, None))
    else:
        res.append(('IX_%s_Department' % t.table, ['DepartmentId'], False, None))
    for name, cols in t.idx:
        res.append(('IX_%s_%s' % (t.table, name), cols, False, None))
    for name, cols, f in t.uidx:
        res.append(('UX_%s_%s' % (t.table, name), cols, True, f))
    return res

def mig_table(t, pg):
    L = []
    tn = t.table.lower() if pg else t.table
    L.append('\t\t\tif (!Schema.Table("%s").Exists())' % tn)
    L.append('\t\t\t{')
    L.append('\t\t\t\tCreate.Table("%s")' % tn)
    cols = full_cols(t)
    for i, c in enumerate(cols):
        name = c.name.lower() if pg else c.name
        s = '\t\t\t\t\t.WithColumn("%s").%s' % (name, sqltype(c, pg))
        if c.name == t.key:
            s += '.NotNullable().PrimaryKey()'
        elif c.null:
            s += '.Nullable()'
        else:
            s += '.NotNullable()'
            if c.type == 'bool':
                s += '.WithDefaultValue(%s)' % ('true' if c.default is True else 'false')
            elif c.type in ('int', 'long', 'money', 'qty', 'pct') and c.name != 'DepartmentId':
                s += '.WithDefaultValue(%s)' % (0 if (c.default is None or c.default is True) else c.default)
        if i == len(cols) - 1: s += ';'
        L.append(s)
    for name, icols, unique, f in indexes(t):
        if pg:
            L.append('\t\t\t\tExecute.Sql("CREATE %sINDEX IF NOT EXISTS %s ON %s (%s)%s;");' % (
                'UNIQUE ' if unique else '', name.lower(), tn, ', '.join(x.lower() for x in icols), filt(t, f, True)))
        else:
            L.append('\t\t\t\tExecute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = \'%s\' AND object_id = OBJECT_ID(\'%s\')) CREATE %sNONCLUSTERED INDEX [%s] ON [%s] (%s)%s;");' % (
                name, t.table, 'UNIQUE ' if unique else '', name, t.table, ', '.join('[%s]' % x for x in icols), filt(t, f, False)))
    if pg:
        for c in cols:
            if c.name in ('Currency',) or c.name == 'ShortCode':
                n = int(c.type[1:])
                ck = 'ck_%s_%s' % (tn, c.name.lower())
                L.append('\t\t\t\tExecute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = \'%s\') THEN ALTER TABLE %s ADD CONSTRAINT %s CHECK (char_length(%s) <= %d); END IF; END $$;");' % (ck, tn, ck, c.name.lower(), n))
    L.append('\t\t\t}')
    return L

EXTRA_UP = {}
EXTRA_DOWN = {}

def gen_migrations():
    for num, (name, doc) in MIGRATIONS.items():
        tables = [t for t in gen_tables() if t.mig == num]
        for pg in (False, True):
            cls = 'M%04d_%s%s' % (num, name, 'Pg' if pg else '')
            ns = 'Resgrid.Providers.MigrationsPg.Migrations' if pg else 'Resgrid.Providers.Migrations.Migrations'
            L = ['using FluentMigrator;', '', 'namespace ' + ns, '{', '\t/// <summary>']
            for line in wrap(doc + (' PostgreSQL twin: lower-case identifiers and every string column citext (plan §11.2), existence-guarded.' if pg else ' No foreign keys (the IC precedent): tenant ownership is enforced in the service layer and the department purge (IncidentBackOfficeDepartmentCleanup) removes every row. Existence-guarded for safe retry.')):
                L.append('\t/// ' + esc(line))
            L += ['\t/// </summary>', '\t[Migration(%d)]' % num, '\tpublic class %s : Migration' % cls, '\t{', '\t\tpublic override void Up()', '\t\t{']
            first = True
            for t in tables:
                if not first: L.append('')
                first = False
                L += mig_table(t, pg)
            extra = EXTRA_UP.get((num, pg))
            if extra:
                L.append('')
                L += ['\t\t\t' + x if x else '' for x in extra]
            L += ['\t\t}', '', '\t\tpublic override void Down()', '\t\t{']
            extra = EXTRA_DOWN.get((num, pg))
            if extra:
                L += ['\t\t\t' + x if x else '' for x in extra]
            for t in reversed(tables):
                tn = t.table.lower() if pg else t.table
                L.append('\t\t\tif (Schema.Table("%s").Exists())' % tn)
                L.append('\t\t\t\tDelete.Table("%s");' % tn)
            L += ['\t\t}', '\t}', '}']
            d = MIGPG_DIR if pg else MIG_DIR
            write(os.path.join(d, cls + '.cs'), '\r\n'.join(L) + '\r\n')

def wrap(text, width=120):
    words = text.split(' ')
    lines, cur = [], ''
    for w in words:
        if len(cur) + len(w) + 1 > width and cur:
            lines.append(cur); cur = w
        else:
            cur = (cur + ' ' + w) if cur else w
    if cur: lines.append(cur)
    return lines

# ------------------------------------------------------------------ table registry + ADP catalog
def gen_registry():
    tables = gen_tables()
    L = ['using System.Collections.Generic;', '', 'namespace Resgrid.Model.IncidentLogistics', '{', '\t/// <summary>',
         '\t/// Every Incident Back Office table (generated from the schema spec; plan §11.3), in create order. Department deletion, the',
         '\t/// GDPR export and the lifecycle-hook test (plan §19.5) enumerate it, so a table cannot be added without being purged.',
         '\t/// </summary>', '\tpublic static class IncidentBackOfficeTables', '\t{',
         '\t\tpublic static readonly IReadOnlyList<(string Table, string Key, string Track, bool HasActivation)> All = new[]', '\t\t{']
    for t in tables:
        L.append('\t\t\t("%s", "%s", "%s", %s),' % (t.table, t.key, t.track, 'true' if has_activation(t) else 'false'))
    L[-1] = L[-1].rstrip(',')
    L += ['\t\t};', '', '\t\t/// <summary>The department-scoped billing account of the add-on (M0291), outside the ledger above.</summary>',
          '\t\tpublic const string BillingAccounts = "IncidentBackOfficeBillingAccounts";', '',
          '\t\t/// <summary>The column a table is named by in pick lists and drill-through labels.</summary>',
          '\t\tpublic static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)', '\t\t{']
    for t in tables:
        L.append('\t\t\t["%s"] = "%s",' % (t.table, label_column(t)))
    L[-1] = L[-1].rstrip(',')
    L += ['\t\t};', '', '\t\t/// <summary>Tables with an IsDeleted column (soft delete).</summary>',
          '\t\tpublic static readonly IReadOnlyCollection<string> SoftDeletable = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)', '\t\t{']
    L.append(',\n'.join('\t\t\t"%s"' % t.table for t in tables if t.soft))
    L += ['\t\t};', '\t}', '}']
    write(os.path.join(MODEL_DIR, 'IncidentBackOfficeTables.cs'), '\r\n'.join(L) + '\r\n')

    # ADP catalog + accessor maps
    L = ['using System;', 'using System.Collections.Generic;', '', 'namespace Resgrid.Model.IncidentLogistics', '{', '\t/// <summary>',
         '\t/// The Incident Back Office ADP catalog (plan §15.3a): exactly the fields that identify, locate, price or describe the health of a',
         '\t/// person, or carry a financial identifier — and nothing else. LI-A participants are catalog 34, LI-B lodging, travel, transport,',
         '\t/// service, communications, vendor and self-service fields 35, LI-C rate, cost, expense, time remarks, claim and intake fields 36.',
         '\t/// Values are sealed in place (text columns); the AAD is the string key + DepartmentId. Accessor maps drive the shared seams.',
         '\t/// </summary>', '\tpublic static class IncidentBackOfficeProtectedFields', '\t{',
         '\t\tpublic const int FoundationCatalogVersion = 34;', '\t\tpublic const int LogisticsCatalogVersion = 35;', '\t\tpublic const int BusinessCatalogVersion = 36;',
         '\t\tpublic const string LogisticsWorkloadPurpose = "incident-logistics";', '\t\tpublic const string CostingWorkloadPurpose = "incident-costing";',
         '\t\tpublic const string SelfServiceWorkloadPurpose = "incident-self-service";', '',
         '\t\tprivate static IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> Map<T>(params (string Field, Func<T, string> Get, Action<T, string> Set)[] entries)',
         '\t\t{', '\t\t\tvar map = new Dictionary<string, (Func<T, string>, Action<T, string>)>(StringComparer.OrdinalIgnoreCase);',
         '\t\t\tforeach (var (field, get, set) in entries) map[field] = (get, set);', '\t\t\treturn map;', '\t\t}']
    for t in tables:
        if not t.adp: continue
        L.append('')
        entries = ['\t\t\t("%s.%s", e => e.%s, (e, v) => e.%s = v)' % (t.table.lower(), c.name.lower(), c.name, c.name) for c in t.cols if c.prot]
        L.append('\t\tpublic static readonly IReadOnlyDictionary<string, (Func<%s, string> Get, Action<%s, string> Set)> %s = Map<%s>(' % (t.entity, t.entity, t.entity, t.entity))
        L.append(',\r\n'.join(entries) + ');')
    L += ['', '\t\t/// <summary>Catalog rows: table, column, classification, family and the catalog version that added them.</summary>',
          '\t\tpublic static IEnumerable<(string Table, string Column, string Classification, string Family, int CatalogVersion)> All()', '\t\t{']
    for t in tables:
        for c in t.cols:
            if c.prot:
                ver = {'A': 'FoundationCatalogVersion', 'B': 'LogisticsCatalogVersion', 'C': 'BusinessCatalogVersion'}[t.track]
                L.append('\t\t\tyield return ("%s", "%s", "%s", "%s", %s);' % (t.table, c.name, c.prot[0], c.prot[1], ver))
    L += ['\t\t}', '\t}', '}']
    write(os.path.join(MODEL_DIR, 'IncidentBackOfficeProtectedFields.cs'), '\r\n'.join(L) + '\r\n')

LABEL_PREFERENCE = ['DisplayName', 'Name', 'Title', 'ShortCode', 'RequestNumber', 'TicketNumber', 'RequisitionNumber', 'CommitmentNumber',
                    'PackageNumber', 'ClaimNumber', 'DeclarationNumber', 'InvoiceNumber', 'Code', 'ItemLabel', 'SiteName', 'BedLabel', 'Component',
                    'Question', 'LaneType', 'ScenarioName', 'RoomType', 'ServiceDescription', 'PartyName', 'Label', 'ItemDescription', 'AgreementNumber',
                    'PlaceholderLabel', 'MetricKey', 'SourceKey', 'ContractKey', 'Route', 'VehicleLabel', 'Text', 'Reason', 'FileName']

def label_column(t):
    names = [c.name for c in full_cols(t)]
    for n in LABEL_PREFERENCE:
        if n in names:
            return n
    for n in ('Number', 'VersionNumber', 'Version'):
        if n in names:
            return n
    return t.key

def write(path, text):
    if (os.sep + 'Migrations' + os.sep) in path and os.path.exists(path):
        raise SystemExit('refusing to overwrite the shipped migration ' + path)
    with io.open(path, 'w', encoding='utf-8', newline='') as f:
        f.write(text)

if __name__ == '__main__':
    import extras  # noqa: populates EXTRA_UP / EXTRA_DOWN
    extras.apply(EXTRA_UP, EXTRA_DOWN)
    gen_models()
    # Shipped migrations are immutable: they are written only on request, and never over an existing file.
    if '--migrations' in sys.argv:
        gen_migrations()
    gen_registry()
    print('tables', len(gen_tables()), 'protected cols', sum(1 for t in gen_tables() for c in t.cols if c.prot))
