# Incident Back Office schema spec (large-incident-logistics-finance-plan.md rev 4.1, §11).
# Consumed by gen.py to emit model classes, dual-dialect migrations, cleanup lists and ADP bindings.

class C:
    def __init__(self, name, type, null=False, doc=None, enum=None, prot=None, default=None):
        self.name = name; self.type = type; self.null = null; self.doc = doc; self.enum = enum
        self.prot = prot  # (classification, family, purpose-note) for ADP-catalogued text columns
        self.default = default

class T:
    def __init__(self, table, entity, track, mig, file, doc, cols, scope='act', mutable=True, soft=True, ver=True,
                 idx=None, uidx=None, notmapped=None, extra_sql=None, extra_pg=None):
        self.table = table; self.entity = entity; self.track = track; self.mig = mig; self.file = file
        self.doc = doc; self.cols = cols; self.scope = scope; self.mutable = mutable; self.soft = soft; self.ver = ver
        self.idx = idx or []; self.uidx = uidx or []; self.notmapped = notmapped or []
        self.extra_sql = extra_sql or []; self.extra_pg = extra_pg or []

    @property
    def key(self):
        return self.entity + 'Id'

    @property
    def adp(self):
        return any(c.prot for c in self.cols)

PII_P = ('Pii', 'Personnel')
PHI_P = ('Phi', 'Personnel')
SENS_O = ('Sensitive', 'Operational')
PII_C = ('Pii', 'Contacts')

TABLES = []
def add(*a, **k):
    t = T(*a, **k); TABLES.append(t); return t

# ---------------------------------------------------------------- LI-A Foundation (M0290)
A = 'A'
add('IncidentActivations', 'IncidentActivation', A, 290, 'ActivationEntities',
    "The incident header this module owns (plan §9.0): \"this department is running the back office for this incident\". Kind, complex membership, read-only Call / IC board links, short code, zone, currency, units, lifecycle and the exercise flag. Every incident-scoped table hangs from it.",
    [C('Kind', 'int', enum='IncidentActivationKinds'),
     C('ParentActivationId', 'ref', True, 'Complex membership: the parent Complex activation.'),
     C('CallId', 'int', True, 'Read-only link to the dispatch Call; RMS per-incident numbering anchor.'),
     C('IncidentCommandId', 'ic', True, 'Read-only link to the IC board (one activation per board).'),
     C('Name', 's256'), C('ShortCode', 's32', doc='Unique per department among live rows; used in local numbering and printed artifacts.'),
     C('ComplexityType', 'int', True, 'ICS complexity Type 1..5.'),
     C('TimeZoneId', 's100'), C('Currency', 's3'), C('MeasurementSystem', 's10'),
     C('Status', 'int', enum='IncidentActivationStatuses'),
     C('IsExercise', 'bool'),
     C('AuthorityProfileSnapshotId', 'ref', True),
     C('Description', 'text', True),
     C('OpenedOn', 'dt', True), C('OpenedByUserId', 'user', True),
     C('ClosedOn', 'dt', True), C('ClosedByUserId', 'user', True),
     C('SealedOn', 'dt', True), C('SealedByUserId', 'user', True),
     C('ReopenedOn', 'dt', True), C('ReopenedByUserId', 'user', True),
     C('ReopenReason', 'text', True)],
    scope='dept',
    idx=[('Status', ['DepartmentId', 'Status']), ('Call', ['DepartmentId', 'CallId']), ('Parent', ['DepartmentId', 'ParentActivationId'])],
    uidx=[('ShortCode', ['DepartmentId', 'ShortCode'], 'live'), ('Board', ['DepartmentId', 'IncidentCommandId'], 'live+IncidentCommandId')])

add('DepartmentIncidentAuthorityProfiles', 'DepartmentIncidentAuthorityProfile', A, 290, 'ActivationEntities',
    "A department's per-domain authority configuration (plan §7): which domains Resgrid owns, which an external system owns, and which are shared and reconciled. An activation snapshots it so its rules cannot change silently mid-incident.",
    [C('Name', 's200'), C('IsDefault', 'bool'), C('Description', 'text', True)], scope='dept')

add('IncidentDomainAuthorities', 'IncidentDomainAuthority', A, 290, 'ActivationEntities',
    "One authority domain row of a profile or of an activation's snapshot (plan §7.1).",
    [C('OwnerKind', 'int', enum='IncidentAuthorityOwnerKinds'), C('OwnerId', 'ref'),
     C('Domain', 'int', enum='IncidentAuthorityDomains'), C('Mode', 'int', enum='IncidentAuthorityModes'),
     C('SourceSystem', 's100', True), C('ConnectorState', 'int', enum='IncidentConnectorStates'),
     C('Precedence', 'int'), C('Notes', 'text', True)],
    scope='dept', soft=False, ver=False, idx=[('Owner', ['DepartmentId', 'OwnerKind', 'OwnerId'])])

add('IncidentAuthorityProfileSnapshots', 'IncidentAuthorityProfileSnapshot', A, 290, 'ActivationEntities',
    "The authority profile as it stood when an activation took it (plan §7). Immutable; a new profile choice takes a new snapshot.",
    [C('SourceProfileId', 'ref', True), C('Name', 's200'), C('TakenOn', 'dt'), C('TakenByUserId', 'user', True)],
    mutable=False, soft=False, ver=False)

add('IncidentReconciliationItems', 'IncidentReconciliationItem', A, 290, 'ActivationEntities',
    "A conflict between local and external authority queued for a named owner (plan §7, risk 14).",
    [C('Domain', 'int', enum='IncidentAuthorityDomains'), C('SubjectType', 's32'), C('SubjectId', 'ref', True),
     C('SourceSystem', 's100', True), C('ExternalIdentifier', 's200', True), C('Description', 'text', True),
     C('Status', 'int', enum='IncidentReconciliationStatuses'), C('OwnerUserId', 'user', True),
     C('ResolvedOn', 'dt', True), C('ResolvedByUserId', 'user', True), C('Resolution', 'text', True)],
    idx=[('Status', ['DepartmentId', 'IncidentActivationId', 'Status'])])

add('DepartmentIncidentTeams', 'DepartmentIncidentTeam', A, 290, 'TeamEntities',
    "A standing team roster (plan §9.2): county or complex IMT, an all-hazards IMT on rotation, an EOC section roster, a support team. Readiness is computed, never stored.",
    [C('Name', 's200'), C('TeamType', 'int', enum='IncidentTeamTypes'), C('IsActive', 'bool', default=True),
     C('OwningGroupId', 'int', True), C('Description', 'text', True)], scope='dept')

add('DepartmentIncidentTeamRotations', 'DepartmentIncidentTeamRotation', A, 290, 'TeamEntities',
    "An on-call window for a standing team (plan §9.2).",
    [C('DepartmentIncidentTeamId', 'ref'), C('WindowStart', 'dt'), C('WindowEnd', 'dt'), C('Notes', 'text', True)],
    scope='dept', idx=[('Team', ['DepartmentId', 'DepartmentIncidentTeamId', 'WindowStart'])])

add('DepartmentIncidentTeamSlots', 'DepartmentIncidentTeamSlot', A, 290, 'TeamEntities',
    "One position on a standing roster (plan §9.2); candidates are ranked under it.",
    [C('DepartmentIncidentTeamId', 'ref'), C('IncidentPositionDefinitionId', 'ref'), C('SlotOrder', 'int'),
     C('Label', 's200', True), C('Notes', 'text', True)],
    scope='dept', idx=[('Team', ['DepartmentId', 'DepartmentIncidentTeamId', 'SlotOrder'])])

add('DepartmentIncidentTeamCandidates', 'DepartmentIncidentTeamCandidate', A, 290, 'TeamEntities',
    "A ranked candidate for a roster slot: a Resgrid user or an external person kept as a Contact, never free text (plan §9.2).",
    [C('DepartmentIncidentTeamId', 'ref'), C('DepartmentIncidentTeamSlotId', 'ref'),
     C('CandidateKind', 'int', enum='IncidentTeamCandidateKinds'), C('Priority', 'int'),
     C('UserId', 'user', True), C('ContactId', 'user', True), C('Notes', 'text', True)],
    scope='dept', idx=[('Slot', ['DepartmentId', 'DepartmentIncidentTeamSlotId', 'Priority'])])

add('IncidentOperationalPeriods', 'IncidentOperationalPeriod', A, 290, 'PlanningEntities',
    "A numbered operational period (plan §9.1): the planning unit for forecasts, the IAP and cost. A planning window, not a staffing roster.",
    [C('Number', 'int'), C('StartOn', 'dt'), C('EndOn', 'dt'), C('TimeZoneId', 's100'), C('BriefingOn', 'dt', True),
     C('Status', 'int', enum='IncidentPeriodStatuses'), C('PlannerUserId', 'user', True),
     C('IsFrozen', 'bool'), C('FrozenOn', 'dt', True), C('FrozenByUserId', 'user', True),
     C('ApprovedOn', 'dt', True), C('ApprovedByUserId', 'user', True),
     C('CopiedFromPeriodId', 'ref', True), C('Notes', 'text', True)],
    idx=[('Window', ['DepartmentId', 'IncidentActivationId', 'StartOn'])],
    uidx=[('Number', ['DepartmentId', 'IncidentActivationId', 'Number'], 'live')])

add('IncidentPlanVersions', 'IncidentPlanVersion', A, 290, 'PlanningEntities',
    "An IAP assembly manifest for a period (plan §9.16): which RMS records and revisions are included, page order, maps, distribution and print job. Approval and immutability are RMS's.",
    [C('IncidentOperationalPeriodId', 'ref'), C('VersionNumber', 'int'),
     C('Status', 'int', enum='IncidentPlanVersionStatuses'), C('RecordId', 'ref', True, 'The RMS record finalized over this manifest.'),
     C('ManifestJson', 'text', True), C('DistributionJson', 'text', True), C('PrintCopies', 'int'),
     C('ApprovedOn', 'dt', True), C('ApprovedByUserId', 'user', True), C('DistributedOn', 'dt', True),
     C('SupersededByPlanVersionId', 'ref', True)],
    idx=[('Period', ['DepartmentId', 'IncidentActivationId', 'IncidentOperationalPeriodId'])])

add('IncidentPlanWeatherBlocks', 'IncidentPlanWeatherBlock', A, 290, 'PlanningEntities',
    "The structured weather block of a plan version, snapshotted at approval (plan §9.21). No new weather ingestion; references existing WeatherAlert rows.",
    [C('IncidentPlanVersionId', 'ref', True), C('IncidentOperationalPeriodId', 'ref', True),
     C('ForecastSource', 's200', True), C('IssuedOn', 'dt', True), C('ForecastPeriod', 's200', True),
     C('TemperatureSummary', 's500', True), C('HumiditySummary', 's500', True), C('WindSummary', 's500', True),
     C('PrecipitationSummary', 's500', True), C('SpotForecastReference', 's500', True),
     C('ActiveAlertIdsJson', 'text', True), C('Narrative', 'text', True), C('SnapshottedOn', 'dt', True)],
    soft=False, idx=[('Plan', ['DepartmentId', 'IncidentPlanVersionId'])])

add('IncidentObjectives', 'IncidentObjective', A, 290, 'PlanningEntities',
    "An incident objective carried across periods (plans §9.1 and §9.25): becomes an ICS 202 line when the plan is published; shared with LI-D boards.",
    [C('IncidentOperationalPeriodId', 'ref', True), C('Number', 'int'), C('Text', 'text'),
     C('Status', 'int', enum='IncidentObjectiveStatuses'), C('CarriedFromObjectiveId', 'ref', True)],
    idx=[('Period', ['DepartmentId', 'IncidentActivationId', 'IncidentOperationalPeriodId'])])

add('IncidentPositionDefinitions', 'IncidentPositionDefinition', A, 290, 'OrganizationEntities',
    "A standard or custom incident position (plan §9.2). Department catalog when IncidentActivationId is null, incident-defined otherwise. Requirements reuse the Phase D certification shape keyed by CertificationType.Code.",
    [C('IncidentActivationId', 'ref', True), C('Code', 's32'), C('Name', 's200'),
     C('Section', 'int', enum='IncidentSections'), C('Branch', 's100', True), C('UnitName', 's100', True),
     C('Description', 'text', True), C('SourceCatalog', 'int', enum='IncidentPositionSources'), C('SourceVersion', 's32', True),
     C('RequirementsJson', 'text', True, 'Phase D requirement shape: [{Code, IsMandatory, AnyOfGroup, AllowTrainee, GraceDays}].'),
     C('DefaultWorkspace', 's64', True), C('DefaultIncidentRoleType', 'int', True, 'IC board role label this position maps to (read by IC, never written by this module).'),
     C('SupervisorCode', 's32', True), C('IsStandard', 'bool'), C('IsActive', 'bool', default=True), C('SortOrder', 'int')],
    scope='dept', idx=[('Section', ['DepartmentId', 'Section', 'SortOrder'])],
    uidx=[('Code', ['DepartmentId', 'Code'], 'live+catalog')])

