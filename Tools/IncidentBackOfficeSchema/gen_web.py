import io, os, sys, re
sys.path.insert(0, os.path.dirname(__file__))
from schema import TABLES
from gen import full_cols, gen_tables, label_column, has_activation

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
OUT = os.path.join(ROOT, 'Web', 'Resgrid.Web', 'Areas', 'User', 'Models', 'IncidentBackOffice', 'IncidentLedgerCatalog.Generated.cs')
H = 'ClaimsAuthorizationHelper.'

# Tables with a registered fixed lifecycle (IncidentLifecycles.cs) -> status column.
LIFECYCLE = {
    'IncidentActivations': 'Status', 'IncidentOperationalPeriods': 'Status', 'IncidentPlanVersions': 'Status', 'IncidentObjectives': 'Status',
    'IncidentReconciliationItems': 'Status', 'IncidentResourceRequests': 'Status', 'IncidentRequestFills': 'Status', 'IncidentReceipts': 'Status',
    'IncidentResources': 'SupportStatus', 'IncidentParticipants': 'SupportStatus', 'IncidentDemobPlans': 'Status', 'IncidentDemobChecklistItems': 'Status',
    'IncidentCloseoutChecklistItems': 'Status', 'IncidentFacilities': 'Status', 'IncidentLodgingBlocks': 'Status', 'IncidentLodgingAssignments': 'Status',
    'IncidentMealPlans': 'Status', 'IncidentMealOrders': 'Status', 'IncidentServiceRequests': 'Status', 'IncidentTransportRequests': 'Status',
    'IncidentTransportMovements': 'Status', 'IncidentCommunicationsResourceAssignments': 'Status', 'IncidentVendorEngagements': 'Status',
    'IncidentVendorCapacityCommitments': 'Status', 'IncidentVendorQuotes': 'Status', 'PurchaseRequisitions': 'Status', 'IncidentCommitments': 'Status',
    'PurchaseReceipts': 'Status', 'VendorInvoiceReferences': 'Status', 'PaymentPackages': 'Status', 'IncidentBudgets': 'Status',
    'IncidentTimeRecords': 'Status', 'IncidentEquipmentUseRecords': 'Status', 'IncidentExpenses': 'Status', 'IncidentCostShareAgreements': 'Status',
    'IncidentClaims': 'Status', 'IncidentDisasterDeclarations': 'Status', 'IncidentRecoveryProjects': 'Status', 'IncidentRecoveryDeadlines': 'Status',
    'IncidentIntakeItems': 'Status', 'IncidentOpenActions': 'Status', 'IncidentDecisions': 'Status',
}

V = H + 'CanViewIncidentBackOffice()'
def any_(*hs): return ' || '.join(H + h + '()' for h in hs)

# table -> controller, read, write, extras
UI = {}
def ui(tables, controller, read, write, protected=None, ro=(), transition=None, nodelete=()):
    for t in tables:
        UI[t] = dict(controller=controller, read=read, write=write, protected=protected or write, ro=t in ro, transition=transition, delete=t not in nodelete)

ui(['DepartmentIncidentAuthorityProfiles', 'IncidentDomainAuthorities', 'IncidentAuthorityProfileSnapshots', 'IncidentReconciliationItems'], 'IncidentAuthority', V,
   H + 'CanActivateIncident()', ro=('IncidentAuthorityProfileSnapshots',))
