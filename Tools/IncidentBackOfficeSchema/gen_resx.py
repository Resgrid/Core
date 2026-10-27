import io, os, re, sys, json, subprocess
from xml.sax.saxutils import escape

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_JSON = os.path.join(HERE, 'out')
os.makedirs(OUT_JSON, exist_ok=True)
sys.path.insert(0, HERE)
import schema
from gen_web import gen_tables

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
OUT_DIR = os.path.join(ROOT, 'Core', 'Resgrid.Localization', 'Areas', 'User', 'IncidentBackOffice')
ENUMS = os.path.join(ROOT, 'Core', 'Resgrid.Model', 'IncidentLogistics', 'IncidentLogisticsEnums.cs')
POLICIES = os.path.join(ROOT, 'Core', 'Resgrid.Model', 'IncidentLogistics', 'IncidentBackOfficePolicies.cs')
CATALOG = os.path.join(ROOT, 'Web', 'Resgrid.Web', 'Areas', 'User', 'Models', 'IncidentBackOffice', 'IncidentLedgerCatalog.Generated.cs')

ACRONYMS = {'Iap': 'IAP', 'Eta': 'ETA', 'Sla': 'SLA', 'Nims': 'NIMS', 'Nwcg': 'NWCG', 'Ciccs': 'CICCS', 'Emac': 'EMAC', 'Mre': 'MRE', 'Ic': 'IC', 'Eoc': 'EOC',
            'Ppe': 'PPE', 'Pa': 'PA', 'Fema': 'FEMA', 'Url': 'URL', 'Json': 'JSON', 'Csv': 'CSV', 'Poi': 'POI', 'Pii': 'PII', 'Phi': 'PHI', 'Sms': 'SMS'}


def words(name):
    parts = re.sub(r'(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])', ' ', name).split()
    out = []
    for i, p in enumerate(parts):
        p = ACRONYMS.get(p, p)
        out.append(p if (i == 0 or p.isupper() and len(p) > 1) else p.lower())
    return ' '.join(out)


def field_label(name):
    if name.endswith('Id') and len(name) > 2 and name not in ('CallId',):
        name = name[:-2]
    for prefix in ('Incident', 'Department'):
        if name.startswith(prefix) and len(name) > len(prefix) and name[len(prefix)].isupper():
            name = name[len(prefix):]
    text = words(name)
    return text[0].upper() + text[1:]


def kind_label(table):
    for prefix in ('Incident',):
        if table.startswith(prefix) and table[len(prefix)].isupper():
            table = table[len(prefix):]
    text = words(table)
    return text[0].upper() + text[1:]