add('IncidentPositionCapabilities', 'IncidentPositionCapability', A, 290, 'OrganizationEntities',
    "A default desk/queue capability bundle entry for a position (plan §9.2). Confers no authorization: desktop access is ResgridResources policies.",
    [C('IncidentPositionDefinitionId', 'ref'), C('CapabilityKey', 's64')],
    scope='dept', soft=False, ver=False, idx=[('Position', ['DepartmentId', 'IncidentPositionDefinitionId'])])

add('IncidentOrganizationNodes', 'IncidentOrganizationNode', A, 290, 'OrganizationEntities',
    "An ICS organization chart node for a period (plan §9.2) — section, branch, division/group or unit. Not CommandStructureNode, which stays IC's tactical lane model.",
    [C('IncidentOperationalPeriodId', 'ref', True), C('ParentNodeId', 'ref', True), C('Kind', 'int', enum='IncidentOrganizationNodeKinds'),
     C('Name', 's200'), C('Section', 'int', enum='IncidentSections'), C('SortOrder', 'int')],
    idx=[('Period', ['DepartmentId', 'IncidentActivationId', 'IncidentOperationalPeriodId'])])

add('IncidentPositionAssignments', 'IncidentPositionAssignment', A, 290, 'OrganizationEntities',
    "Who holds an incident position, when (plan §9.2, §12.4): a Resgrid user, an incident participant or a placeholder; effective-dated and period-scoped, with acting, deputy, trainee and day/night variants. The IC board role link lives here, never on the IC row.",
    [C('IncidentOperationalPeriodId', 'ref', True), C('IncidentPositionDefinitionId', 'ref'), C('IncidentOrganizationNodeId', 'ref', True),
     C('SubjectType', 'int', enum='IncidentAssignmentSubjectTypes'), C('UserId', 'user', True), C('IncidentParticipantId', 'ref', True),
     C('PlaceholderLabel', 's200', True), C('AssignmentKind', 'int', enum='IncidentAssignmentKinds'), C('Shift', 'int', enum='IncidentShiftPatterns'),
     C('StartOn', 'dt'), C('EndOn', 'dt', True), C('SupervisorAssignmentId', 'ref', True),
     C('IncidentRoleAssignmentId', 'ic', True), C('IsTrainee', 'bool'), C('TaskBookReference', 's100', True),
     C('TaskBookExpiresOn', 'date', True), C('TrainingSpecialistAssignmentId', 'ref', True), C('TraineePriority', 'int', True),
     C('QualificationState', 'int', enum='IncidentQualificationStates'), C('QualificationNote', 's500', True),
     C('TeamActivationBatchId', 'ref', True, 'Set when the assignment was committed by a one-action team activation.'),
     C('Notes', 'text', True)],
    idx=[('Position', ['DepartmentId', 'IncidentActivationId', 'IncidentPositionDefinitionId', 'StartOn']),
         ('User', ['DepartmentId', 'UserId', 'StartOn']), ('Period', ['DepartmentId', 'IncidentActivationId', 'IncidentOperationalPeriodId'])])

add('IncidentParticipants', 'IncidentParticipant', A, 290, 'ParticipantEntities',
    "A supported person on the incident (plan §9.2, §9.5): a department member linked by UserId, an external person, or an IC ad-hoc person enriched here. Support status is owned here; operational status stays IC/Dispatch's and is never stored on this row.",
    [C('UserId', 'user', True), C('ContactId', 'user', True), C('IncidentAdHocPersonnelId', 'ic', True),
     C('DisplayName', 's200'), C('Agency', 's200', True), C('HomeUnit', 's200', True), C('PositionTitle', 's200', True),
     C('OrderNumber', 's100', True), C('RequestNumber', 's100', True),
     C('PaymentClass', 'int', enum='IncidentPaymentClasses'), C('SupportStatus', 'int', enum='IncidentSupportStatuses'),
     C('EnrollmentSource', 'int', enum='IncidentEnrollmentSources'), C('IsDraft', 'bool', doc='AutoDraft participants stay flagged until a person confirms them.'),
     C('ExpectedArrivalOn', 'dt', True), C('ArrivedOn', 'dt', True), C('CheckedInOn', 'dt', True),
     C('AssignmentStartOn', 'date', True), C('MaxAssignmentDays', 'int', True), C('TimeoutOn', 'date', True),
     C('TentativeReleaseOn', 'dt', True), C('ReleasedOn', 'dt', True), C('DepartedOn', 'dt', True),
     C('TravelMode', 's64', True), C('IsSelfSufficient', 'bool'), C('NeedsLodging', 'bool'), C('NeedsMeals', 'bool'),
     C('CheckInCode', 's32', True, 'Opaque personal check-in code printed as the QR the desk reader scans.'),
     C('PersonalPhone', 'text', True, prot=PII_P), C('PersonalEmail', 'text', True, prot=PII_P),
     C('HomeAddress', 'text', True, prot=PII_P), C('DateOfBirth', 'text', True, prot=PII_P),
     C('ExternalIdentifier', 'text', True, prot=PII_P), C('Notes', 'text', True, prot=SENS_O)],
    idx=[('Status', ['DepartmentId', 'IncidentActivationId', 'SupportStatus']), ('User', ['DepartmentId', 'UserId'])],
    uidx=[('AdHoc', ['DepartmentId', 'IncidentActivationId', 'IncidentAdHocPersonnelId'], 'live+IncidentAdHocPersonnelId'),
          ('Member', ['DepartmentId', 'IncidentActivationId', 'UserId'], 'live+UserId'),
          ('CheckInCode', ['DepartmentId', 'CheckInCode'], 'live+CheckInCode')])

add('IncidentParticipantSensitive', 'IncidentParticipantSensitive', A, 290, 'ParticipantEntities',
    "Protected participant detail (plan §15.3a): emergency contact and the health/accommodation facts behind lodging and feeding flags. The whole row exists to hold protected data.",
    [C('IncidentParticipantId', 'ref'),
     C('EmergencyContactName', 'text', True, prot=PII_P), C('EmergencyContactPhone', 'text', True, prot=PII_P),
     C('EmergencyContactRelationship', 'text', True, prot=PII_P),
     C('MedicalAccommodation', 'text', True, prot=PHI_P), C('AccessibilityNeed', 'text', True, prot=PHI_P),
     C('AllergenDetail', 'text', True, prot=PHI_P), C('DietaryRestriction', 'text', True, prot=PHI_P),
     C('DietaryCategoryCode', 'text', True, prot=PHI_P)],
    soft=False, uidx=[('Participant', ['DepartmentId', 'IncidentParticipantId'], None)])

add('IncidentParticipantQualifications', 'IncidentParticipantQualification', A, 290, 'ParticipantEntities',
    "A dated qualification snapshot (plan §9.3): from PersonnelCertification for members, keyed or imported with a named source for external people. Never presented as an official national qualification.",
    [C('IncidentParticipantId', 'ref'), C('Code', 's64'), C('Name', 's200', True),
     C('Source', 'int', enum='IncidentQualificationSources'), C('SourceName', 's200', True),
     C('VerificationState', 'int', enum='IncidentVerificationStates'), C('EffectiveOn', 'date', True), C('ExpiresOn', 'date', True),
     C('SnapshotOn', 'dt'), C('CredentialNumber', 'text', True, prot=PII_P), C('VerificationNotes', 'text', True, prot=PII_P)],
    idx=[('Participant', ['DepartmentId', 'IncidentParticipantId'])])

add('IncidentExternalOrganizationRefs', 'IncidentExternalOrganizationRef', A, 290, 'ParticipantEntities',
    "A thin incident-scoped reference to an organization Contact (plan §10 R11): cooperators, host and responding agencies.",
    [C('ContactId', 'user'), C('Role', 'int', enum='IncidentOrganizationRoles'), C('Name', 's200'), C('Notes', 'text', True)])

add('IncidentOperationalSnapshots', 'IncidentOperationalSnapshot', A, 290, 'IntelligenceEntities',
    "One coherent cut of the back-office picture (plan §13.4): metric values carry its id so widgets shown together use one cursor. Rebuildable by definition.",
    [C('Cursor', 'long'), C('TakenOn', 'dt'), C('Kind', 'int', enum='IncidentSnapshotKinds'),
     C('OldestSourceOn', 'dt', True), C('LagSeconds', 'int'), C('DriftDetected', 'bool'), C('SummaryJson', 'text', True)],
    mutable=False, soft=False, ver=False, idx=[('Cursor', ['DepartmentId', 'IncidentActivationId', 'Cursor'])])

add('IncidentMetricValues', 'IncidentMetricValue', A, 290, 'IntelligenceEntities',
    "A versioned metric value at a snapshot cursor (plan §13.4), bucketed by period, night, meal window or horizon. Drill-through re-runs the metric's source query.",
    [C('IncidentOperationalSnapshotId', 'ref'), C('MetricKey', 's64'), C('MetricVersion', 'int'), C('Bucket', 's64', True),
     C('Value', 'qty'), C('Unit', 's16', True), C('SourceCount', 'int'), C('AsOf', 'dt')],
    mutable=False, soft=False, ver=False, idx=[('Snapshot', ['DepartmentId', 'IncidentOperationalSnapshotId', 'MetricKey'])])

add('IncidentDataFreshness', 'IncidentDataFreshness', A, 290, 'IntelligenceEntities',
    "Per-source freshness for an activation (plan §13.4): last observed and last change of each IC/Dispatch and ledger source.",
    [C('SourceKey', 's64'), C('LastObservedOn', 'dt', True), C('LastChangeOn', 'dt', True), C('StalenessSeconds', 'int'), C('IsStale', 'bool'), C('UpdatedOn', 'dt')],
    mutable=False, soft=False, ver=False, uidx=[('Source', ['DepartmentId', 'IncidentActivationId', 'SourceKey'], None)])

add('IncidentSavedDashboardViews', 'IncidentSavedDashboardView', A, 290, 'IntelligenceEntities',
    "A saved desk layout and filter set (plan §10 R15, §9.17).",
    [C('IncidentActivationId', 'ref', True), C('UserId', 'user', True), C('Name', 's200'), C('DeskKey', 's64'), C('FiltersJson', 'text', True), C('IsShared', 'bool')],
    scope='dept', idx=[('User', ['DepartmentId', 'UserId'])])

add('IncidentProjectionCursors', 'IncidentProjectionCursor', A, 290, 'IntelligenceEntities',
    "As-of cursor and contract version backing a §10A.3 projection contract for an activation.",
    [C('ContractKey', 's64'), C('ContractVersion', 'int'), C('Cursor', 'long'), C('OldestSourceOn', 'dt', True), C('UpdatedOn', 'dt')],
    mutable=False, soft=False, ver=False, uidx=[('Contract', ['DepartmentId', 'IncidentActivationId', 'ContractKey'], None)])

add('IncidentNotificationLogs', 'IncidentNotificationLog', A, 290, 'IntelligenceEntities',
    "Every incident-position routing resolution (plan §13.8): position, holders resolved and fallback used, so \"why didn't I get the arrival notice\" is answerable from data.",
    [C('EventType', 'int'), C('PositionCode', 's32', True), C('HoldersJson', 'text', True), C('Fallback', 'int', enum='IncidentRoutingFallbacks'),
     C('RecipientCount', 'int'), C('Summary', 's500', True)],
    mutable=False, soft=False, ver=False, idx=[('Sent', ['DepartmentId', 'IncidentActivationId', 'AddedOn'])])

add('IncidentBackOfficeBillingAccounts', 'IncidentBackOfficeBillingAccount', A, 291, None,
    None, [], scope='billing')

# ---------------------------------------------------------------- LI-B Logistics: resources, requests, check-in, demob (M0292)
B = 'B'
add('IncidentResources', 'IncidentResource', B, 292, 'ResourceEntities',
    "A supported resource on the incident (plan §9.3): crew, team, equipment, aircraft, supply or service, with provider, ordering authority, order numbers and counts. Support status is owned here; tactical assignment stays IC's (CurrentResourceAssignmentId is a display-only projection).",
    [C('Kind', 'int', enum='IncidentResourceKinds'), C('Name', 's200'), C('ResourceType', 's100', True), C('Capability', 's200', True),
     C('TypingCode', 's64', True), C('ProviderAgency', 's200', True), C('PayingAgency', 's200', True), C('OrderingAuthority', 's200', True),
     C('OrderNumber', 's100', True), C('RequestNumber', 's100', True), C('HomeLocation', 's200', True), C('LeaderName', 's200', True),
     C('LeaderParticipantId', 'ref', True), C('PersonnelCount', 'int'), C('ApparatusCount', 'int'),
     C('UnitId', 'int', True), C('IncidentAdHocUnitId', 'ic', True), C('IncidentRequestFillId', 'ref', True), C('IncidentMissionPackageId', 'ref', True),
     C('IncidentVendorEngagementId', 'ref', True), C('SupportStatus', 'int', enum='IncidentSupportStatuses'),
     C('CurrentResourceAssignmentId', 'ic', True), C('PaymentClass', 'int', enum='IncidentPaymentClasses'),
     C('ArrivedOn', 'dt', True), C('CheckedInOn', 'dt', True), C('AssignmentStartOn', 'date', True), C('MaxAssignmentDays', 'int', True),
     C('TimeoutOn', 'date', True), C('TimeoutBasis', 'int', enum='IncidentTimeoutBases'), C('TentativeReleaseOn', 'dt', True),
     C('ReleasedOn', 'dt', True), C('DepartedOn', 'dt', True), C('Destination', 's200', True), C('Notes', 'text', True)],
    idx=[('Status', ['DepartmentId', 'IncidentActivationId', 'SupportStatus']), ('Timeout', ['DepartmentId', 'IncidentActivationId', 'TimeoutOn'])],
    uidx=[('AdHoc', ['DepartmentId', 'IncidentActivationId', 'IncidentAdHocUnitId'], 'live+IncidentAdHocUnitId')])