UI['IncidentReconciliationItems']['write'] = any_('CanManageIncidentPeriods', 'CanActivateIncident')
ui(['DepartmentIncidentTeams', 'DepartmentIncidentTeamRotations', 'DepartmentIncidentTeamSlots', 'DepartmentIncidentTeamCandidates'], 'IncidentTeams', V, H + 'CanManageIncidentPosition()')
ui(['IncidentOperationalPeriods', 'IncidentPlanVersions', 'IncidentPlanWeatherBlocks', 'IncidentObjectives'], 'IncidentPeriods', V, H + 'CanManageIncidentPeriods()')
ui(['IncidentPositionDefinitions', 'IncidentPositionCapabilities'], 'IncidentPositions', V, H + 'CanManageIncidentPosition()')
ui(['IncidentOrganizationNodes', 'IncidentPositionAssignments'], 'IncidentOrganization', V, H + 'CanManageIncidentPosition()')
ui(['IncidentParticipants', 'IncidentParticipantSensitive', 'IncidentParticipantQualifications', 'IncidentExternalOrganizationRefs', 'IncidentSelfServiceLinks'], 'IncidentParticipants', V,
   H + 'CanManageIncidentParticipant()', protected=H + 'CanViewIncidentParticipantSensitive()')
UI['IncidentParticipantSensitive']['read'] = H + 'CanViewIncidentParticipantSensitive()'
UI['IncidentSelfServiceLinks']['write'] = any_('CanManageIncidentParticipant', 'CanOrderVendor')
UI['IncidentSelfServiceLinks']['protected'] = any_('CanManageIncidentParticipant', 'CanOrderVendor')
ui(['IncidentOperationalSnapshots', 'IncidentMetricValues', 'IncidentDataFreshness', 'IncidentProjectionCursors', 'IncidentNotificationLogs', 'IncidentSavedDashboardViews'],
   'IncidentBackOffice', V, V, ro=('IncidentOperationalSnapshots', 'IncidentMetricValues', 'IncidentDataFreshness', 'IncidentProjectionCursors', 'IncidentNotificationLogs'))
# Append-only ledgers: people create rows, nobody edits or deletes them (corrections are new rows).
APPEND = ('IncidentFundingAuthorizations', 'IncidentTimeAdjustments', 'IncidentAssignmentExtensions', 'IncidentFillEtaUpdates', 'IncidentRateSnapshots', 'IncidentMealDemands')
# Issued and revoked through their own actions only.
SYSTEM_ONLY = ('IncidentSelfServiceLinks',)
UI['IncidentActivations'] = dict(controller='IncidentBackOffice', read=V, write=H + 'CanActivateIncident()', protected=V, ro=True, transition=None, delete=False)

REQ_T = '(s) => s is 2 or 12 ? %sCanApproveIncidentRequest() : s == 3 ? %sCanOrderIncidentRequest() : %sCanCreateIncidentRequest() || %sCanManageIncidentRequest()' % (H, H, H, H)
ui(['IncidentResourceRequests', 'IncidentRequestApprovals', 'IncidentRequestFills', 'IncidentFillEtaUpdates', 'IncidentReceipts', 'IncidentReceiptLines',
    'IncidentMissionPackages', 'IncidentMissionPackageEstimates'], 'IncidentRequests', V, H + 'CanCreateIncidentRequest()', ro=('IncidentRequestApprovals', 'IncidentFillEtaUpdates'))
UI['IncidentResourceRequests']['transition'] = REQ_T
UI['IncidentRequestFills']['write'] = any_('CanOrderIncidentRequest', 'CanManageIncidentRequest')
UI['IncidentReceipts']['write'] = H + 'CanManageIncidentRequest()'
UI['IncidentReceiptLines']['write'] = H + 'CanManageIncidentRequest()'
ui(['IncidentResources', 'IncidentResourceComponents', 'IncidentResourceStatusHistory', 'IncidentResourceMovements', 'IncidentAssignmentExtensions', 'IncidentCheckIns'],
   'IncidentCheckIn', V, H + 'CanManageIncidentRequest()', ro=('IncidentResourceStatusHistory', 'IncidentAssignmentExtensions'))