UI = {
    # Module and shell
    'IncidentBackOffice': 'Incident Back Office', 'Incidents': 'Incidents', 'IncidentContext': 'Incident context', 'Sections': 'Sections', 'Tables': 'Tables',
    'SelectorIntro': 'Pick the incident to work on. Every desk shows the selected incident until you switch.',
    'Section.Command': 'Command', 'Section.Planning': 'Planning', 'Section.Logistics': 'Logistics', 'Section.Finance': 'Finance / Admin',
    'Section.Records': 'Records', 'Section.Coordination': 'Coordination', 'Section.Department': 'Department setup',
    'Nav.Incidents': 'Incidents', 'Nav.CommandDesk': 'Command desk', 'Nav.OperationsDesk': 'Operations', 'Nav.Periods': 'Periods & plan',
    'Nav.Organization': 'Organization', 'Nav.Participants': 'Participants', 'Nav.CheckIn': 'Check-in', 'Nav.Demob': 'Demobilization',
    'Nav.LogisticsDesk': 'Logistics desk', 'Nav.Requests': 'Requests', 'Nav.Facilities': 'Facilities', 'Nav.Lodging': 'Lodging', 'Nav.Feeding': 'Feeding',
    'Nav.Water': 'Water', 'Nav.Services': 'Services', 'Nav.GroundSupport': 'Ground support', 'Nav.Communications': 'Communications',
    'Nav.IncidentVendors': 'Vendors on incident', 'Nav.FinanceDesk': 'Finance desk', 'Nav.Procurement': 'Procurement', 'Nav.Budget': 'Budget',
    'Nav.Time': 'Time', 'Nav.Cost': 'Cost', 'Nav.Recovery': 'Disaster recovery', 'Nav.Intake': 'Intake', 'Nav.Claims': 'Claims', 'Nav.Iap': 'Incident action plan',
    'Nav.Closeout': 'Closeout', 'Nav.Boards': 'Coordination boards', 'Nav.Situation': 'Situation', 'Nav.Teams': 'Incident teams', 'Nav.Positions': 'Positions',
    'Nav.VendorCatalog': 'Vendor catalog', 'Nav.SupportPlan': 'Support plan', 'Nav.Authority': 'Authority profiles', 'Nav.Settings': 'Settings',
    'LapsedBanner': 'The Incident Back Office add-on has lapsed. Your incidents stay readable, but nothing can be changed until the add-on is renewed.',
    'LapsedReadOnly': 'The Incident Back Office add-on has lapsed, so this change was not saved.',
    'RenewAddon': 'Renew the add-on', 'ExerciseBanner': 'EXERCISE — this activation is an exercise; nothing here is a real incident.',
    'Exercise': 'Exercise', 'ComplexMember': 'Part of complex', 'ComplexOf': 'Complex of {0} incidents', 'PeriodShort': 'Period {0}',
    'NoCurrentPeriod': 'No current operational period', 'AuthorityResgrid': 'Resgrid is the system of record',
    'SnapshotStale': 'Numbers may be out of date (last updated {0})', 'SnapshotPending': 'Numbers are not computed yet', 'SnapshotAsOf': 'Numbers as of {0}',
    'SwitchIncident': 'Switch incident', 'Saved': 'Saved.', 'SaveFailed': 'The change could not be saved. Try again, and contact support if it keeps happening.',
    # Generic ledger
    'Actions': 'Actions', 'Open': 'Open', 'New': 'New', 'NewOf': 'New {0}', 'EditOf': '{0}', 'Search': 'Search', 'Any': 'Any', 'Filter': 'Filter', 'Clear': 'Clear',
    'ExportCsv': 'Export CSV', 'NoRows': 'Nothing here yet.', 'Untitled': '(untitled)', 'Previous': 'Previous', 'Next': 'Next', 'RowCount': 'Rows',
    'ForParent': 'For {0}', 'ForThisIncident': 'Only for this incident', 'ForThisIncidentHelp': 'Unchecked, the row joins the department catalog every incident can use.',
    'RevealProtected': 'Reveal protected fields', 'ProtectedField': 'Protected field', 'Save': 'Save', 'Cancel': 'Cancel', 'Delete': 'Delete',
    'ConfirmDelete': 'Delete this record? It is kept in the audit history.', 'Lifecycle': 'Lifecycle', 'CurrentStatus': 'Current status', 'MoveTo': 'Move to',
    'Reason': 'Reason', 'ReasonHelp': 'Required when rejecting, cancelling or waiving.', 'ChangeStatus': 'Change status', 'Related': 'Related records',
    'Yes': 'Yes', 'No': 'No', 'Select': 'Select', 'Selected': 'Selected', 'Line': 'Line', 'Problems': 'Problems', 'Missing': 'Missing',
    # Selector, activation, open
    'IncludeClosed': 'Include closed', 'OpenOnly': 'Open incidents only', 'OpenActivation': 'Open an incident', 'OpenDesk': 'Open desk',
    'ActivationRecord': 'Activation record', 'NoActivations': 'No incidents are open in the back office.',
    'OpenActivationIntro': 'Start the back office for an incident, complex, planned event or EOC activation.',
    'StepIdentity': 'Identity', 'StepLinks': 'Links', 'StepLocale': 'Time zone, currency and authority', 'ComplexityTypeN': 'Type {0}',
    'FromDepartment': 'From the department', 'Metric': 'Metric', 'Customary': 'Customary (US)', 'DefaultProfile': 'Department default profile',
    'StartActive': 'Start active', 'CreateFirstPeriod': 'Create the first operational period now', 'ComplexMembers': 'Incidents in this complex',
    'ActivationStatusHelp': 'Closing needs every blocking closeout item done or waived; reopening needs a reason.',
    'Help.IncidentActivation.Kind': 'A complex groups several incidents; a planned event or EOC activation uses the same desks.',
    'Help.IncidentActivation.ShortCode': 'Letters, digits, - and _; shown on badges, numbers and notices.',
    'Help.IncidentActivation.IsExercise': 'Exercises are marked everywhere and never notify external people or vendors unless settings allow it.',
    'Help.IncidentActivation.CallId': 'Link the dispatch call so the back office can read its resources and command board.',
    'Help.IncidentActivation.TimeZoneId': 'Times on every desk of this incident show in this zone.',
    'Help.IncidentActivation.AuthorityProfileId': 'Which system owns ordering, qualifications, finance and the IAP for this incident.',
    # Command / operations / logistics / finance desks
    'AsOf': 'As of {0}', 'Inbound': 'Inbound', 'Outbound': 'Outbound', 'Window': 'Window', 'Apparatus': 'Apparatus', 'Personnel': 'Personnel',
    'Predicted': 'Predicted timeouts', 'Arrivals': 'Arrivals', 'CurrentHolders': 'Who holds which position now', 'NoHolders': 'No positions are filled.',
    'OperationsIntro': 'Who and what is on the incident, with support status kept apart from the position held.',
    'LogisticsIntro': 'Requests, arrivals, lodging and services for the selected incident.',
    'FinanceIntro': 'Budget against commitments, the latest cost projection and the finance queues.',
    'PersonnelPicture': 'Personnel picture', 'PositionHeld': 'Position held', 'Unmatched': 'Not a member', 'UnmatchedHelp': 'This participant is not linked to a department member.',
    'Committed': 'Committed', 'OpenCommitments': '{0} open commitments', 'OverThreshold': '(over threshold)',
    'Window.2h': 'Next 2 hours', 'Window.6h': '2 to 6 hours', 'Window.12h': '6 to 12 hours', 'Window.24h': '12 to 24 hours',
    'Metric.participants.total': 'Participants', 'Metric.participants.on_incident': 'People on the incident', 'Metric.participants.inbound': 'People inbound',
    'Metric.resources.on_incident': 'Resources on the incident', 'Metric.resources.inbound': 'Resources inbound', 'Metric.resources.personnel': 'Personnel in resources',
    'Metric.requests.open': 'Open requests', 'Metric.requests.critical_unfilled': 'Critical requests unfilled', 'Metric.requests.overdue': 'Requests past need-by',
    'Metric.fills.en_route': 'Fills en route', 'Metric.demob.pending': 'Pending demobilizations', 'Metric.lodging.beds_held_tonight': 'Beds held tonight',
    'Metric.lodging.beds_available_tonight': 'Beds free tonight', 'Metric.lodging.needed': 'People needing lodging', 'Metric.service.open': 'Open service tickets',
    'Metric.finance.committed': 'Committed spend', 'Metric.finance.daily_burn': 'Yesterday\'s cost', 'Metric.finance.cumulative': 'Cost to date',
    'Metric.finance.time_to_certify': 'Time to certify', 'Metric.finance.time_to_approve': 'Time to approve', 'Metric.finance.invoice_exceptions': 'Invoice exceptions',
    # Periods and organization
    'PeriodsIntro': 'Operational periods in the incident\'s time zone, and the plan assembled for each.', 'StartPeriod': 'Start', 'CopyForward': 'Copy forward',
    'FreezeApprove': 'Freeze and approve', 'AssemblePlan': 'Assemble plan', 'ApprovePlan': 'Approve plan', 'NewPeriod': 'New period', 'Frozen': 'Frozen',
    'AssignPosition': 'Assign a position', 'AssignPositionIntro': 'Qualifications are checked before the assignment is saved; conflicts are reported, never double-booked.',
    'AssignUserHelp': 'A member, or leave empty and name a placeholder.', 'OverrideQualification': 'Override the qualification check',
    'OverrideQualificationHelp': 'Administrators only; the assignment is recorded as waived with your note.',
    'AssignedWithConflicts': 'Assigned. {0} other open assignment(s) overlap for this person.', 'EndAssignment': 'End assignment',
    'ConfirmEndAssignment': 'End this assignment now?',
    # Participants and check-in
    'EnrollmentQueue': 'Enrollment queue', 'EnrollmentQueueIntro': 'People and units dispatched to or placed on the incident who have no participant record yet.',
    'EnrollmentQueueEmpty': 'Everyone on the incident is enrolled.', 'EnrollAsDraft': 'Enroll as drafts for review', 'EnrollSelected': 'Enroll selected',
    'EnrollSkipped': '{0} were already enrolled and were skipped.', 'NothingSelected': 'Select at least one row.', 'SeenSince': 'Seen since',
    'Candidate.Person': 'Member', 'Candidate.Unit': 'Unit', 'Candidate.AdHocPerson': 'Ad-hoc person', 'Candidate.AdHocUnit': 'Ad-hoc unit',
    'CandidateSource.Dispatch': 'Dispatched', 'CandidateSource.Board': 'Command board', 'CandidateSource.IC': 'Incident command',
    'RosterImport': 'Roster import', 'RosterImportIntro': 'Stage a roster CSV, review every row, then commit the valid rows.', 'RosterFile': 'CSV file',
    'RosterPaste': 'Or paste the CSV', 'RosterFormatHelp': 'First row is the header; DisplayName is required. Agency, HomeUnit, PositionTitle, OrderNumber, RequestNumber, PaymentClass and ExpectedArrival are optional.',
    'StageRoster': 'Review roster', 'RosterReview': '{0} of {1} rows are ready', 'RosterDuplicate': 'Already on the incident; will be skipped.',
    'CommitRoster': 'Commit {0} rows', 'StartOver': 'Start over', 'RosterCommitted': '{0} participants added.',
    'CheckIn': 'Check in', 'CheckInIntro': 'Scan or type the check-in code, or pick from the people expected.', 'CheckInByCode': 'Check in by code',
    'Find': 'Find', 'CheckInCodeNotFound': 'No participant on this incident has that code.', 'AlreadyCheckedIn': 'Checked in {0}',
    'ExpectedArrivals': 'Expected arrivals', 'NoExpectedArrivals': 'Nobody is expected right now.', 'SnapshotQualifications': 'Snapshot qualifications',
    'QualificationsSnapshotted': '{0} qualifications recorded.',
    # Teams and positions
    'TeamReadiness': 'Team readiness', 'TeamReadinessIntro': 'Computed for the window you choose; nothing is stored.', 'WindowStart': 'Window start',
    'WindowEnd': 'Window end', 'CheckReadiness': 'Check readiness', 'ReadinessGaps': '{0} slot(s) without a qualified candidate.', 'Candidate': 'Candidate',
    'Readiness': 'Readiness', 'ActivateInto': 'Activate into', 'ProposeActivation': 'Propose activation',
    'ProposeActivationIntro': 'Choose the candidate for each slot; a slot left empty is recorded as a gap.', 'LeaveGap': '(leave a gap)',
    'ConflictWith': 'already assigned to {0}', 'ActivateTeam': 'Activate team', 'TeamActivated': '{0} assigned, {1} gaps, {2} conflicts.',
    'SeedCatalogIntro': 'Adds the positions of the standard catalog that are missing. Existing positions are never changed.',
    'SeedCatalog.Nims': 'Add NIMS core positions', 'SeedCatalog.Nwcg': 'Add NWCG wildland positions', 'SeedCatalog.Ciccs': 'Add CICCS positions',
    'CatalogSeeded': '{0} positions added.',
    # Settings
    'SettingsIntro': 'Department policies of the Incident Back Office. Every number is yours to set; empty values use the documented defaults.',
    'CommaSeparated': 'Separate values with commas.', 'Notice': 'Notice', 'PositionCodes': 'Position codes',
    'PositionMapHelp': 'Position codes that receive each notice; the holders at send time are notified. Empty uses the default shown.',
    # Billing, subscription and modules
    'IncidentBackOfficeAddon': 'Incident Back Office add-on', 'ManageIncidentBackOffice': 'Manage Incident Back Office', 'ManageSubscription': 'Manage subscription',
    'AddonIntro': 'Run large incidents end to end: periods, organization and check-in, resource requests, lodging, feeding, vendors, procurement, time, cost, claims and coordination boards.',
    'AddonFeatureFoundation': 'Activations, operational periods, positions and check-in', 'AddonFeatureLogistics': 'Requests, lodging, feeding, water, services and vendors',
    'AddonFeatureFinance': 'Procurement, budget, time, cost share and claims', 'AddonFeatureCoordination': 'Coordination boards, lifelines and situation status',
    'AddonActive': 'The add-on is active.', 'AskOwner': 'Ask your department\'s managing member to add Incident Back Office.', 'Monthly': 'month',
    'CheckoutPending': 'Not purchased yet.', 'PaidThrough': 'Paid through', 'SubscriptionCancelled': 'Renewal cancelled; the add-on stays active until the paid-through date.',
    'Buy': 'Buy', 'CancelSubscription': 'Cancel renewal', 'CancelSubscriptionConfirm': 'Cancel the renewal of Incident Back Office?',
    'BillingUnavailable': 'Billing is not available right now. Try again later.', 'Links': 'Links', 'ModuleSettings': 'Module settings', 'PerDepartment': 'per department',
    'SubscriptionCardHeader': 'Incident Back Office Addon',
    'SubscriptionCardText': 'Incident Back Office runs the logistics, finance and coordination side of large incidents. Billed monthly; only the department\'s managing member can buy or cancel it.',
    'ModuleIncidentBusiness': 'Incident finance desks', 'ModuleIncidentCoordination': 'Coordination boards',
    'ModuleNote.IncidentBackOffice': 'Turns the Incident Back Office module on for your department (requires the add-on).',
    'ModuleNote.IncidentBusiness': 'Procurement, budget, time, cost and claims desks of the Incident Back Office.',
    'ModuleNote.IncidentCoordination': 'Coordination boards, lifelines and the situation desk.',
    # Errors
    'Error.Required': 'Required.', 'Error.InvalidNumber': 'Enter a number.', 'Error.InvalidValue': 'Pick one of the listed values.', 'Error.InvalidDate': 'Enter a valid date and time.',
    'Error.TooLong': 'Too long.', 'Error.RecordNotFound': 'This record no longer exists.', 'Error.InvalidPeriodWindow': 'The period must end after it starts.',
    'Error.RosterTooLarge': 'The roster is larger than 2 MB.', 'Error.DisplayNameRequired': 'DisplayName is required.', 'Error.DisplayNameTooLong': 'DisplayName is longer than 200 characters.',
    'Error.PaymentClassInvalid': 'PaymentClass is not a known value.', 'Error.ExpectedArrivalInvalid': 'ExpectedArrival is not a date.',
    'Rule.Concurrency': 'Someone else changed this record. Reload it and try again.',
    # Self-service
    'IssueSelfServiceLink': 'Issue self-service link', 'RevokeLink': 'Revoke link', 'ConfirmRevokeLink': 'Revoke this link? It stops working at once.',
    'SelfServiceLinkIssued': 'Give this link to the participant; it is shown only now and works until {1}: {0}',
    'SelfServiceTitle': 'Your incident information', 'SelfServiceUnavailable': 'This link is not available. It may have expired or been revoked; ask the incident for a new one.',
    'SelfServiceCheckInHelp': 'Show this code at the check-in desk.', 'SelfServiceMeals': 'Meals', 'SelfServiceLodging': 'Lodging',
    'SelfServiceLodgingNeedsCode': 'Your lodging details need a one-time code sent to your Resgrid account.', 'SelfServiceSendCode': 'Send me a code',
    'SelfServiceCode': 'Code', 'SelfServiceVerify': 'Show lodging', 'SelfServiceCodeSent': 'A code was sent to your Resgrid account. It is valid for 10 minutes.',
    'SelfServiceCodeUnavailable': 'A code cannot be sent for this link; ask the lodging unit of the incident for your details.',
    'SelfServiceCodeInvalid': 'That code is not valid or has expired.', 'SelfServiceTooManyRequests': 'Too many requests. Try again in a minute.',
    'SelfServiceExpires': 'This link works until {0}.', 'Notice.SelfServiceCode': 'Your Incident Back Office code is {0}. It is valid for {1} minutes.',
}