add('IncidentResourceComponents', 'IncidentResourceComponent', B, 292, 'ResourceEntities',
    "A component of a composite resource (strike team, task force, mission package).",
    [C('IncidentResourceId', 'ref'), C('ComponentResourceId', 'ref', True), C('IncidentParticipantId', 'ref', True),
     C('Kind', 'int', enum='IncidentResourceKinds'), C('Name', 's200'), C('Quantity', 'qty'), C('Notes', 'text', True)],
    idx=[('Resource', ['DepartmentId', 'IncidentResourceId'])])

add('IncidentResourceStatusHistory', 'IncidentResourceStatusHistory', B, 292, 'ResourceEntities',
    "Append-only support-status history of a resource or participant (plan §9.5).",
    [C('SubjectType', 'int', enum='IncidentSubjectTypes'), C('SubjectId', 'ref'), C('OldStatus', 'int'), C('NewStatus', 'int'), C('Reason', 's500', True)],
    mutable=False, soft=False, ver=False, idx=[('Subject', ['DepartmentId', 'SubjectId', 'AddedOn'])])

add('IncidentResourceMovements', 'IncidentResourceMovement', B, 292, 'ResourceEntities',
    "A mobilization, reassignment, transfer or demob movement of a resource (plan §9.5).",
    [C('IncidentResourceId', 'ref'), C('Kind', 'int', enum='IncidentMovementKinds'), C('FromLocation', 's200', True), C('ToLocation', 's200', True),
     C('DepartedOn', 'dt', True), C('ArrivedOn', 'dt', True), C('EtaOn', 'dt', True), C('Notes', 'text', True)],
    idx=[('Resource', ['DepartmentId', 'IncidentResourceId'])])

add('IncidentAssignmentExtensions', 'IncidentAssignmentExtension', B, 292, 'ResourceEntities',
    "An authorized maximum-time-allowance extension (plan §9.5): the prior projection is preserved, the predictive board shows what moved and why.",
    [C('SubjectType', 'int', enum='IncidentSubjectTypes'), C('SubjectId', 'ref'), C('PriorTimeoutOn', 'date', True), C('NewTimeoutOn', 'date'),
     C('ApprovedByUserId', 'user', True), C('Reason', 's500')],
    mutable=False, soft=False, ver=False, idx=[('Subject', ['DepartmentId', 'SubjectId'])])

add('IncidentMissionPackages', 'IncidentMissionPackage', B, 292, 'ResourceEntities',
    "A reusable mission package template (department scope, IsTemplate) or its incident snapshot taken at mobilization (plan §9.3).",
    [C('IncidentActivationId', 'ref', True), C('Name', 's200'), C('Code', 's64', True), C('Kind', 'int', enum='IncidentResourceKinds'),
     C('Description', 'text', True), C('ComponentsJson', 'text', True), C('IsTemplate', 'bool'), C('SourceTemplateId', 'ref', True)],
    scope='dept')

add('IncidentMissionPackageEstimates', 'IncidentMissionPackageEstimate', B, 292, 'ResourceEntities',
    "An estimate line of a mission package (personnel, equipment, travel, lodging, meals, commodities...).",
    [C('IncidentMissionPackageId', 'ref'), C('Category', 'int', enum='IncidentEstimateCategories'), C('Quantity', 'qty'), C('Unit', 's16', True),
     C('Amount', 'money', True), C('Currency', 's3', True), C('Notes', 's500', True)],
    scope='dept', soft=False, idx=[('Package', ['DepartmentId', 'IncidentMissionPackageId'])])

add('IncidentResourceRequests', 'IncidentResourceRequest', B, 292, 'RequestEntities',
    "The incident-side resource request (plan §9.4, §12.2): fixed lifecycle, approvals, source channel and the IC need it details. Mutual-aid requests emit a shipped ResourceOrder.",
    [C('IncidentOperationalPeriodId', 'ref', True), C('RequestNumber', 's32'), C('IsLocallyIssued', 'bool', default=True),
     C('IncidentNeedId', 'ic', True), C('IncidentObjectiveId', 'ref', True), C('Priority', 'int', enum='IncidentRequestPriorities'),
     C('Status', 'int', enum='IncidentRequestStatuses'), C('SubStateDetailId', 'int', True), C('Kind', 'int', enum='IncidentResourceKinds'),
     C('ResourceType', 's100', True), C('Capability', 's200', True), C('Quantity', 'qty'), C('UnitOfMeasure', 's16', True),
     C('Substitutions', 's500', True), C('Justification', 'text', True), C('NeedBy', 'dt', True),
     C('DeliveryLocation', 's200', True), C('ReportingLocation', 's200', True), C('ReceivingContact', 's200', True),
     C('FundingCode', 's100', True), C('IncidentBudgetLineId', 'ref', True), C('OwningSection', 'int', enum='IncidentSections'),
     C('SourceChannel', 'int', enum='IncidentSourceChannels'), C('RequestedByUserId', 'user', True), C('SubmittedOn', 'dt', True),
     C('ApprovedOn', 'dt', True), C('ApprovedByUserId', 'user', True), C('OrderedOn', 'dt', True), C('OrderedByUserId', 'user', True),
     C('RejectedReason', 's500', True), C('CancelledReason', 's500', True), C('ClosedOn', 'dt', True),
     C('ResourceOrderId', 'int', True), C('IncidentVendorEngagementId', 'ref', True), C('Notes', 'text', True)],
    idx=[('Status', ['DepartmentId', 'IncidentActivationId', 'Status']), ('Need', ['DepartmentId', 'IncidentNeedId'])],
    uidx=[('Number', ['DepartmentId', 'RequestNumber'], 'live')])

add('IncidentRequestApprovals', 'IncidentRequestApproval', B, 292, 'RequestEntities',
    "One approval decision on a request, append-only.",
    [C('IncidentResourceRequestId', 'ref'), C('Step', 'int'), C('Decision', 'int', enum='IncidentApprovalDecisions'), C('Comment', 's1000', True)],
    mutable=False, soft=False, ver=False, idx=[('Request', ['DepartmentId', 'IncidentResourceRequestId'])])

add('IncidentRequestFills', 'IncidentRequestFill', B, 292, 'RequestEntities',
    "A fill of a request carrying the numbers the sections plan against (plan §9.4): apparatus and personnel counts, kind, ETA and confidence. A fill without counts is flagged incomplete.",
    [C('IncidentResourceRequestId', 'ref'), C('Source', 'int', enum='IncidentSourceChannels'), C('SourceReference', 's100', True),
     C('Provider', 's200', True), C('ResourceKind', 'int', enum='IncidentResourceKinds'), C('ResourceType', 's100', True),
     C('ApparatusCount', 'int'), C('PersonnelCount', 'int'), C('Quantity', 'qty'), C('EtaOn', 'dt', True),
     C('EtaConfidence', 'int', enum='IncidentEtaConfidences'), C('Status', 'int', enum='IncidentFillStatuses'),
     C('AcceptedOn', 'dt', True), C('ArrivedOn', 'dt', True), C('ResourceOrderFillId', 'int', True), C('Notes', 'text', True)],
    idx=[('Request', ['DepartmentId', 'IncidentResourceRequestId']), ('Eta', ['DepartmentId', 'IncidentActivationId', 'EtaOn'])])

add('IncidentFillEtaUpdates', 'IncidentFillEtaUpdate', B, 292, 'RequestEntities',
    "A tracked ETA change on a fill (plan §9.4): never an overwrite, so arrival callouts and forecasts re-derive with history.",
    [C('IncidentRequestFillId', 'ref'), C('PriorEtaOn', 'dt', True), C('NewEtaOn', 'dt', True),
     C('Confidence', 'int', enum='IncidentEtaConfidences'), C('Reason', 's500', True)],
    mutable=False, soft=False, ver=False, idx=[('Fill', ['DepartmentId', 'IncidentRequestFillId'])])

add('IncidentReceipts', 'IncidentReceipt', B, 292, 'RequestEntities',
    "An incident receiving event (plan §9.9): references inventory transactions, never duplicates them.",
    [C('IncidentResourceRequestId', 'ref', True), C('IncidentRequestFillId', 'ref', True), C('ReceivingLocation', 's200', True),
     C('ShipmentReference', 's100', True), C('Carrier', 's200', True), C('ReceivedOn', 'dt'), C('ReceivedByUserId', 'user', True),
     C('Status', 'int', enum='IncidentReceiptStatuses'), C('DiscrepancyKind', 'int', enum='IncidentDiscrepancyKinds'), C('Notes', 'text', True)],
    idx=[('Request', ['DepartmentId', 'IncidentResourceRequestId'])])

add('IncidentReceiptLines', 'IncidentReceiptLine', B, 292, 'RequestEntities',
    "A received line with expected, accepted, rejected and backordered quantities.",
    [C('IncidentReceiptId', 'ref'), C('ItemDescription', 's500'), C('InventoryItemId', 'ref', True), C('InventoryTransactionId', 'ref', True),
     C('QuantityExpected', 'qty'), C('QuantityAccepted', 'qty'), C('QuantityRejected', 'qty'), C('QuantityBackordered', 'qty'),
     C('UnitOfMeasure', 's16', True), C('Damage', 's1000', True)],
    soft=False, idx=[('Receipt', ['DepartmentId', 'IncidentReceiptId'])])

add('IncidentCheckIns', 'IncidentCheckIn', B, 292, 'CheckInEntities',
    "A back-office check-in record (plan §9.5): who arrived, what they brought and what they need. Operational accountability stays IC's.",
    [C('SubjectType', 'int', enum='IncidentSubjectTypes'), C('SubjectId', 'ref'), C('Source', 'int', enum='IncidentCheckInSources'),
     C('CheckedInOn', 'dt'), C('MobilizationOrigin', 's200', True), C('ActualPersonnelCount', 'int', True), C('VehicleDescription', 's200', True),
     C('ReportingPoint', 's200', True), C('FirstWorkDay', 'date', True), C('AssignmentLengthDays', 'int', True),
     C('SpecialRequirements', 's1000', True), C('IsDuplicateCandidate', 'bool'), C('Notes', 'text', True)],
    idx=[('Subject', ['DepartmentId', 'SubjectId']), ('Day', ['DepartmentId', 'IncidentActivationId', 'CheckedInOn'])])

add('IncidentDemobPlans', 'IncidentDemobPlan', B, 292, 'CheckInEntities',
    "The release plan of a resource or participant (plan §9.5): tentative, approved, released, departed. The checklist under it is the gate.",
    [C('SubjectType', 'int', enum='IncidentSubjectTypes'), C('SubjectId', 'ref'), C('Status', 'int', enum='IncidentDemobStatuses'),
     C('TentativeReleaseOn', 'dt', True), C('ApprovedReleaseOn', 'dt', True), C('ApprovedByUserId', 'user', True),
     C('DepartureWindowStart', 'dt', True), C('DepartureWindowEnd', 'dt', True), C('Destination', 's200', True), C('TravelMode', 's64', True),
     C('ReassignmentEligible', 'bool'), C('DepartedOn', 'dt', True), C('Notes', 'text', True)],
    idx=[('Status', ['DepartmentId', 'IncidentActivationId', 'Status']), ('Subject', ['DepartmentId', 'SubjectId'])])

add('IncidentDemobChecklistItems', 'IncidentDemobChecklistItem', B, 292, 'CheckInEntities',
    "One demob gate item with owner, blocking flag and an authorized-waiver path (plan §9.5).",
    [C('IncidentDemobPlanId', 'ref'), C('Category', 'int', enum='IncidentDemobCategories'), C('Name', 's200'), C('OwnerPosition', 's32', True),
     C('IsBlocking', 'bool'), C('Status', 'int', enum='IncidentGateItemStatuses'), C('CompletedOn', 'dt', True), C('CompletedByUserId', 'user', True),
     C('WaivedReason', 's500', True), C('WaivedByUserId', 'user', True)],
    idx=[('Plan', ['DepartmentId', 'IncidentDemobPlanId'])])