ui(['IncidentDemobPlans', 'IncidentDemobChecklistItems'], 'IncidentDemob', V, any_('CanManageIncidentRequest', 'CanManageIncidentParticipant'))
ui(['IncidentFacilities', 'IncidentFacilityCapacities', 'IncidentFacilityAgreements', 'IncidentFacilityServices'], 'IncidentFacilities', V, H + 'CanManageIncidentFacility()')
ui(['IncidentLodgingProperties', 'IncidentLodgingBlocks', 'IncidentLodgingUnits', 'IncidentLodgingBeds', 'IncidentLodgingNightInventory', 'IncidentLodgingAssignments',
    'IncidentRoomingListVersions'], 'IncidentLodging', V, H + 'CanManageIncidentLodging()', protected=H + 'CanViewIncidentLodgingDetail()',
   ro=('IncidentLodgingNightInventory', 'IncidentRoomingListVersions'))
ui(['IncidentMealPlans', 'IncidentMealDemands', 'IncidentMealOrders', 'IncidentMealActuals', 'IncidentDemandCoefficients'], 'IncidentFeeding', V, H + 'CanManageIncidentFeeding()',
   ro=('IncidentMealDemands',))
ui(['IncidentWaterPlans', 'IncidentWaterStock', 'IncidentWaterDeliveries'], 'IncidentWater', V, H + 'CanManageIncidentFeeding()')
ui(['IncidentServiceTemplates', 'IncidentServiceRequests', 'IncidentServiceActivities', 'IncidentServiceAssets'], 'IncidentServices', V, H + 'CanManageIncidentService()')
UI['IncidentServiceTemplates']['write'] = any_('CanManageIncidentService', 'CanActivateIncident')
ui(['IncidentTransportRequests', 'IncidentTransportMovements', 'IncidentTravelItineraries', 'IncidentFuelTransactions'], 'IncidentGroundSupport', V,
   H + 'CanManageIncidentGroundSupport()', protected=H + 'CanViewIncidentParticipantSensitive()')
ui(['IncidentCommunicationsPlans', 'IncidentCommunicationsResourceAssignments'], 'IncidentCommunications', V, H + 'CanManageIncidentComms()')
ui(['DepartmentEmergencySupportPlans', 'SupportPlanAreas', 'SupportPlanVendorPreferences', 'IncidentSupportPlanSnapshots'], 'SupportPlan', V, H + 'CanManageVendor()',
   ro=('IncidentSupportPlanSnapshots',))
ui(['VendorLocations', 'VendorCapabilities', 'VendorAvailabilityWindows', 'VendorComplianceDocuments', 'VendorProtectedDetails'], 'Vendors', V, H + 'CanManageVendor()',
   protected=H + 'CanViewVendorProtected()')
UI['VendorProtectedDetails']['read'] = H + 'CanViewVendorProtected()'
ui(['IncidentVendorEngagements', 'IncidentVendorCapacityCommitments', 'IncidentVendorQuotes'], 'IncidentVendors', V, H + 'CanOrderVendor()')

PV = any_('CanViewIncidentProcurement')
ui(['VendorAgreements', 'VendorPaymentPlans', 'PurchaseRequisitions', 'PurchaseApprovals', 'IncidentCommitments', 'PurchaseReceipts', 'VendorInvoiceReferences', 'PaymentPackages',
    'PaymentPackageLines'], 'IncidentProcurement', PV, H + 'CanManageIncidentProcurement()', protected=H + 'CanViewVendorProtected()',
   ro=('PurchaseApprovals', 'PaymentPackageLines'))
UI['IncidentCommitments']['write'] = H + 'CanCommitIncidentProcurement()'
UI['PurchaseRequisitions']['transition'] = '(s) => s is 2 or 3 ? %sCanApproveIncidentProcurement() : %sCanManageIncidentProcurement()' % (H, H)
UI['VendorInvoiceReferences']['transition'] = '(s) => s is 3 or 4 ? %sCanApproveIncidentProcurement() : %sCanManageIncidentProcurement()' % (H, H)
ui(['IncidentBudgets', 'IncidentBudgetLines', 'IncidentFundingAuthorizations', 'IncidentSpendingDelegations', 'IncidentFundingCodes', 'IncidentCostGroups'], 'IncidentBudget',
   H + 'CanViewIncidentBudget()', H + 'CanManageIncidentBudget()', ro=('IncidentFundingAuthorizations',))