RULES = {
    'ActivationNotFound': 'The incident was not found.', 'ActivationSealed': 'The incident is sealed; its records can no longer change.',
    'AdjustmentSelf': 'A time record cannot adjust itself.', 'ApprovalAboveDelegation': 'This amount is above your spending delegation.',
    'AssignmentSubjectRequired': 'Pick a person, a participant or name a placeholder.', 'AuthorizationAppendOnly': 'Funding authorizations cannot be changed or deleted; record a revision.',
    'BedNotAccessible': 'The assignment needs an accessible bed.', 'BedNotFound': 'The bed was not found on this incident.',
    'BedOccupied': 'The bed is already taken on {0}.', 'BedOutOfService': 'The bed is out of service.',
    'BoardAlreadyActivated': 'This board is already active for the incident.', 'BoardCallMismatch': 'The board belongs to another call.', 'BoardNotFound': 'The board was not found.',
    'BucketOutOfOrder': 'The capacity window must end after it starts.', 'BudgetLineNegative': 'Budget line {0} would go below zero.',
    'BudgetLineNotFound': 'The budget line was not found on this incident.', 'CallNotFound': 'The call was not found.',
    'CapabilityNotFound': 'The vendor capability was not found.', 'CapabilityNotVendors': 'The capability belongs to a different vendor than the engagement.',
    'CloseoutGateOpen': '{0} blocking closeout item(s) are still open.', 'CommitmentNotFound': 'The commitment was not found on this incident.',
    'CommitmentVoid': 'The commitment is void.', 'ComplexCannotNest': 'A complex cannot belong to another complex.', 'ComplexClosed': 'The complex is closed.',
    'ComplexNotFound': 'The complex was not found.', 'ComplexSelf': 'An incident cannot be its own complex.',
    'DemobBlocked': 'Release is blocked by open checklist items: {0}.', 'EndBeforeStart': 'The end must be after the start.',
    'EngagementNotFound': 'The vendor engagement was not found on this incident.', 'InvalidValue': 'One of the values is not valid.', 'NameRequired': 'A name is required.',
    'NightsOutOfOrder': 'The last night must be on or after the first night.', 'NoLifecycle': 'This record has no status to change.',
    'NotAMember': 'The person is not a current member of the department.', 'ParticipantNotFound': 'The participant was not found on this incident.',
    'PeriodOverlaps': 'The period overlaps another operational period.', 'PeriodWindowInvalid': 'The period must end after it starts.',
    'PositionNotFound': 'The position was not found.', 'ProtectedWriteRefused': 'Protected fields could not be sealed; nothing was saved.',
    'QualificationEnforced': 'The person is missing required qualifications: {0}.', 'QuantityRequired': 'Enter a quantity greater than zero.',
    'ReasonRequired': 'A reason is required for this change.', 'RecordCannotBeDeleted': 'This record cannot be deleted.', 'RecordNotFound': 'This record no longer exists.',
    'RequestNotFound': 'The request was not found on this incident.', 'RequestNotOrdered': 'Fills can be added only after the request is ordered.',
    'RequisitionNotApproved': 'The requisition is not approved.', 'RequisitionNotFound': 'The requisition was not found on this incident.',
    'SeparationOfDuties': 'The same person cannot do both steps unless single-person operation is acknowledged in settings.',
    'ShortCodeInUse': 'Another open incident already uses this short code.', 'ShortCodeInvalid': 'Use 2 to 32 letters, digits, - or _.',
    'TimeRecordLocked': 'Certified or approved time cannot be edited; record an adjustment.', 'TimeSpanOutOfOrder': 'The end must be after the start.',
    'TimeSpanTooLong': 'A time record cannot span more than {0} hours.', 'TooManyNights': 'A lodging assignment cannot exceed {0} nights.',
    'TransferNeedsSource': 'A transfer needs a source line and a positive amount.', 'TransitionNotAllowed': 'A record cannot move from {0} to {1}.',
    'VendorCapacityExceeded': 'Only {0} {1} are free in that window; an administrator can override with a reason.',
}