# ---------------------------------------------------------------- LI-B support: facilities, lodging, feeding, water, services, ground, comms (M0293)
add('IncidentFacilities', 'IncidentFacility', B, 293, 'FacilityEntities',
    "An incident facility (plan §9.6): ICP, base, camp, helibase, staging, warehouse, feeding or shower site, hotel and more. Location soft-references a Poi; capacity and agreement live here, inspections are records.",
    [C('Name', 's200'), C('FacilityType', 'int', enum='IncidentFacilityTypes'), C('PoiId', 'int', True), C('MapLayerId', 's64', True),
     C('IndoorMapId', 's64', True), C('Address', 's500', True), C('OperatingHours', 's200', True), C('OwnerOperator', 's200', True),
     C('ContactId', 'user', True), C('ContactPhone', 's64', True), C('Status', 'int', enum='IncidentFacilityStatuses'),
     C('SafetyNotes', 'text', True), C('AccessNotes', 'text', True), C('OpenedOn', 'dt', True), C('ClosedOn', 'dt', True)],
    idx=[('Type', ['DepartmentId', 'IncidentActivationId', 'FacilityType'])])

add('IncidentFacilityCapacities', 'IncidentFacilityCapacity', B, 293, 'FacilityEntities',
    "Maximum, usable, used, reserved and out-of-service capacity of one service dimension of a facility, with its alert threshold (plan §9.6).",
    [C('IncidentFacilityId', 'ref'), C('ServiceKind', 'int', enum='IncidentFacilityServiceKinds'), C('MaxCapacity', 'qty'), C('UsableCapacity', 'qty'),
     C('CurrentUse', 'qty'), C('Reserved', 'qty'), C('OutOfService', 'qty'), C('Unit', 's16', True), C('ThresholdPercent', 'pct', True), C('Notes', 's500', True)],
    idx=[('Facility', ['DepartmentId', 'IncidentFacilityId'])])

add('IncidentFacilityAgreements', 'IncidentFacilityAgreement', B, 293, 'FacilityEntities',
    "The land-use, lease or direct-bill arrangement behind a facility; the signed document is an RMS record.",
    [C('IncidentFacilityId', 'ref'), C('VendorAgreementId', 'ref', True), C('AgreementKind', 'int', enum='IncidentFacilityAgreementKinds'),
     C('RecordId', 'ref', True), C('StartsOn', 'date', True), C('EndsOn', 'date', True), C('Terms', 'text', True)],
    idx=[('Facility', ['DepartmentId', 'IncidentFacilityId'])])

add('IncidentFacilityServices', 'IncidentFacilityService', B, 293, 'FacilityEntities',
    "A service a facility provides and its readiness (plan §9.6).",
    [C('IncidentFacilityId', 'ref'), C('ServiceKind', 'int', enum='IncidentFacilityServiceKinds'), C('Status', 'int', enum='IncidentServiceReadiness'),
     C('Schedule', 's200', True), C('IncidentVendorEngagementId', 'ref', True), C('Notes', 's500', True)],
    idx=[('Facility', ['DepartmentId', 'IncidentFacilityId'])])

add('IncidentLodgingProperties', 'IncidentLodgingProperty', B, 293, 'LodgingEntities',
    "A hotel, dormitory or camp sleeping property (plan §9.7) with its terms; rooms and beds hang from it.",
    [C('IncidentFacilityId', 'ref', True), C('VendorId', 'ref', True, 'The shipped InventoryVendors row (Contact extension).'),
     C('ContactId', 'user', True), C('Name', 's200'), C('Address', 's500', True), C('DistanceMinutes', 'int', True),
     C('CheckInHours', 's100', True), C('Amenities', 's1000', True), C('CancellationTerms', 's1000', True),
     C('DirectBillMethod', 's200', True), C('BillingContact', 's200', True), C('CutoffOn', 'dt', True),
     C('Status', 'int', enum='IncidentFacilityStatuses'), C('IsCamp', 'bool'), C('Notes', 'text', True)])

add('IncidentLodgingBlocks', 'IncidentLodgingBlock', B, 293, 'LodgingEntities',
    "A room block by date and room type (plan §9.7) with rate, tax, cutoff and attrition terms.",
    [C('IncidentLodgingPropertyId', 'ref'), C('RoomType', 's100', True), C('FirstNight', 'date'), C('LastNight', 'date'),
     C('RoomsBlocked', 'int'), C('RoomsConfirmed', 'int'), C('NightlyRate', 'money', True), C('TaxPercent', 'pct', True), C('Currency', 's3'),
     C('CutoffOn', 'dt', True), C('AttritionTerms', 's1000', True), C('Status', 'int', enum='IncidentLodgingBlockStatuses'),
     C('IncidentVendorEngagementId', 'ref', True), C('VendorAgreementId', 'ref', True)],
    idx=[('Property', ['DepartmentId', 'IncidentLodgingPropertyId'])])

add('IncidentLodgingUnits', 'IncidentLodgingUnit', B, 293, 'LodgingEntities',
    "A room, tent, bunkhouse or cot group (plan §9.7). The unit label locates where a person sleeps and is ADP-protected.",
    [C('IncidentLodgingPropertyId', 'ref'), C('IncidentLodgingBlockId', 'ref', True), C('UnitType', 'int', enum='IncidentLodgingUnitTypes'),
     C('BedCount', 'int'), C('IsAccessible', 'bool'), C('IsOutOfService', 'bool'), C('UnitLabel', 'text', True, prot=PII_P), C('Notes', 's500', True)],
    idx=[('Property', ['DepartmentId', 'IncidentLodgingPropertyId'])])

add('IncidentLodgingBeds', 'IncidentLodgingBed', B, 293, 'LodgingEntities',
    "An assignable sleeping position (plan §9.7, §11.5).",
    [C('IncidentLodgingUnitId', 'ref'), C('IncidentLodgingPropertyId', 'ref'), C('BedLabel', 's32', True), C('IsAccessible', 'bool'), C('IsOutOfService', 'bool')],
    idx=[('Unit', ['DepartmentId', 'IncidentLodgingUnitId']), ('Property', ['DepartmentId', 'IncidentLodgingPropertyId'])])

add('IncidentLodgingNightInventory', 'IncidentLodgingNightInventory', B, 293, 'LodgingEntities',
    "One bed-night held by an assignment (plan §11.5). The filtered unique index on (bed, night) is the no-overlap invariant in both dialects; written in the assignment's transaction.",
    [C('IncidentLodgingBedId', 'ref'), C('Night', 'date'), C('IncidentLodgingAssignmentId', 'ref')],
    mutable=False, soft=False, ver=False,
    idx=[('Assignment', ['DepartmentId', 'IncidentLodgingAssignmentId']), ('Night', ['DepartmentId', 'IncidentActivationId', 'Night'])],
    uidx=[('BedNight', ['IncidentLodgingBedId', 'Night'], None)])

add('IncidentLodgingAssignments', 'IncidentLodgingAssignment', B, 293, 'LodgingEntities',
    "A bed/date-range assignment (plan §11.4). Flags are matching constraints visible only with ViewLodgingAssignmentDetail; the reasons live in IncidentParticipantSensitive.",
    [C('IncidentLodgingBedId', 'ref'), C('IncidentLodgingPropertyId', 'ref'), C('SubjectType', 'int', enum='IncidentLodgingSubjectTypes'),
     C('IncidentParticipantId', 'ref', True), C('IncidentResourceId', 'ref', True), C('ReservationLabel', 's128', True),
     C('FirstNight', 'date'), C('LastNight', 'date'), C('Status', 'int', enum='IncidentLodgingAssignmentStatuses'),
     C('IsAccessible', 'bool'), C('RequiresSingleOccupancy', 'bool'), C('DayNightPattern', 'int', enum='IncidentShiftPatterns'),
     C('IsRestPeriod', 'bool', doc='R&R / 24-hour down lodging generated from a rest window (plan §9.7).'),
     C('IncidentVendorEngagementId', 'ref', True), C('IncidentBudgetLineId', 'ref', True), C('RateSnapshotId', 'ref', True), C('Currency', 's3'),
     C('ConfirmationNumber', 'text', True, prot=PII_P), C('Notes', 'text', True, prot=SENS_O)],
    idx=[('Nights', ['DepartmentId', 'IncidentActivationId', 'FirstNight']), ('Bed', ['IncidentLodgingBedId', 'FirstNight', 'LastNight']),
         ('Participant', ['IncidentParticipantId', 'FirstNight'])])

add('IncidentRoomingListVersions', 'IncidentRoomingListVersion', B, 293, 'LodgingEntities',
    "An immutable rooming list sent to a property (plan §9.7). Holds names and rooms, so its content is protected.",
    [C('IncidentLodgingPropertyId', 'ref'), C('VersionNumber', 'int'), C('Status', 'int', enum='IncidentRoomingListStatuses'),
     C('SentOn', 'dt', True), C('Checksum', 's64', True), C('LineCount', 'int'), C('ContentJson', 'text', True, prot=PII_P)],
    mutable=False, soft=False, ver=False, idx=[('Property', ['DepartmentId', 'IncidentLodgingPropertyId', 'VersionNumber'])])

add('IncidentMealPlans', 'IncidentMealPlan', B, 293, 'FeedingEntities',
    "A meal window at a site (plan §9.8), including offsite feeding sites and voucher arrangements.",
    [C('IncidentOperationalPeriodId', 'ref', True), C('IncidentFacilityId', 'ref', True), C('SiteName', 's200'),
     C('MealType', 'int', enum='IncidentMealTypes'), C('ServiceDate', 'date'), C('WindowStart', 'dt'), C('WindowEnd', 'dt'), C('CutoffOn', 'dt', True),
     C('DeliveryMode', 'int', enum='IncidentMealDeliveryModes'), C('IncidentVendorEngagementId', 'ref', True), C('IsOffsite', 'bool'),
     C('VoucherTerms', 's500', True), C('AuthorizedHeadcount', 'int', True), C('Status', 'int', enum='IncidentMealPlanStatuses'), C('Notes', 'text', True)],
    idx=[('Window', ['DepartmentId', 'IncidentActivationId', 'WindowStart'])])

add('IncidentMealDemands', 'IncidentMealDemand', B, 293, 'FeedingEntities',
    "A versioned meal-count forecast snapshot (plan §9.8): source population, inbound and departures, bands, contingency, overrides. Dietary aggregates apply the minimum-cell rule.",
    [C('IncidentMealPlanId', 'ref'), C('Version', 'int'), C('MetricVersion', 'int'), C('Population', 'int'), C('Inbound', 'int'), C('Departures', 'int'),
     C('Remote', 'int'), C('Guests', 'int'), C('Exclusions', 'int'), C('ContingencyPercent', 'pct'), C('UnregisteredBand', 'int'),
     C('PredictedTimeoutBand', 'int'), C('ForecastCount', 'int'), C('ManualOverride', 'int', True), C('OverrideReason', 's500', True),
     C('DietaryJson', 'text', True), C('InputsJson', 'text', True)],
    mutable=False, soft=False, ver=False, idx=[('Plan', ['DepartmentId', 'IncidentMealPlanId', 'Version'])])

add('IncidentMealOrders', 'IncidentMealOrder', B, 293, 'FeedingEntities',
    "A meal order against a vendor engagement. Ordered quantities freeze; later roster changes create a variance, never a rewrite (plan §9.8).",
    [C('IncidentMealPlanId', 'ref'), C('IncidentVendorEngagementId', 'ref', True), C('OrderedCount', 'int'), C('FrozenForecastVersion', 'int', True),
     C('UnitPrice', 'qty', True), C('Currency', 's3'), C('Status', 'int', enum='IncidentOrderStatuses'), C('Notes', 's1000', True)],
    idx=[('Plan', ['DepartmentId', 'IncidentMealPlanId'])])

add('IncidentMealActuals', 'IncidentMealActual', B, 293, 'FeedingEntities',
    "Delivered, served, returned, waste and shortage counts for a meal window (plan §9.8).",
    [C('IncidentMealPlanId', 'ref'), C('Delivered', 'int'), C('Served', 'int'), C('Returned', 'int'), C('Waste', 'int'), C('Shortage', 'int'),
     C('ReceivingTemperature', 's32', True), C('InspectionNotes', 's1000', True), C('Complaint', 's1000', True)],
    soft=False, idx=[('Plan', ['DepartmentId', 'IncidentMealPlanId'])])

add('IncidentWaterPlans', 'IncidentWaterPlan', B, 293, 'FeedingEntities',
    "Potable water planning at a site (plan §9.8): storage, reorder and days-of-supply thresholds, coefficient profile.",
    [C('IncidentFacilityId', 'ref', True), C('SiteName', 's200'), C('StorageCapacity', 'qty'), C('Unit', 's16'),
     C('ReorderThreshold', 'qty', True), C('DaysOfSupplyThreshold', 'qty', True), C('Source', 's200', True),
     C('CoefficientProfile', 's64', True), C('Status', 'int', enum='IncidentMealPlanStatuses')])