UI['IncidentSpendingDelegations']['write'] = H + 'CanApproveIncidentBudget()'
UI['IncidentBudgets']['transition'] = '(s) => s == 1 ? %sCanApproveIncidentBudget() : %sCanManageIncidentBudget()' % (H, H)
TIME_T = '(s) => s == 2 ? %sCanCertifyIncidentTime() : s is 3 or 5 ? %sCanApproveIncidentTime() : %sCanCreateIncidentTime()' % (H, H, H)
ui(['IncidentTimeRecords', 'IncidentTimeAdjustments', 'IncidentEquipmentUseRecords', 'IncidentExpenses'], 'IncidentTime', H + 'CanViewIncidentTime()', H + 'CanCreateIncidentTime()',
   protected=H + 'CanViewIncidentTime()', ro=('IncidentTimeAdjustments',), transition=TIME_T)
UI['IncidentExpenses']['transition'] = '(s) => s is 2 or 3 ? %sCanApproveIncidentTime() : %sCanCreateIncidentTime()' % (H, H)
ui(['IncidentRateSnapshots', 'IncidentCostTransactions', 'IncidentAccrualSnapshots', 'IncidentCostProjections', 'IncidentCostShareAgreements', 'IncidentCostShareRules',
    'IncidentApportionmentRuns', 'IncidentCostAllocations'], 'IncidentCost', H + 'CanViewIncidentCost()', H + 'CanManageIncidentCost()',
   ro=('IncidentRateSnapshots', 'IncidentCostTransactions', 'IncidentAccrualSnapshots', 'IncidentCostProjections', 'IncidentApportionmentRuns', 'IncidentCostAllocations'))
ui(['IncidentClaims'], 'IncidentClaims', H + 'CanViewIncidentClaim()', H + 'CanManageIncidentClaim()', protected=H + 'CanViewIncidentClaimProtected()')
ui(['IncidentCloseoutChecklistItems'], 'IncidentCloseout', V, H + 'CanCloseIncidentActivation()')
ui(['IncidentDisasterDeclarations', 'IncidentRecoveryProjects', 'IncidentRecoveryDeadlines'], 'IncidentRecovery', H + 'CanViewIncidentCost()', H + 'CanManageIncidentCost()')
LIC = any_('CanViewIncidentProcurement', 'CanViewIncidentTime', 'CanViewIncidentCost')
ui(['IncidentIntakeItems', 'IncidentIntakeFiles'], 'IncidentIntake', LIC, LIC, protected=LIC, ro=('IncidentIntakeFiles',))
CBV = any_('CanViewCoordinationBoard')
ui(['CoordinationBoards', 'CoordinationBoardLanes', 'CoordinationShareGrants'], 'CoordinationBoards', CBV, H + 'CanManageCoordinationBoard()')
UI['CoordinationShareGrants']['write'] = H + 'CanShareCoordinationBoard()'
ui(['IncidentLifelineStatuses', 'IncidentOpenActions', 'IncidentDecisions', 'IncidentSituationalFeatures'], 'IncidentSituation', CBV, H + 'CanManageCoordinationBoard()')