NOTICES = {
    'Notice.Title': 'Incident {0}', 'Notice.ExercisePrefix': 'EXERCISE: {0}',
    'Notice.PositionAssigned.Assigned': 'You are assigned as {0} on {1} ({2}).', 'Notice.PositionAssigned.Changed': 'Your assignment as {0} on {1} ({2}) changed.',
    'Notice.PositionAssigned.Ended': 'Your assignment as {0} on {1} ({2}) ended.', 'Notice.FillArrived': 'Request {0}: {1} has arrived.',
    'Notice.ClaimDeadline': 'Claim {0} is due on {1}.',
    'Notice.35': 'Position notification', 'Notice.36': 'Situation report', 'Notice.37': 'Resources assigned', 'Notice.38': 'Arrival update', 'Notice.39': 'Lodging assignment',
    'Notice.40': 'Demobilization', 'Notice.41': 'Approval required', 'Notice.42': 'Finance exception', 'Notice.43': 'Claim notice', 'Notice.44': 'Closeout', 'Notice.49': 'Resource timeout',
}

DESKS = {'Desk.IncidentRequests': 'Requests', 'Desk.IncidentFacilities': 'Facilities', 'Desk.IncidentLodging': 'Lodging', 'Desk.IncidentFeeding': 'Feeding',
         'Desk.IncidentWater': 'Water', 'Desk.IncidentServices': 'Services', 'Desk.IncidentGroundSupport': 'Ground support', 'Desk.IncidentCommunications': 'Communications',
         'Desk.IncidentVendors': 'Vendors on incident', 'Desk.Vendors': 'Vendor catalog', 'Desk.SupportPlan': 'Emergency support plan', 'Desk.IncidentProcurement': 'Procurement',
         'Desk.IncidentBudget': 'Budget', 'Desk.IncidentTime': 'Time', 'Desk.IncidentCost': 'Cost', 'Desk.IncidentRecovery': 'Disaster recovery', 'Desk.IncidentIntake': 'Intake',
         'Desk.IncidentClaims': 'Claims', 'Desk.IncidentCloseout': 'Closeout', 'Desk.CoordinationBoards': 'Coordination boards', 'Desk.IncidentSituation': 'Situation',
         'Desk.IncidentTeams': 'Incident teams', 'Desk.IncidentPositions': 'Positions', 'Desk.IncidentAuthority': 'Authority profiles', 'Desk.IncidentDemob': 'Demobilization',
         'Desk.IncidentCheckIn': 'Check-in', 'Desk.IncidentOrganization': 'Organization', 'Desk.IncidentParticipants': 'Participants', 'Desk.IncidentPeriods': 'Periods & plan',
         'Desk.IncidentIap': 'Incident action plan', 'Desk.IncidentBackOffice': 'Command'}