add('IncidentWaterStock', 'IncidentWaterStock', B, 293, 'FeedingEntities',
    "An on-hand water measurement with original and canonical units (plan §9.8, R25).",
    [C('IncidentWaterPlanId', 'ref'), C('Kind', 'int', enum='IncidentWaterKinds'), C('Quantity', 'qty'), C('UnitOfMeasure', 's16'),
     C('CanonicalQuantity', 'qty', True), C('CanonicalUnit', 's16', True), C('MeasuredOn', 'dt')],
    mutable=False, soft=False, ver=False, idx=[('Plan', ['DepartmentId', 'IncidentWaterPlanId', 'MeasuredOn'])])

add('IncidentWaterDeliveries', 'IncidentWaterDelivery', B, 293, 'FeedingEntities',
    "A scheduled or received water delivery (plan §9.8).",
    [C('IncidentWaterPlanId', 'ref'), C('IncidentVendorEngagementId', 'ref', True), C('Quantity', 'qty'), C('UnitOfMeasure', 's16'),
     C('CanonicalQuantity', 'qty', True), C('CanonicalUnit', 's16', True), C('ScheduledOn', 'dt', True), C('DeliveredOn', 'dt', True),
     C('TestResult', 's200', True), C('Notes', 's1000', True)],
    idx=[('Plan', ['DepartmentId', 'IncidentWaterPlanId'])])

add('IncidentDemandCoefficients', 'IncidentDemandCoefficient', B, 293, 'FeedingEntities',
    "A configurable per-person demand coefficient (plan §9.8): department default when IncidentActivationId is null. Never one hard-coded universal number.",
    [C('IncidentActivationId', 'ref', True), C('Kind', 'int', enum='IncidentCoefficientKinds'), C('PopulationClass', 's32', True),
     C('Value', 'qty'), C('Unit', 's16', True), C('Climate', 's32', True), C('Notes', 's500', True)],
    scope='dept')

add('IncidentServiceTemplates', 'IncidentServiceTemplate', B, 293, 'ServiceEntities',
    "A support-service template (plan §9.10): radio, print/copy, IT, sanitation and custom services. Template-specific fields are UDF definitions, never columns.",
    [C('Code', 's32'), C('Name', 's200'), C('Category', 'int', enum='IncidentServiceCategories'), C('DefaultSlaMinutes', 'int', True),
     C('IsSelfService', 'bool'), C('IsActive', 'bool', default=True), C('Description', 's1000', True)],
    scope='dept', uidx=[('Code', ['DepartmentId', 'Code'], 'live')])

add('IncidentServiceRequests', 'IncidentServiceRequest', B, 293, 'ServiceEntities',
    "A support-service ticket (plan §9.10) with SLA, assignment, vendor, cost and confirmation.",
    [C('IncidentOperationalPeriodId', 'ref', True), C('IncidentServiceTemplateId', 'ref'), C('TicketNumber', 's32'),
     C('RequesterUserId', 'user', True), C('RequesterParticipantId', 'ref', True), C('IncidentFacilityId', 'ref', True), C('Location', 's200', True),
     C('Priority', 'int', enum='IncidentRequestPriorities'), C('NeededBy', 'dt', True), C('Title', 's200'), C('Quantity', 'qty', True),
     C('Unit', 's16', True), C('AssetReference', 's200', True), C('AssignedPosition', 's32', True), C('AssignedUserId', 'user', True),
     C('IncidentVendorEngagementId', 'ref', True), C('Status', 'int', enum='IncidentServiceRequestStatuses'), C('SlaDueOn', 'dt', True),
     C('SlaBreached', 'bool'), C('CompletedOn', 'dt', True), C('CustomerConfirmedOn', 'dt', True), C('CostAmount', 'money', True),
     C('Currency', 's3', True), C('WorkOrderId', 'ref', True),
     C('Description', 'text', True, prot=SENS_O), C('CompletionNotes', 'text', True, prot=SENS_O)],
    idx=[('Status', ['DepartmentId', 'IncidentActivationId', 'Status']), ('Template', ['DepartmentId', 'IncidentServiceTemplateId'])],
    uidx=[('Ticket', ['DepartmentId', 'IncidentActivationId', 'TicketNumber'], 'live')])

add('IncidentServiceActivities', 'IncidentServiceActivity', B, 293, 'ServiceEntities',
    "A note, labor, parts or cost entry on a service ticket.",
    [C('IncidentServiceRequestId', 'ref'), C('Kind', 'int', enum='IncidentServiceActivityKinds'), C('LaborMinutes', 'int', True), C('Amount', 'money', True),
     C('Currency', 's3', True), C('InventoryTransactionId', 'ref', True), C('Description', 'text', True, prot=SENS_O)],
    mutable=False, soft=False, ver=False, idx=[('Ticket', ['DepartmentId', 'IncidentServiceRequestId'])])

add('IncidentServiceAssets', 'IncidentServiceAsset', B, 293, 'ServiceEntities',
    "A copier, generator, shower unit, network device or similar asset a service queue works (plan §9.10).",
    [C('IncidentServiceTemplateId', 'ref', True), C('Name', 's200'), C('Kind', 'int', enum='IncidentServiceAssetKinds'), C('SerialNumber', 's100', True),
     C('InventoryAssetId', 'ref', True), C('IncidentFacilityId', 'ref', True), C('Status', 'int', enum='IncidentServiceReadiness')])

add('IncidentTransportRequests', 'IncidentTransportRequest', B, 293, 'GroundSupportEntities',
    "A shuttle, crew transport, cargo or travel request (plan §9.11).",
    [C('Kind', 'int', enum='IncidentTransportKinds'), C('RequestedByUserId', 'user', True), C('PickupLocation', 's200', True), C('DropoffLocation', 's200', True),
     C('NeededOn', 'dt', True), C('Passengers', 'int'), C('Cargo', 's500', True), C('Status', 'int', enum='IncidentTransportStatuses'),
     C('AssignedVehicle', 's200', True), C('AssignedDriver', 's200', True), C('Notes', 's1000', True)],
    idx=[('Status', ['DepartmentId', 'IncidentActivationId', 'Status'])])

add('IncidentTransportMovements', 'IncidentTransportMovement', B, 293, 'GroundSupportEntities',
    "A run of the incident's own support fleet or hired transport with its manifest (plan §9.11). Passenger pickup addresses are protected.",
    [C('IncidentTransportRequestId', 'ref', True), C('Kind', 'int', enum='IncidentTransportKinds'), C('Route', 's500', True), C('VehicleLabel', 's200', True),
     C('DriverName', 's200', True), C('DepartedOn', 'dt', True), C('ArrivedOn', 'dt', True), C('EtaOn', 'dt', True),
     C('Status', 'int', enum='IncidentTransportStatuses'), C('PassengerCount', 'int'), C('PassengerNames', 'text', True), C('Mileage', 'qty', True),
     C('ExceptionKind', 'int', enum='IncidentTransportExceptions'), C('Notes', 's1000', True), C('PickupAddresses', 'text', True, prot=PII_P)],
    idx=[('Day', ['DepartmentId', 'IncidentActivationId', 'DepartedOn'])])

add('IncidentTravelItineraries', 'IncidentTravelItinerary', B, 293, 'GroundSupportEntities',
    "Mobilization or demob travel for a participant or resource (plan §9.11). Locators, carrier detail and pickup addresses are protected.",
    [C('SubjectType', 'int', enum='IncidentSubjectTypes'), C('SubjectId', 'ref'), C('Direction', 'int', enum='IncidentTravelDirections'),
     C('TravelMode', 's64', True), C('DepartureOn', 'date', True), C('Destination', 's200', True), C('Status', 'int', enum='IncidentTransportStatuses'),
     C('BookingLocator', 'text', True, prot=PII_P), C('CarrierDetail', 'text', True, prot=PII_P), C('PickupAddress', 'text', True, prot=PII_P),
     C('Notes', 'text', True, prot=PII_P)],
    idx=[('Subject', ['DepartmentId', 'SubjectId'])])

add('IncidentFuelTransactions', 'IncidentFuelTransaction', B, 293, 'GroundSupportEntities',
    "A fuel delivery, issue, reconciliation or spill record (plans §9.11 and §9.18). Fuel cards are stored as last four only.",
    [C('FuelType', 'int', enum='IncidentFuelTypes'), C('Kind', 'int', enum='IncidentFuelTransactionKinds'), C('SiteName', 's200', True),
     C('IncidentFacilityId', 'ref', True), C('IncidentResourceId', 'ref', True), C('Quantity', 'qty'), C('UnitOfMeasure', 's16'),
     C('CanonicalQuantity', 'qty', True), C('CanonicalUnit', 's16', True), C('CardLastFour', 's4', True), C('IncidentVendorEngagementId', 'ref', True),
     C('OccurredOn', 'dt'), C('ExceptionKind', 'int', enum='IncidentTransportExceptions'), C('Notes', 's1000', True)],
    mutable=False, soft=False, ver=False, idx=[('Day', ['DepartmentId', 'IncidentActivationId', 'OccurredOn'])])

add('IncidentCommunicationsPlans', 'IncidentCommunicationsPlan', B, 293, 'GroundSupportEntities',
    "The communications assignment ledger header for a period (plan §9.15). The plan itself is the ICS 205 RMS record.",
    [C('IncidentOperationalPeriodId', 'ref', True), C('RecordId', 'ref', True), C('Status', 'int', enum='IncidentMealPlanStatuses'), C('Notes', 's1000', True)])

add('IncidentCommunicationsResourceAssignments', 'IncidentCommunicationsResourceAssignment', B, 293, 'GroundSupportEntities',
    "Radio, kit, battery and phone issue/return to a holder (plan §9.15). Restricted channel identifiers are protected; encryption keys are never stored.",
    [C('IncidentCommunicationsPlanId', 'ref', True), C('ItemKind', 'int', enum='IncidentCommsItemKinds'), C('ItemLabel', 's200'), C('SerialNumber', 's100', True),
     C('InventoryAssetId', 'ref', True), C('HolderType', 'int', enum='IncidentSubjectTypes'), C('HolderId', 'ref', True), C('HolderName', 's200', True),
     C('IssuedOn', 'dt', True), C('ExpectedReturnOn', 'dt', True), C('ReturnedOn', 'dt', True), C('ProgrammingProfile', 's200', True),
     C('Condition', 's200', True), C('Status', 'int', enum='IncidentCustodyStatuses'), C('Notes', 's1000', True),
     C('RestrictedChannelIds', 'text', True, prot=SENS_O)],
    idx=[('Status', ['DepartmentId', 'IncidentActivationId', 'Status']), ('Holder', ['DepartmentId', 'HolderId'])])

# ---------------------------------------------------------------- LI-B vendors, support plan, engagements, self-service (M0294)
add('DepartmentEmergencySupportPlans', 'DepartmentEmergencySupportPlan', B, 294, 'VendorEntities',
    "A department's pre-incident local support plan (plan §9.12.1).",
    [C('Name', 's200'), C('Description', 'text', True), C('ReviewedOn', 'dt', True), C('NextReviewOn', 'date', True), C('Status', 'int', enum='IncidentCatalogStatuses')],
    scope='dept')

add('SupportPlanAreas', 'SupportPlanArea', B, 294, 'VendorEntities',
    "A geography or function area of a support plan.",
    [C('DepartmentEmergencySupportPlanId', 'ref'), C('Name', 's200'), C('Geography', 's1000', True), C('IncidentTypes', 's500', True), C('ActivationThreshold', 's500', True)],
    scope='dept', idx=[('Plan', ['DepartmentId', 'DepartmentEmergencySupportPlanId'])])

add('SupportPlanVendorPreferences', 'SupportPlanVendorPreference', B, 294, 'VendorEntities',
    "Primary, alternate and contingency vendors per category in an area (plan §9.12.1).",
    [C('DepartmentEmergencySupportPlanId', 'ref'), C('SupportPlanAreaId', 'ref', True), C('VendorId', 'ref'),
     C('Category', 'int', enum='IncidentSupportCategories'), C('Rank', 'int', enum='IncidentVendorRanks'), C('Notes', 's500', True)],
    scope='dept', idx=[('Plan', ['DepartmentId', 'DepartmentEmergencySupportPlanId', 'Category'])])

add('IncidentSupportPlanSnapshots', 'IncidentSupportPlanSnapshot', B, 294, 'VendorEntities',
    "The support plan as it stood when an activation took it (plan §1 commitment 8).",
    [C('DepartmentEmergencySupportPlanId', 'ref'), C('ContentJson', 'text')],
    mutable=False, soft=False, ver=False)

add('VendorLocations', 'VendorLocation', B, 294, 'VendorEntities',
    "A branch or service location of a vendor (plan §9.12.1). Extends the shipped InventoryVendors row by VendorId.",
    [C('VendorId', 'ref'), C('Name', 's200'), C('Address', 's500', True), C('ServiceRadiusKm', 'qty', True), C('DispatchPhone', 's64', True),
     C('AfterHoursPhone', 's64', True), C('OrderingChannel', 's200', True), C('Hours', 's200', True), C('Is24x7', 'bool'), C('Languages', 's200', True), C('Notes', 's1000', True)],
    scope='dept', idx=[('Vendor', ['DepartmentId', 'VendorId'])])