SYSTEM = {'DepartmentId', 'IsDeleted', 'RowVersion', 'IsProtected', 'ProtectedCatalogVersion', 'AddedOn', 'AddedByUserId', 'EditedOn', 'EditedByUserId'}
READONLY_NAMES = {'RunId', 'SourceId', 'TargetId', 'TeamActivationBatchId', 'EvidenceArtifactId', 'IncidentCommandId', 'IncidentNeedId', 'IncidentRoleAssignmentId',
                  'IncidentAdHocPersonnelId', 'IncidentAdHocUnitId', 'CurrentResourceAssignmentId', 'ResourceOrderId', 'ResourceOrderFillId', 'Cursor', 'TokenHash',
                  'OneTimeCodeHash', 'Checksum', 'InputChecksum', 'OutputChecksum', 'SnapshotArtifactId', 'ProviderMessageId', 'CheckInCode', 'LastUsedOn', 'UseCount',
                  'VerifiedOn', 'RevokedOn', 'RevokedByUserId', 'LastAccessedOn', 'NotifiedOn', 'DeadlineNotifiedOn', 'StaleSince', 'LastSourceOn', 'SlaBreached',
                  'IsSplitPurchaseWarning', 'OpenedOn', 'OpenedByUserId', 'ClosedOn', 'ClosedByUserId', 'SealedOn', 'SealedByUserId', 'ReopenedOn', 'ReopenedByUserId',
                  'ApprovedOn', 'ApprovedByUserId', 'CertifiedOn', 'CertifiedByUserId', 'FrozenOn', 'FrozenByUserId', 'CommittedOn', 'CommittedByUserId',
                  'AcceptedOn', 'AcceptedByUserId', 'FiledOn', 'FiledByUserId', 'IsSystemGenerated', 'IsDraft', 'EnrollmentSource', 'AuthorityProfileSnapshotId',
                  'SupersededByPlanVersionId', 'CopiedFromPeriodId', 'CarriedFromObjectiveId', 'TimeoutOn', 'MatchVariance', 'LineCount', 'TotalAmount', 'SubmittedOn',
                  'DistributedOn', 'SnapshottedOn', 'AdjustsTimeRecordId', 'AdjustsRecordId', 'AdjustsTransactionId', 'AdjustsAllocationId', 'Version', 'VersionNumber'}
SPECIAL_REFS = {
    'ParentActivationId': 'IncidentActivations', 'MemberActivationId': 'IncidentActivations', 'ParentNodeId': 'IncidentOrganizationNodes',
    'SupervisorAssignmentId': 'IncidentPositionAssignments', 'TrainingSpecialistAssignmentId': 'IncidentPositionAssignments',
    'ComponentResourceId': 'IncidentResources', 'LeaderParticipantId': 'IncidentParticipants', 'RequesterParticipantId': 'IncidentParticipants',
    'RateSnapshotId': 'IncidentRateSnapshots', 'FromBudgetLineId': 'IncidentBudgetLines', 'SourceProfileId': 'DepartmentIncidentAuthorityProfiles',
    'SourceTemplateId': 'IncidentMissionPackages', 'AdjustingTimeRecordId': 'IncidentTimeRecords',
}
CONTACT_COLS = {'ContactId', 'PartnerContactId', 'ClaimantContactId'}

entities = {t.entity: t.table for t in gen_tables()}