def enums():
    s = io.open(ENUMS, encoding='utf-8-sig').read()
    out = {}
    for m in re.finditer(r'public enum (\w+)\s*\{([^}]*)\}', s):
        for v in re.finditer(r'(\w+)\s*=\s*\d+', m.group(2)):
            text = words(v.group(1))
            out['Enum.%s.%s' % (m.group(1), v.group(1))] = text[0].upper() + text[1:]
    return out


def policies():
    s = io.open(POLICIES, encoding='utf-8-sig').read()
    out = {}
    for m in re.finditer(r'(?:/// <summary>(.*?)</summary>\s*)?public sealed class (\w+)\s*\{(.*?)\n\t\}', s, re.S):
        cls = m.group(2)
        key = cls
        out['Policy.' + key] = {'IncidentBackOfficeDefaults': 'General defaults', 'IncidentEnrollmentPolicy': 'Enrollment', 'IncidentNotificationRoutingPolicy': 'Notice routing',
                                'IncidentForecastPolicy': 'Forecasts and thresholds', 'IncidentAssignmentRulePolicy': 'Assignment length and work/rest',
                                'IncidentVendorPolicy': 'Vendors and capacity holds', 'IncidentSelfServicePolicy': 'Self-service links', 'IncidentFinancePolicy': 'Finance controls',
                                'IncidentRetentionPolicy': 'Retention', 'CoordinationBoardDefaults': 'Coordination boards'}[cls]
        body = m.group(3)
        for p in re.finditer(r'(?:/// <summary>(.*?)</summary>\s*)?public [\w<>,\[\] ?]+ (\w+) \{ get; set; \}', body, re.S):
            name = p.group(2)
            out['PolicyField.%s.%s' % (key, name)] = field_label(name)
            if p.group(1):
                help_text = re.sub(r'<see cref="(\w+)"\s*/>', lambda x: words(x.group(1)), p.group(1))
                help_text = re.sub(r'\s+', ' ', re.sub(r'<[^>]+>', '', help_text)).strip()
                help_text = re.sub(r'\(plan [^)]*\)|\(registry[^)]*\)|\bD\d+\b', '', help_text).replace('  ', ' ').strip()
                if help_text:
                    out['PolicyHelp.%s.%s' % (key, name)] = help_text
    return out