add('VendorCapabilities', 'VendorCapability', B, 294, 'VendorEntities',
    "A typed vendor capability with units and normal/maximum capacity (plan §9.12.1). Capacity commitments lock this row.",
    [C('VendorId', 'ref'), C('VendorLocationId', 'ref', True), C('Category', 'int', enum='IncidentSupportCategories'), C('Name', 's200'),
     C('Unit', 's32'), C('NormalCapacity', 'qty'), C('MaxCapacity', 'qty'), C('LeadTimeHours', 'int', True), C('MinimumOrder', 's200', True),
     C('CutoffTime', 's64', True), C('Status', 'int', enum='IncidentCatalogStatuses'), C('LastVerifiedOn', 'dt', True),
     C('VerifiedByUserId', 'user', True), C('Notes', 's1000', True)],
    scope='dept', idx=[('Vendor', ['DepartmentId', 'VendorId']), ('Category', ['DepartmentId', 'Category'])])

add('VendorAvailabilityWindows', 'VendorAvailabilityWindow', B, 294, 'VendorEntities',
    "A dated availability or blackout window of a capability.",
    [C('VendorCapabilityId', 'ref'), C('StartsOn', 'dt'), C('EndsOn', 'dt'), C('Kind', 'int', enum='IncidentAvailabilityKinds'),
     C('Capacity', 'qty', True), C('Notes', 's500', True)],
    scope='dept', idx=[('Capability', ['DepartmentId', 'VendorCapabilityId', 'StartsOn'])])

add('VendorComplianceDocuments', 'VendorComplianceDocument', B, 294, 'VendorEntities',
    "Insurance, licence, safety and tax-form metadata with expiry (plan §9.12.1).",
    [C('VendorId', 'ref'), C('Kind', 'int', enum='IncidentComplianceDocumentKinds'), C('DocumentNumber', 's100', True), C('IssuedOn', 'date', True),
     C('ExpiresOn', 'date', True), C('Status', 'int', enum='IncidentCatalogStatuses'), C('Notes', 's1000', True)],
    scope='dept', idx=[('Vendor', ['DepartmentId', 'VendorId']), ('Expiry', ['DepartmentId', 'ExpiresOn'])])

add('VendorProtectedDetails', 'VendorProtectedDetail', B, 294, 'VendorEntities',
    "A vendor's tax identifier and remittance detail (plan §15.3a): a sole proprietor's TIN can be an SSN.",
    [C('VendorId', 'ref'), C('TaxIdentifier', 'text', True, prot=PII_C), C('RemittanceDetails', 'text', True, prot=PII_C)],
    scope='dept', soft=False, uidx=[('Vendor', ['DepartmentId', 'VendorId'], None)])

add('IncidentVendorEngagements', 'IncidentVendorEngagement', B, 294, 'VendorEntities',
    "An incident's time-bounded engagement of a vendor (plan §9.12.3), distinct from the vendor's standing eligibility.",
    [C('VendorId', 'ref'), C('VendorCapabilityId', 'ref', True), C('VendorAgreementId', 'ref', True), C('ServiceDescription', 's500'),
     C('IncidentFacilityId', 'ref', True), C('Status', 'int', enum='IncidentEngagementStatuses'), C('PurchaserUserId', 'user', True),
     C('OwnerUserId', 'user', True), C('WindowStart', 'dt', True), C('WindowEnd', 'dt', True), C('Notes', 'text', True)],
    idx=[('Status', ['DepartmentId', 'IncidentActivationId', 'Status']), ('Vendor', ['DepartmentId', 'VendorId'])])

add('IncidentVendorCapacityCommitments', 'IncidentVendorCapacityCommitment', B, 294, 'VendorEntities',
    "A time-bucketed hold or commitment of vendor capacity (plan §11.5), checked across concurrent incidents in a locked transaction.",
    [C('IncidentVendorEngagementId', 'ref'), C('VendorCapabilityId', 'ref'), C('BucketStart', 'dt'), C('BucketEnd', 'dt'),
     C('Kind', 'int', enum='IncidentCommitmentKinds'), C('Quantity', 'qty'), C('Unit', 's32'), C('Status', 'int', enum='IncidentHoldStatuses'),
     C('ExpiresOn', 'dt', True), C('IsOverride', 'bool'), C('OverrideReason', 's500', True), C('ApprovedByUserId', 'user', True)],
    idx=[('Capability', ['VendorCapabilityId', 'BucketStart', 'BucketEnd']), ('Engagement', ['DepartmentId', 'IncidentVendorEngagementId'])])

add('IncidentVendorQuotes', 'IncidentVendorQuote', B, 294, 'VendorEntities',
    "A quote requested from or returned by an engaged vendor.",
    [C('IncidentVendorEngagementId', 'ref'), C('RequestedOn', 'dt'), C('ReceivedOn', 'dt', True), C('Amount', 'money', True), C('Currency', 's3'),
     C('Terms', 's2000', True), C('Status', 'int', enum='IncidentQuoteStatuses')],
    idx=[('Engagement', ['DepartmentId', 'IncidentVendorEngagementId'])])

add('IncidentSelfServiceLinks', 'IncidentSelfServiceLink', B, 294, 'SelfServiceEntities',
    "A scoped, expiring, revocable link to a participant's or vendor engagement's own page (plan §9.28). The token is stored hashed; the delivery target is protected.",
    [C('SubjectType', 'int', enum='IncidentSelfServiceSubjects'), C('SubjectId', 'ref'), C('TokenHash', 's128'), C('Purposes', 'int'),
     C('ExpiresOn', 'dt'), C('RevokedOn', 'dt', True), C('RevokedByUserId', 'user', True), C('LastUsedOn', 'dt', True), C('UseCount', 'int'),
     C('OneTimeCodeHash', 's128', True), C('OneTimeCodeExpiresOn', 'dt', True), C('VerifiedOn', 'dt', True),
     C('DeliveryTarget', 'text', True, prot=PII_C)],
    soft=False, idx=[('Subject', ['DepartmentId', 'SubjectId'])], uidx=[('Token', ['TokenHash'], None)])

# ---------------------------------------------------------------- LI-C procurement, budget, time, cost (M0295)
C_ = 'C'
add('VendorAgreements', 'VendorAgreement', C_, 295, 'ProcurementEntities',
    "A standing or incident agreement's arithmetic (plan §9.12.2): rates, ceilings, ordering and payment rules. The signed document is an RMS record; rates reference the shared rate engine.",
    [C('VendorId', 'ref'), C('AgreementType', 'int', enum='IncidentAgreementTypes'), C('AgreementNumber', 's100', True), C('RecordId', 'ref', True),
     C('EffectiveOn', 'date', True), C('ExpiresOn', 'date', True), C('CeilingAmount', 'money', True), C('Currency', 's3'),
     C('RateScheduleId', 'ref', True), C('RatesJson', 'text', True), C('TaxesAndFees', 's1000', True), C('Minimums', 's1000', True),
     C('CancellationTerms', 's1000', True), C('OrderingInstructions', 'text', True), C('InspectionRules', 's1000', True),
     C('PaymentTerms', 's1000', True), C('InvoiceRoute', 's500', True), C('RequiredDocuments', 's1000', True), C('Status', 'int', enum='IncidentCatalogStatuses')],
    scope='dept', idx=[('Vendor', ['DepartmentId', 'VendorId']), ('Expiry', ['DepartmentId', 'ExpiresOn'])])

add('VendorPaymentPlans', 'VendorPaymentPlan', C_, 295, 'ProcurementEntities',
    "How an agreement or engagement is expected to be paid (plan §9.12.2). Remittance references are protected.",
    [C('VendorAgreementId', 'ref', True), C('VendorId', 'ref'), C('PayingAgency', 's200', True), C('FundingSource', 's200', True),
     C('Method', 'int', enum='IncidentPurchaseMethods'), C('Terms', 's1000', True), C('PaymentOffice', 's200', True),
     C('RemittanceReference', 'text', True, prot=PII_C)],
    scope='dept', idx=[('Vendor', ['DepartmentId', 'VendorId'])])

add('PurchaseRequisitions', 'PurchaseRequisition', C_, 295, 'ProcurementEntities',
    "A requisition (plan §9.12.3): requisition → approval → sourcing → commitment, with separation of duties and split-purchase warnings.",
    [C('RequisitionNumber', 's32'), C('IncidentResourceRequestId', 'ref', True), C('VendorId', 'ref', True), C('IncidentVendorEngagementId', 'ref', True),
     C('IncidentBudgetLineId', 'ref', True), C('Method', 'int', enum='IncidentPurchaseMethods'), C('Description', 's1000'),
     C('EstimatedAmount', 'money'), C('Currency', 's3'), C('Justification', 'text', True), C('Status', 'int', enum='IncidentRequisitionStatuses'),
     C('RequestedByUserId', 'user', True), C('SubmittedOn', 'dt', True), C('ApprovedOn', 'dt', True), C('ApprovedByUserId', 'user', True),
     C('ConflictOfInterestAttested', 'bool'), C('IsSplitPurchaseWarning', 'bool'), C('OverrideReason', 's500', True)],
    idx=[('Status', ['DepartmentId', 'IncidentActivationId', 'Status'])], uidx=[('Number', ['DepartmentId', 'RequisitionNumber'], 'live')])

add('PurchaseApprovals', 'PurchaseApproval', C_, 295, 'ProcurementEntities',
    "One approval decision on a requisition, append-only.",
    [C('PurchaseRequisitionId', 'ref'), C('Step', 'int'), C('Decision', 'int', enum='IncidentApprovalDecisions'), C('ThresholdAmount', 'money', True), C('Comment', 's1000', True)],
    mutable=False, soft=False, ver=False, idx=[('Requisition', ['DepartmentId', 'PurchaseRequisitionId'])])

add('IncidentCommitments', 'IncidentCommitment', C_, 295, 'ProcurementEntities',
    "An authorized financial obligation (plan §9.12.3): PO, agreement call, purchase card or emergency purchase. Links the shipped InventoryPurchaseOrders row when sourced through inventory purchasing.",
    [C('CommitmentNumber', 's32'), C('PurchaseRequisitionId', 'ref', True), C('VendorId', 'ref', True), C('IncidentVendorEngagementId', 'ref', True),
     C('VendorAgreementId', 'ref', True), C('IncidentBudgetLineId', 'ref', True), C('IncidentFundingCodeId', 'ref', True),
     C('Method', 'int', enum='IncidentPurchaseMethods'), C('Amount', 'money'), C('Currency', 's3'), C('Status', 'int', enum='IncidentCommitmentStatuses'),
     C('CommittedOn', 'dt'), C('CommittedByUserId', 'user', True), C('InventoryPurchaseOrderId', 'ref', True), C('Description', 's1000', True)],
    idx=[('Status', ['DepartmentId', 'IncidentActivationId', 'Status'])], uidx=[('Number', ['DepartmentId', 'CommitmentNumber'], 'live')])

add('PurchaseReceipts', 'PurchaseReceipt', C_, 295, 'ProcurementEntities',
    "Receipt or service acceptance against a commitment (plan §9.12.3).",
    [C('IncidentCommitmentId', 'ref'), C('IncidentReceiptId', 'ref', True), C('AcceptedOn', 'dt'), C('AcceptedByUserId', 'user', True),
     C('Quantity', 'qty', True), C('Amount', 'money'), C('Currency', 's3'), C('Status', 'int', enum='IncidentReceiptStatuses'), C('Notes', 's1000', True)],
    idx=[('Commitment', ['DepartmentId', 'IncidentCommitmentId'])])

add('VendorInvoiceReferences', 'VendorInvoiceReference', C_, 295, 'ProcurementEntities',
    "A vendor invoice and its three-way match state (plan §9.12.3).",
    [C('VendorId', 'ref'), C('IncidentCommitmentId', 'ref', True), C('InvoiceNumber', 's100'), C('InvoiceDate', 'date', True), C('Amount', 'money'),
     C('Currency', 's3'), C('ReceivedOn', 'dt'), C('Status', 'int', enum='IncidentInvoiceStatuses'), C('MatchVariance', 'money', True),
     C('ExceptionKind', 'int', enum='IncidentInvoiceExceptionKinds'), C('AcceptedByUserId', 'user', True), C('AcceptedOn', 'dt', True),
     C('EvidenceArtifactId', 'ref', True), C('Notes', 's1000', True)],
    idx=[('Vendor', ['DepartmentId', 'VendorId', 'InvoiceNumber']), ('Status', ['DepartmentId', 'IncidentActivationId', 'Status'])])

add('PaymentPackages', 'PaymentPackage', C_, 295, 'ProcurementEntities',
    "The host's payment package to a vendor or responder (plan §9.13, §9.24). Payment is never inferred from generation.",
    [C('PackageNumber', 's32'), C('VendorId', 'ref', True), C('Status', 'int', enum='IncidentPaymentPackageStatuses'), C('TotalAmount', 'money'),
     C('Currency', 's3'), C('LineCount', 'int'), C('SubmittedOn', 'dt', True), C('ExternalReference', 's200', True), C('PaidReference', 's200', True),
     C('PaidOn', 'dt', True), C('EvidenceArtifactId', 'ref', True)],
    uidx=[('Number', ['DepartmentId', 'PackageNumber'], 'live')])