def field(t, c):
    n = c.name
    if n in SYSTEM or n == t.key or (n == 'IncidentActivationId' and t.scope == 'act') or c.type == 'bin' or n.endswith('Json') or n == 'Cursor':
        return None
    if t.table == 'IncidentActivations':
        pass
    readonly = n in READONLY_NAMES
    status_field = LIFECYCLE.get(t.table)
    if n == status_field:
        readonly = True
    inp, enum, ref, maxlen = None, None, None, 0
    if c.enum:
        inp, enum = 'Enum', c.enum
    elif c.type == 'bool':
        inp = 'Bool'
    elif c.type == 'int':
        inp = 'Unit' if n == 'UnitId' else 'Group' if n == 'OwningGroupId' else 'Int'
    elif c.type == 'long':
        inp = 'Int'; readonly = True
    elif c.type == 'money':
        inp = 'Money'
    elif c.type in ('qty', 'pct'):
        inp = 'Decimal'
    elif c.type == 'dt':
        inp = 'DateTime'
    elif c.type == 'date':
        inp = 'Date'
    elif c.type == 'text':
        inp = 'TextArea'
    elif c.type.startswith('s'):
        inp = 'Text'; maxlen = int(c.type[1:])
    elif c.type == 'ic':
        inp = 'Text'; readonly = True
    elif c.type == 'user':
        if n in CONTACT_COLS:
            inp = 'Contact'
        else:
            inp = 'User'
    elif c.type == 'ref':
        if n == 'VendorId':
            inp = 'Vendor'
        elif n in SPECIAL_REFS:
            inp, ref = 'Ref', SPECIAL_REFS[n]
        elif n == 'IncidentActivationId':
            inp, ref = 'Ref', 'IncidentActivations'
        elif n.endswith('Id') and n[:-2] in entities:
            inp, ref = 'Ref', entities[n[:-2]]
        else:
            inp = 'Text'; maxlen = 36
    if readonly:
        pass
    return dict(name=n, input=inp, enum=enum, ref=ref, maxlen=maxlen, readonly=readonly, required=(not c.null and c.type not in ('bool', 'int', 'long', 'money', 'qty', 'pct') and not readonly),
                prot=bool(c.prot), nullable=c.null, filter=(c.enum is not None or c.type == 'bool') and not c.prot)

def list_columns(t, fields):
    label = label_column(t)
    picked = []
    names = [f['name'] for f in fields]
    if label in names: picked.append(label)
    st = LIFECYCLE.get(t.table)
    if st and st in names and st not in picked: picked.append(st)
    for f in fields:
        if len(picked) >= 6: break
        if f['name'] in picked or f['prot'] or f['input'] in ('TextArea',) or f['name'].endswith('Notes'):
            continue
        if f['input'] in ('Enum', 'Ref', 'Date', 'DateTime', 'Money', 'Decimal', 'Int', 'User', 'Unit', 'Vendor', 'Contact', 'Text', 'Bool'):
            picked.append(f['name'])
    return picked

def cs_bool(b): return 'true' if b else 'false'