def build():
    data = {}
    data.update(UI)
    for k, v in RULES.items():
        data['Rule.' + k] = v
    data.update(NOTICES)
    data.update(DESKS)
    tables = gen_tables()
    for t in tables:
        data['Kind.' + t.entity] = kind_label(t.table)
    names = set()
    s = io.open(CATALOG, encoding='utf-8-sig').read()
    for m in re.finditer(r'F\("(\w+)", LedgerInput', s):
        names.add(m.group(1))
    for n in sorted(names):
        data.setdefault('Field.' + n, field_label(n))
    for n in ('Agency', 'RevisedAmount', 'Source', 'UserId', 'DepartmentIncidentTeamId', 'ApprovedOn', 'VersionNumber', 'AuthorityProfileId'):
        data.setdefault('Field.' + n, field_label(n))
    data.update(enums())
    data.update(policies())
    trig = io.open(os.path.join(ROOT, 'Core', 'Resgrid.Model', 'WorkflowTriggerEventType.cs'), encoding='utf-8-sig').read()
    for name, value in re.findall(r'(\w+) = (\d+),?', trig):
        if 116 <= int(value) <= 148 or 192 <= int(value) <= 207:
            label = words(name[len('Incident'):] if name.startswith('Incident') else name)
            data['Trigger.' + name] = 'Incident Back Office: ' + label[0].lower() + label[1:]
    data.update({'Field.CallId': 'Dispatch call', 'Field.IsExercise': 'Exercise', 'Field.ShortCode': 'Short code', 'Field.TimeZoneId': 'Time zone',
                 'Field.IncidentCommandId': 'Command board', 'Field.ParentActivationId': 'Complex', 'Field.AuthorityProfileId': 'Authority profile',
                 'Field.AuthorityProfileSnapshotId': 'Authority snapshot', 'Field.ResourceOrderId': 'Resource order', 'Field.UnitId': 'Unit',
                 'Field.RecordId': 'Record', 'Field.Unit': 'Unit of measure', 'Field.UnitLabel': 'Room or space label', 'Field.UnitType': 'Lodging unit type', 'Field.PoiId': 'Point of interest', 'Field.WorkOrderId': 'Work order',
                 'PolicyHelp.IncidentEnrollmentPolicy.Mode': 'Off, Suggest or Auto-draft; default Suggest.'})
    return data