add('PaymentPackageLines', 'PaymentPackageLine', C_, 295, 'ProcurementEntities',
    "An invoice included in a payment package.",
    [C('PaymentPackageId', 'ref'), C('VendorInvoiceReferenceId', 'ref'), C('Amount', 'money'), C('Currency', 's3')],
    mutable=False, soft=False, ver=False, idx=[('Package', ['DepartmentId', 'PaymentPackageId'])])

add('IncidentBudgets', 'IncidentBudget', C_, 295, 'BudgetEntities',
    "An incident budget (plan §9.13); the signed delegation is an RMS record.",
    [C('Name', 's200'), C('OriginalAmount', 'money'), C('RevisedAmount', 'money'), C('Currency', 's3'), C('Status', 'int', enum='IncidentBudgetStatuses'), C('RecordId', 'ref', True)])

add('IncidentBudgetLines', 'IncidentBudgetLine', C_, 295, 'BudgetEntities',
    "A budget line by section, function, facility, vendor, cost group or period, with thresholds (plan §9.13).",
    [C('IncidentBudgetId', 'ref'), C('Name', 's200'), C('Kind', 'int', enum='IncidentBudgetLineKinds'), C('ScopeKey', 's64', True),
     C('IncidentFundingCodeId', 'ref', True), C('OriginalAmount', 'money'), C('RevisedAmount', 'money'), C('Currency', 's3'),
     C('ThresholdPercent', 'pct', True), C('FloorAmount', 'money', True)],
    idx=[('Budget', ['DepartmentId', 'IncidentBudgetId'])])

add('IncidentFundingAuthorizations', 'IncidentFundingAuthorization', C_, 295, 'BudgetEntities',
    "An initial authorization, revision or transfer of budget authority, append-only (plan §9.13).",
    [C('IncidentBudgetLineId', 'ref', True), C('FromBudgetLineId', 'ref', True), C('Kind', 'int', enum='IncidentFundingAuthorizationKinds'),
     C('Amount', 'money'), C('Currency', 's3'), C('AuthorizedOn', 'dt'), C('RecordId', 'ref', True), C('Reason', 's1000', True)],
    mutable=False, soft=False, ver=False, idx=[('Line', ['DepartmentId', 'IncidentBudgetLineId'])])

add('IncidentSpendingDelegations', 'IncidentSpendingDelegation', C_, 295, 'BudgetEntities',
    "Who may request, approve, commit, receive, accept or close, up to what amount (plan §9.13, §15.1).",
    [C('PositionCode', 's32', True), C('UserId', 'user', True), C('Action', 'int', enum='IncidentSpendingActions'), C('ThresholdAmount', 'money'),
     C('Currency', 's3'), C('IncidentFundingCodeId', 'ref', True), C('EffectiveOn', 'dt'), C('ExpiresOn', 'dt', True), C('RecordId', 'ref', True)],
    idx=[('User', ['DepartmentId', 'IncidentActivationId', 'UserId'])])

add('IncidentFundingCodes', 'IncidentFundingCode', C_, 295, 'BudgetEntities',
    "A FireCode, FEMA DR/P-code, project or agency accounting code with effective dates (plan §9.13).",
    [C('Code', 's100'), C('Kind', 'int', enum='IncidentFundingCodeKinds'), C('Description', 's500', True), C('PayingAgency', 's200', True),
     C('EffectiveOn', 'date', True), C('ExpiresOn', 'date', True)])

add('IncidentCostGroups', 'IncidentCostGroup', C_, 295, 'BudgetEntities',
    "A cost group for slicing incident cost.",
    [C('Name', 's200'), C('Code', 's64', True), C('Description', 's500', True)])

add('IncidentTimeRecords', 'IncidentTimeRecord', C_, 295, 'TimeEntities',
    "A personnel or equipment time span (plan §11.4). Certified and approved rows are immutable; corrections are adjusting rows. (SourceType, SourceId) makes a DTR import a no-op the second time.",
    [C('IncidentOperationalPeriodId', 'ref', True), C('SubjectType', 'int', enum='IncidentTimeSubjectTypes'), C('IncidentParticipantId', 'ref', True),
     C('IncidentResourceId', 'ref', True), C('IncidentPositionAssignmentId', 'ref', True), C('IncidentRecoveryProjectId', 'ref', True),
     C('MemberActivationId', 'ref', True, 'For a complex: the member incident the time belongs to.'),
     C('EntryType', 'int', enum='IncidentTimeEntryTypes'), C('StartOn', 'dt'), C('EndOn', 'dt'), C('PaidBreakMinutes', 'int'), C('UnpaidBreakMinutes', 'int'),
     C('PaymentClass', 'int', enum='IncidentPaymentClasses'), C('RateSnapshotId', 'ref', True), C('Currency', 's3'),
     C('Status', 'int', enum='IncidentTimeStatuses'), C('IsSystemGenerated', 'bool'), C('CertifiedByUserId', 'user', True), C('CertifiedOn', 'dt', True),
     C('ApprovedByUserId', 'user', True), C('ApprovedOn', 'dt', True), C('AdjustsTimeRecordId', 'ref', True), C('SourceType', 's32', True),
     C('SourceId', 'ref', True), C('Remarks', 'text', True, prot=SENS_O)],
    idx=[('Period', ['DepartmentId', 'IncidentActivationId', 'IncidentOperationalPeriodId']), ('Participant', ['DepartmentId', 'IncidentParticipantId', 'StartOn']),
         ('Resource', ['DepartmentId', 'IncidentResourceId', 'StartOn'])],
    uidx=[('Source', ['DepartmentId', 'SourceType', 'SourceId'], 'live+SourceType+SourceId')])

add('IncidentTimeAdjustments', 'IncidentTimeAdjustment', C_, 295, 'TimeEntities',
    "The provenance link of a correction: the adjusted record, the adjusting record and the reason.",
    [C('IncidentTimeRecordId', 'ref'), C('AdjustingTimeRecordId', 'ref'), C('Reason', 's1000')],
    mutable=False, soft=False, ver=False, idx=[('Record', ['DepartmentId', 'IncidentTimeRecordId'])])

add('IncidentEquipmentUseRecords', 'IncidentEquipmentUseRecord', C_, 295, 'TimeEntities',
    "Hired equipment use for a shift (plan §9.13): the quantities an e-shift ticket posts; the OF-286 invoice renders from them by reference.",
    [C('IncidentOperationalPeriodId', 'ref', True), C('IncidentResourceId', 'ref'), C('IncidentVendorEngagementId', 'ref', True),
     C('VendorAgreementId', 'ref', True), C('ShiftDate', 'date'), C('StartOn', 'dt', True), C('EndOn', 'dt', True), C('Hours', 'qty', True),
     C('Mileage', 'qty', True), C('UseUnit', 's16', True), C('Quantity', 'qty', True), C('WorkPerformed', 's2000', True),
     C('FuelProvided', 's500', True), C('OperatorName', 's200', True), C('ShiftTicketRecordId', 'ref', True),
     C('Status', 'int', enum='IncidentTimeStatuses'), C('RateSnapshotId', 'ref', True), C('Currency', 's3'), C('IncidentRecoveryProjectId', 'ref', True),
     C('SourceType', 's32', True), C('SourceId', 'ref', True), C('CertifiedByUserId', 'user', True), C('CertifiedOn', 'dt', True),
     C('ApprovedByUserId', 'user', True), C('ApprovedOn', 'dt', True), C('AdjustsRecordId', 'ref', True)],
    idx=[('Resource', ['DepartmentId', 'IncidentResourceId', 'ShiftDate'])],
    uidx=[('Source', ['DepartmentId', 'SourceType', 'SourceId'], 'live+SourceType+SourceId')])

add('IncidentExpenses', 'IncidentExpense', C_, 295, 'TimeEntities',
    "An eligible expense with receipt evidence and reimbursable/billable flags (plan §9.13).",
    [C('IncidentOperationalPeriodId', 'ref', True), C('SubjectType', 'int', enum='IncidentSubjectTypes'), C('SubjectId', 'ref', True),
     C('Category', 'int', enum='IncidentExpenseCategories'), C('IncurredOn', 'date'), C('Amount', 'money'), C('Currency', 's3'),
     C('IsReimbursable', 'bool'), C('IsBillable', 'bool'), C('VendorId', 'ref', True), C('EvidenceArtifactId', 'ref', True),
     C('IncidentRecoveryProjectId', 'ref', True), C('Status', 'int', enum='IncidentExpenseStatuses'), C('ApprovedByUserId', 'user', True),
     C('ApprovedOn', 'dt', True), C('Notes', 'text', True, prot=SENS_O)],
    idx=[('Day', ['DepartmentId', 'IncidentActivationId', 'IncurredOn'])])

add('IncidentRateSnapshots', 'IncidentRateSnapshot', C_, 295, 'CostEntities',
    "A rate schedule entry as applied (plan §10 R18) — never a second rate model. A person's rate sits in PersonalAmount (protected); equipment, vendor and contract rates in Amount.",
    [C('SubjectKind', 'int', enum='IncidentRateSubjectKinds'), C('SubjectId', 'ref', True), C('RateScheduleId', 'ref', True), C('RateScheduleEntryId', 'ref', True),
     C('VendorAgreementId', 'ref', True), C('Description', 's500', True), C('Amount', 'qty', True), C('Unit', 's16'), C('Currency', 's3'),
     C('EffectiveOn', 'date', True), C('RulesJson', 'text', True), C('PersonalAmount', 'text', True, prot=PII_P)],
    mutable=False, soft=False, ver=False, idx=[('Subject', ['DepartmentId', 'SubjectId'])])

add('IncidentCostTransactions', 'IncidentCostTransaction', C_, 295, 'CostEntities',
    "One dated cost fact in exactly one measure (plan §13.4): Estimated, Reserved, Committed, Accrued, Actual, Invoiced or Posted are never blended. Labor priced from a personal rate keeps its amount in ProtectedAmount.",
    [C('IncidentOperationalPeriodId', 'ref', True), C('Measure', 'int', enum='IncidentCostMeasures'), C('Category', 'int', enum='IncidentCostCategories'),
     C('SourceType', 's32', True), C('SourceId', 'ref', True), C('IncidentResourceId', 'ref', True), C('IncidentParticipantId', 'ref', True),
     C('VendorId', 'ref', True), C('IncidentBudgetLineId', 'ref', True), C('IncidentFundingCodeId', 'ref', True), C('IncidentCostGroupId', 'ref', True),
     C('Section', 'int', enum='IncidentSections'), C('IncidentFacilityId', 'ref', True), C('Agency', 's200', True), C('IncidentRecoveryProjectId', 'ref', True),
     C('MemberActivationId', 'ref', True), C('CostDate', 'date'), C('Quantity', 'qty', True), C('Unit', 's16', True), C('Amount', 'money', True),
     C('Currency', 's3'), C('IsSystemGenerated', 'bool'), C('AdjustsTransactionId', 'ref', True), C('RunId', 'ref', True),
     C('ProtectedAmount', 'text', True, prot=PII_P)],
    mutable=False, soft=False, ver=False,
    idx=[('Day', ['DepartmentId', 'IncidentActivationId', 'CostDate', 'Measure']), ('Source', ['DepartmentId', 'SourceType', 'SourceId'])])

add('IncidentAccrualSnapshots', 'IncidentAccrualSnapshot', C_, 295, 'CostEntities',
    "A daily rollup by measure and category produced by the daily cost worker (plan §15.3a: aggregates are unprotected totals with a minimum-cell rule).",
    [C('AsOfDate', 'date'), C('Measure', 'int', enum='IncidentCostMeasures'), C('Category', 'int', enum='IncidentCostCategories'),
     C('Amount', 'money'), C('Currency', 's3'), C('SubjectCount', 'int'), C('RunId', 'ref', True)],
    mutable=False, soft=False, ver=False, idx=[('Day', ['DepartmentId', 'IncidentActivationId', 'AsOfDate'])])

add('IncidentCostProjections', 'IncidentCostProjection', C_, 295, 'CostEntities',
    "A cost projection scenario: daily burn, cumulative to date and forecast at completion (plan §9.13).",
    [C('ScenarioName', 's200'), C('ProjectedEndOn', 'date', True), C('DailyBurn', 'money'), C('CumulativeToDate', 'money'), C('ForecastAtCompletion', 'money'),
     C('Currency', 's3'), C('Assumptions', 's2000', True), C('RunId', 'ref', True)],
    mutable=False, soft=False, ver=False, idx=[('Activation', ['DepartmentId', 'IncidentActivationId', 'AddedOn'])])

add('IncidentCostShareAgreements', 'IncidentCostShareAgreement', C_, 295, 'CostEntities',
    "A versioned cost-share agreement (plan §9.23). Amendments create a new version; the signed document is an RMS record.",
    [C('Name', 's200'), C('Version', 'int'), C('Basis', 'int', enum='IncidentCostShareBases'), C('EffectiveFrom', 'date', True), C('EffectiveTo', 'date', True),
     C('RecordId', 'ref', True), C('Status', 'int', enum='IncidentCostShareStatuses'), C('PartiesJson', 'text', True), C('SupersedesAgreementId', 'ref', True)])