def gen():
    L = ['// <auto-generated>Generated from the Incident Back Office schema spec (large-incident-logistics-finance-plan.md §11, §14.7). Regenerate, do not hand-edit.</auto-generated>',
         'using System;', 'using System.Collections.Generic;', 'using Resgrid.Model.IncidentLogistics;', 'using Resgrid.Model.Services;', 'using Resgrid.Web.Helpers;', '',
         'namespace Resgrid.Web.Areas.User.Models.IncidentBackOffice', '{',
         '\t/// <summary>Generic desk descriptors of every Incident Back Office table: fields, references, list columns and the claims that gate them.</summary>',
         '\tpublic static partial class IncidentLedgerCatalog', '\t{',
         '\t\tpublic static readonly IReadOnlyDictionary<string, LedgerKindDescriptor> All = Build();', '',
         '\t\tpublic static LedgerKindDescriptor For<T>() => All[typeof(T).Name];', '',
         '\t\tpublic static LedgerKind<T> Kind<T>(IIncidentLedgerService<T> service) where T : class, IIncidentLedgerEntity, new() => new LedgerKind<T>(service, For<T>());', '',
         '\t\tprivate static LedgerField F(string name, LedgerInput input, bool required = false, int max = 0, Type enumType = null, string refTable = null, bool list = false, bool prot = false, bool ro = false, bool nullable = false, bool filter = false) =>',
         '\t\t\tnew LedgerField { Name = name, Input = input, Required = required, MaxLength = max, EnumType = enumType, RefTable = refTable, InList = list, Protected = prot, ReadOnly = ro, Nullable = nullable, Filter = filter };', '',
         '\t\tprivate static Dictionary<string, LedgerKindDescriptor> Build()', '\t\t{', '\t\t\tvar all = new Dictionary<string, LedgerKindDescriptor>(StringComparer.Ordinal);']
    for t in gen_tables():
        u = UI[t.table]
        fields = [f for f in (field(t, c) for c in full_cols(t)) if f]
        cols = list_columns(t, fields)
        scope = 'Activation' if t.scope == 'act' else ('DepartmentOrActivation' if any(c.name == 'IncidentActivationId' for c in t.cols) else 'Department')
        label = label_column(t)
        search = [f['name'] for f in fields if f['input'] == 'Text' and not f['prot'] and not f['readonly']][:3]
        if label in [f['name'] for f in fields] and label not in search and any(f['name'] == label and f['input'] == 'Text' for f in fields):
            search = [label] + search[:2]
        order = 'AddedOn'; desc = True
        names = [f['name'] for f in fields]
        for candidate, d in (('Number', False), ('SortOrder', False), ('SlotOrder', False), ('RuleOrder', False), ('StartOn', True), ('WindowStart', True), ('FirstNight', False)):
            if candidate in names:
                order, desc = candidate, d
                break
        L.append('\t\t\tall["%s"] = new LedgerKindDescriptor' % t.entity)
        L.append('\t\t\t{')
        L.append('\t\t\t\tKey = "%s", Table = "%s", EntityType = typeof(%s), Controller = "%s", Scope = LedgerScope.%s, Track = "%s",' % (t.entity, t.table, t.entity, u['controller'], scope, t.track))
        L.append('\t\t\t\tOrderBy = "%s", Descending = %s, IsReadOnly = %s, AppendOnly = %s, CanDelete = %s, LabelField = "%s", StatusField = %s,' % (
            order, cs_bool(desc), cs_bool(u['ro']), cs_bool(u.get('append', False)), cs_bool(u['delete'] and t.soft), label, ('"%s"' % LIFECYCLE[t.table]) if t.table in LIFECYCLE else 'null'))
        L.append(('				SearchColumns = new[] { %s },' % ', '.join('"%s"' % s for s in search)) if search else '				SearchColumns = Array.Empty<string>(),')
        L.append('\t\t\t\tCanRead = () => %s, CanWrite = () => %s, CanSeeProtected = () => %s,' % (u['read'], u['write'], u['protected']))
        if u.get('transition'):
            L.append('\t\t\t\tCanTransitionTo = %s,' % u['transition'])
        L.append('\t\t\t\tFields = new[]')
        L.append('\t\t\t\t{')
        fl = []
        for f in fields:
            args = ['"%s"' % f['name'], 'LedgerInput.%s' % f['input']]
            opt = []
            if f['required']: opt.append('required: true')
            if f['maxlen']: opt.append('max: %d' % f['maxlen'])
            if f['enum']: opt.append('enumType: typeof(%s)' % f['enum'])
            if f['ref']: opt.append('refTable: "%s"' % f['ref'])
            if f['name'] in cols: opt.append('list: true')
            if f['prot']: opt.append('prot: true')
            if f['readonly']: opt.append('ro: true')
            if f['nullable']: opt.append('nullable: true')
            if f['filter']: opt.append('filter: true')
            fl.append('\t\t\t\t\tF(%s)' % ', '.join(args + opt))
        L.append(',\n'.join(fl))
        L.append('\t\t\t\t}')
        L.append('\t\t\t};')
    L += ['\t\t\treturn all;', '\t\t}', '\t}', '}']
    with io.open(OUT, 'w', encoding='utf-8', newline='') as f:
        f.write('\r\n'.join(L) + '\r\n')

def finalize():
    for t in APPEND:
        UI[t]['ro'] = False
        UI[t]['append'] = True
        UI[t]['delete'] = False
    for t in SYSTEM_ONLY:
        UI[t]['ro'] = True
        UI[t]['delete'] = False

finalize()

if __name__ == '__main__':
    missing = [t.table for t in gen_tables() if t.table not in UI]
    if missing:
        raise SystemExit('missing UI config: %s' % missing)
    gen()
    print('ok', len(gen_tables()))