def write(path, data):
    lines = ['<?xml version="1.0" encoding="utf-8"?>', '<root>',
             '  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>',
             '  <resheader name="version"><value>2.0</value></resheader>',
             '  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms</value></resheader>',
             '  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms</value></resheader>']
    for k in sorted(data, key=lambda x: x.lower()):
        lines.append('  <data name="%s" xml:space="preserve"><value>%s</value></data>' % (escape(k, {'"': '&quot;'}), escape(data[k])))
    lines.append('</root>')
    with io.open(path, 'w', encoding='utf-8', newline='') as f:
        f.write('\r\n'.join(lines) + '\r\n')


if __name__ == '__main__':
    data = build()
    # Every literal key used in code must be present.
    subprocess.run([sys.executable, os.path.join(HERE, 'scan_keys.py')], check=True, capture_output=True)
    keys = json.load(io.open(os.path.join(OUT_JSON, 'keys.json'), encoding='utf-8'))
    missing = [k for k in keys['literal'] if k not in data and not k.startswith(('KindIntro.', 'DeskIntro.'))]
    missing += ['Rule.' + r for r in keys['rules'] if 'Rule.' + r not in data]
    missing += [n for n in keys['notices'] if not n.endswith('.') and n not in data]
    if missing:
        print('MISSING', missing)
        sys.exit(1)
    write(os.path.join(OUT_DIR, 'IncidentBackOffice.resx'), data)
    write(os.path.join(OUT_DIR, 'IncidentBackOffice.en.resx'), data)
    json.dump(data, io.open(os.path.join(OUT_JSON, 'en.json'), 'w', encoding='utf-8'), indent=0, ensure_ascii=False, sort_keys=True)
    print('keys', len(data))