add('IncidentCostShareRules', 'IncidentCostShareRule', C_, 295, 'CostEntities',
    "An ordered apportionment rule of an agreement version (plan §9.23).",
    [C('IncidentCostShareAgreementId', 'ref'), C('RuleOrder', 'int'), C('ScopeKind', 'int', enum='IncidentCostShareScopes'), C('ScopeValue', 's100', True),
     C('Method', 'int', enum='IncidentCostShareMethods'), C('ParametersJson', 'text', True)],
    soft=False, idx=[('Agreement', ['DepartmentId', 'IncidentCostShareAgreementId', 'RuleOrder'])])

add('IncidentApportionmentRuns', 'IncidentApportionmentRun', C_, 295, 'CostEntities',
    "A reproducible apportionment run: agreement version, rule versions, the cost cut consumed and its checksum (plan §9.23).",
    [C('IncidentCostShareAgreementId', 'ref'), C('AgreementVersion', 'int'), C('RunKind', 'int', enum='IncidentApportionmentRunKinds'), C('ThroughDate', 'date'),
     C('InputChecksum', 's64'), C('OutputChecksum', 's64'), C('TotalApportioned', 'money'), C('Currency', 's3')],
    mutable=False, soft=False, ver=False, idx=[('Agreement', ['DepartmentId', 'IncidentCostShareAgreementId'])])

add('IncidentCostAllocations', 'IncidentCostAllocation', C_, 295, 'CostEntities',
    "A party's apportioned share from a run; re-apportionment adds adjusting rows, never in-place edits (plan §9.23).",
    [C('IncidentApportionmentRunId', 'ref'), C('IncidentCostShareRuleId', 'ref', True), C('PartyKey', 's128'), C('PartyName', 's200'),
     C('Category', 'int', enum='IncidentCostCategories'), C('Amount', 'money'), C('Currency', 's3'), C('Method', 'int', enum='IncidentCostShareMethods'),
     C('AdjustsAllocationId', 'ref', True)],
    mutable=False, soft=False, ver=False, idx=[('Run', ['DepartmentId', 'IncidentApportionmentRunId'])])

# ---------------------------------------------------------------- LI-C claims, closeout, recovery, intake (M0296)
add('IncidentClaims', 'IncidentClaim', C_, 296, 'ClaimsEntities',
    "The claim case ledger (plan §9.14): deadlines, reserves and settlement references. The injury, exposure and loss facts are RMS restricted records and are never copied here.",
    [C('ClaimNumber', 's32'), C('Kind', 'int', enum='IncidentClaimKinds'), C('Status', 'int', enum='IncidentClaimStatuses'), C('ClaimantName', 's200'),
     C('IncidentParticipantId', 'ref', True), C('ClaimantContactId', 'user', True), C('ResponsibleUnit', 's200', True), C('AssignedUserId', 'user', True),
     C('DeadlineOn', 'date', True), C('RecordId', 'ref', True), C('Currency', 's3'), C('OpenedOn', 'dt'), C('ClosedOn', 'dt', True),
     C('DeadlineNotifiedOn', 'dt', True), C('FinancialsJson', 'text', True, prot=PII_P), C('CaseNotes', 'text', True, prot=PII_P)],
    idx=[('Deadline', ['DepartmentId', 'IncidentActivationId', 'DeadlineOn'])], uidx=[('Number', ['DepartmentId', 'ClaimNumber'], 'live')])

add('IncidentCloseoutChecklistItems', 'IncidentCloseoutChecklistItem', C_, 296, 'ClaimsEntities',
    "A closeout gate item with blocking flag and authorized waiver (plan §9.16). Closing requires the gate or recorded waivers.",
    [C('Category', 'int', enum='IncidentCloseoutCategories'), C('Name', 's200'), C('OwnerPosition', 's32', True), C('IsBlocking', 'bool'),
     C('Status', 'int', enum='IncidentGateItemStatuses'), C('CompletedOn', 'dt', True), C('CompletedByUserId', 'user', True),
     C('WaivedReason', 's500', True), C('WaivedByUserId', 'user', True)])

add('IncidentDisasterDeclarations', 'IncidentDisasterDeclaration', C_, 296, 'RecoveryEntities',
    "A FEMA Public Assistance or state program declaration (plan §9.26). Resgrid never determines eligibility or computes a payable federal share.",
    [C('DeclarationNumber', 's32'), C('Program', 'int', enum='IncidentRecoveryPrograms'), C('State', 's8', True), C('IncidentPeriodStart', 'date', True),
     C('IncidentPeriodEnd', 'date', True), C('EligibleCategories', 's32', True), C('ApplicantId', 's64', True), C('FederalSharePercent', 'pct', True),
     C('NonFederalSharePercent', 'pct', True), C('Status', 'int', enum='IncidentDeclarationStatuses'), C('EligibilityPolicyVersion', 's64', True),
     C('RateScheduleYear', 'int', True)])

add('IncidentRecoveryProjects', 'IncidentRecoveryProject', C_, 296, 'RecoveryEntities',
    "An applicant's project / damage-inventory line by PA category (plan §9.26); cost rows tag it.",
    [C('IncidentDisasterDeclarationId', 'ref'), C('Title', 's200'), C('Category', 's2'), C('IncidentFacilityId', 'ref', True),
     C('ContactId', 'user', True), C('Location', 's500', True), C('ExternalProjectNumber', 's64', True), C('Status', 'int', enum='IncidentDeclarationStatuses')],
    idx=[('Declaration', ['DepartmentId', 'IncidentDisasterDeclarationId'])])

add('IncidentRecoveryDeadlines', 'IncidentRecoveryDeadline', C_, 296, 'RecoveryEntities',
    "A disaster-recovery obligation (plan §9.26): request-for-assistance window, damage inventory, project completion, extension expiry. Notified at most once.",
    [C('IncidentDisasterDeclarationId', 'ref'), C('IncidentRecoveryProjectId', 'ref', True), C('Kind', 'int', enum='IncidentRecoveryObligationKinds'),
     C('DueOn', 'date'), C('Status', 'int', enum='IncidentGateItemStatuses'), C('NotifiedOn', 'dt', True), C('Notes', 's500', True)],
    idx=[('Due', ['DepartmentId', 'DueOn'])])

add('IncidentIntakeItems', 'IncidentIntakeItem', C_, 296, 'IntakeEntities',
    "Evidence arriving by email, upload, vendor portal or self-service page (plan §9.27). Unknown senders are quarantined; a person always files.",
    [C('Channel', 'int', enum='IncidentIntakeChannels'), C('SenderKind', 'int', enum='IncidentIntakeSenderKinds'), C('SenderReferenceId', 's128', True),
     C('ReceivedOn', 'dt'), C('FileCount', 'int'), C('Status', 'int', enum='IncidentIntakeStatuses'), C('TargetType', 's32', True), C('TargetId', 'ref', True),
     C('EvidenceArtifactId', 'ref', True), C('FiledOn', 'dt', True), C('FiledByUserId', 'user', True), C('ProviderMessageId', 's128', True),
     C('RejectReason', 's500', True), C('LooksLikeShiftTicket', 'bool'),
     C('SenderAddress', 'text', True, prot=PII_C), C('Subject', 'text', True, prot=PII_C)],
    soft=False, idx=[('Status', ['DepartmentId', 'IncidentActivationId', 'Status'])],
    uidx=[('Message', ['DepartmentId', 'ProviderMessageId'], 'ProviderMessageId')])

add('IncidentIntakeFiles', 'IncidentIntakeFile', C_, 296, 'IntakeEntities',
    "A file attached to an intake item; classified Restricted by default.",
    [C('IncidentIntakeItemId', 'ref'), C('FileName', 's260'), C('ContentType', 's100'), C('Size', 'long'), C('Data', 'bin')],
    mutable=False, soft=False, ver=False, idx=[('Item', ['DepartmentId', 'IncidentIntakeItemId'])])

# ---------------------------------------------------------------- LI-D Coordination (M0297)
D = 'D'
add('CoordinationBoards', 'CoordinationBoard', D, 297, 'CoordinationEntities',
    "A configurable status board (plan §9.25.3a): department or activation scope. Frozen and snapshotted once into an RMS evidence artifact on close.",
    [C('IncidentActivationId', 'ref', True), C('Name', 's200'), C('Scope', 'int', enum='CoordinationBoardScopes'), C('DefaultStalenessMinutes', 'int'),
     C('IsArchived', 'bool'), C('IsFrozen', 'bool'), C('FrozenOn', 'dt', True), C('SnapshotArtifactId', 'ref', True), C('Description', 's1000', True)],
    scope='dept', idx=[('Activation', ['DepartmentId', 'IncidentActivationId'])])

add('CoordinationBoardLanes', 'CoordinationBoardLane', D, 297, 'CoordinationEntities',
    "A lane: a saved query from the lane-type code catalog with a display rule and staleness threshold (plan §9.25.3a). No board designer executes anything.",
    [C('CoordinationBoardId', 'ref'), C('LaneType', 's64'), C('Name', 's200'), C('SortOrder', 'int'), C('FilterJson', 'text', True),
     C('DisplayRule', 's32', True), C('StalenessMinutes', 'int'), C('AlarmOnStale', 'bool'), C('LastSourceOn', 'dt', True), C('StaleSince', 'dt', True)],
    scope='dept', idx=[('Board', ['DepartmentId', 'CoordinationBoardId', 'SortOrder'])])

add('CoordinationShareGrants', 'CoordinationShareGrant', D, 297, 'CoordinationEntities',
    "A scoped, revocable, audited read of named board lanes for a partner agency (plan §9.25.1). No protected value is ever on a board.",
    [C('CoordinationBoardId', 'ref'), C('CoordinationBoardLaneId', 'ref', True), C('PartnerContactId', 'user', True), C('PartnerName', 's200'),
     C('Scope', 's32', True), C('TokenHash', 's128'), C('ExpiresOn', 'dt'), C('RevokedOn', 'dt', True), C('RevokedByUserId', 'user', True), C('LastAccessedOn', 'dt', True)],
    scope='dept', soft=False, idx=[('Board', ['DepartmentId', 'CoordinationBoardId'])], uidx=[('Token', ['TokenHash'], None)])

add('IncidentLifelineStatuses', 'IncidentLifelineStatus', D, 297, 'CoordinationEntities',
    "A FEMA community-lifeline component status as of a time (plan §9.25). Append-only history; current is the latest per component.",
    [C('Lifeline', 'int', enum='IncidentLifelines'), C('Component', 's100'), C('Status', 'int', enum='IncidentLifelineColors'), C('AsOf', 'dt'),
     C('Source', 's200', True), C('Notes', 's1000', True)],
    mutable=False, soft=False, ver=False, idx=[('Lifeline', ['DepartmentId', 'IncidentActivationId', 'Lifeline', 'AsOf'])])

add('IncidentOpenActions', 'IncidentOpenAction', D, 297, 'CoordinationEntities',
    "An open action with owner and due date (plan §9.25).",
    [C('Title', 's200'), C('Description', 's2000', True), C('OwnerUserId', 'user', True), C('OwnerName', 's200', True), C('DueOn', 'dt', True),
     C('Status', 'int', enum='IncidentOpenActionStatuses'), C('NotifiedOn', 'dt', True)],
    idx=[('Due', ['DepartmentId', 'IncidentActivationId', 'DueOn'])])

add('IncidentDecisions', 'IncidentDecision', D, 297, 'CoordinationEntities',
    "A decision in flight (plan §9.25): question, options, owner and deadline. Once decided it is written to an RMS decision-log record and closes.",
    [C('Question', 's1000'), C('OptionsJson', 'text', True), C('OwnerUserId', 'user', True), C('OwnerName', 's200', True), C('DeadlineOn', 'dt', True),
     C('Status', 'int', enum='IncidentDecisionStatuses'), C('Decision', 's2000', True), C('DecidedOn', 'dt', True), C('ClosedToRecordId', 'ref', True), C('NotifiedOn', 'dt', True)],
    idx=[('Deadline', ['DepartmentId', 'IncidentActivationId', 'DeadlineOn'])])

add('IncidentSituationalFeatures', 'IncidentSituationalFeature', D, 297, 'CoordinationEntities',
    "A road closure, shelter, healthcare facility, critical infrastructure or evacuation zone (plan §9.25.3a). Shelters store capacity and occupancy counts only, never persons.",
    [C('FeatureType', 'int', enum='IncidentSituationalFeatureTypes'), C('Name', 's200'), C('Status', 'int', enum='IncidentSituationalStatuses'),
     C('GeometryJson', 'text', True), C('CapacityTotal', 'int', True), C('CapacityAvailable', 'int', True), C('ThresholdPercent', 'pct', True),
     C('AsOf', 'dt'), C('Source', 's200', True), C('Notes', 's1000', True)],
    idx=[('Type', ['DepartmentId', 'IncidentActivationId', 'FeatureType'])])
