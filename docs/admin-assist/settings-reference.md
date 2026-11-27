# Admin Assist settings reference

Catalog release: 2026.09.25.2

Operational changes use their owning screens. Availability and observed evidence are verified at read time.

<a id="setting-disabledautoavailable"></a>
## Disabled Auto Available

Disabled Auto Available. Disables automatic availability behavior. Review staffing/status reset interactions before changing it.

Default: false. Source: DepartmentSettingTypes.DisabledAutoAvailable.

<a id="setting-personnelsortorder"></a>
## Personnel Sort Order

Personnel Sort Order. Sort order for personnel lists; does not change availability, access or dispatch priority.

Default: owning view default. Source: DepartmentSettingTypes.PersonnelSortOrder.

<a id="setting-personnelliststatussortorder"></a>
## Personnel List Status Sort Order

Personnel List Status Sort Order. Custom ordering of personnel statuses in lists. Review all configured statuses before replacing the order.

Default: empty ordering. Source: DepartmentSettingTypes.PersonnelListStatusSortOrder.

<a id="setting-staffingsuppressstaffinglevels"></a>
## Staffing Suppress Staffing Levels

Staffing Suppress Staffing Levels. Staffing levels suppressed in supported notifications. Suppression can change who hears an alert and is not evidence of delivery.

Default: empty suppression list. Source: DepartmentSettingTypes.StaffingSuppressStaffingLevels.

<a id="field-personnelliststatusordersetting-orders"></a>
## Personnel List Status Order Setting / Orders

Personnel List Status Order Setting / Orders. Ordered status weights for supported personnel lists. This does not set availability or dispatch priority.

Default: empty. Source: PersonnelListStatusOrderSetting.Orders.

<a id="field-personnelliststatusorder-weight"></a>
## Personnel List Status Order / Weight

Personnel List Status Order / Weight. Relative list order for this status. Confirm all configured statuses remain represented.

Default: 0. Source: PersonnelListStatusOrder.Weight.

<a id="field-personnelliststatusorder-statusid"></a>
## Personnel List Status Order / Status ID

Personnel List Status Order / Status ID. Existing personnel-status identifier whose list position is being configured.

Default: unset. Source: PersonnelListStatusOrder.StatusId.

<a id="field-departmentsuppressstaffinginfo-enablesupressstaffing"></a>
## Department Suppress Staffing Info / Enable Suppress Staffing

Department Suppress Staffing Info / Enable Suppress Staffing. Enables the configured staffing-level suppression in supported notification consumers; review reachability before changing it.

Default: false. Source: DepartmentSuppressStaffingInfo.EnableSupressStaffing.

<a id="field-departmentsuppressstaffinginfo-staffinglevelstosupress"></a>
## Department Suppress Staffing Info / Staffing Levels To Suppress

Department Suppress Staffing Info / Staffing Levels To Suppress. Existing staffing levels to suppress. A suppressed level is not proof that another channel reaches the member.

Default: empty. Source: DepartmentSuppressStaffingInfo.StaffingLevelsToSupress.

<a id="personnel"></a>
## Personnel

Personnel Member accounts, contact details and status. Dispatch, notifications, permissions and reports all start from who belongs to which group, so set this up first. A volunteer department creates Station 1 and Station 2 groups, invites 24 members and assigns Engineer and Firefighter roles. Invite members or add them directly, then check that each has a mobile number or email address.

<a id="invites"></a>
## Manage Invites

Manage Invites Email invitations for new members to join the department. Dispatch, notifications, permissions and reports all start from who belongs to which group, so set this up first. A volunteer department creates Station 1 and Station 2 groups, invites 24 members and assigns Engineer and Firefighter roles. Send invitations and follow up on any that have not been accepted.

<a id="groups"></a>
## Groups & Stations

Groups & Stations Stations and groups that organize members and units. Dispatch, notifications, permissions and reports all start from who belongs to which group, so set this up first. A volunteer department creates Station 1 and Station 2 groups, invites 24 members and assigns Engineer and Firefighter roles. Create a station group for each station with its address, and organizational groups for teams, then add members.

<a id="personnel-roles"></a>
## Personnel roles

Personnel roles Roles such as Firefighter, EMT or Officer. Describe responsibility consistently without assuming that a role proves qualification. A volunteer department creates Station 1 and Station 2 groups, invites 24 members and assigns Engineer and Firefighter roles. Create the roles you dispatch or report on, and assign them to members. Upload a map icon for roles that should stand out, then set the role order on the Roles page: each person's map pin uses the icon of the first role they hold, in that order, that has one.

<a id="add-person"></a>
## Add Person

Add Person Create a member account directly instead of sending an invitation. Dispatch, notifications, permissions and reports all start from who belongs to which group, so set this up first. A volunteer department creates Station 1 and Station 2 groups, invites 24 members and assigns Engineer and Firefighter roles. Have your member list with email addresses or mobile numbers, and decide how stations or teams are grouped.

<a id="new-group"></a>
## New Group

New Group Create a station or group. Dispatch, notifications, permissions and reports all start from who belongs to which group, so set this up first. A volunteer department creates Station 1 and Station 2 groups, invites 24 members and assigns Engineer and Firefighter roles. Have your member list with email addresses or mobile numbers, and decide how stations or teams are grouped.

<a id="table-department-addressid"></a>
## Department / Address ID

Department / Address ID. Department address reference. Review the owning address and mapping consumers; it is separate from station and site addresses.

Default: unset. Source: Department.AddressId.

<a id="setting-testenabled"></a>
## Test Enabled

Test Enabled. Department testing behavior. Do not treat enabling a test setting as proof of production readiness.

Default: false. Source: DepartmentSettingTypes.TestEnabled.

<a id="setting-updatetimestamp"></a>
## Update Timestamp

Update Timestamp. Internal configuration timestamp. Read-only metadata, not an administrator-editable preference.

Default: unset. Source: DepartmentSettingTypes.UpdateTimestamp.

<a id="setting-modulesettings"></a>
## Module Settings

Module Settings. Controls module navigation and supported feature gates. Hiding a module does not delete its records or prove independent dispatch settings are disabled.

Default: all switches enabled, no label overrides. Source: DepartmentSettingTypes.ModuleSettings.

<a id="setting-require2faforadmins"></a>
## Require 2FA for Admins

Require 2FA for Admins. Administrator MFA requirement mode. Verify actual factors and recovery for each administrator before enforcement changes.

Default: 0. Source: DepartmentSettingTypes.Require2FAForAdmins.

<a id="setting-forcechatbotsecuritypin"></a>
## Force Chatbot Security Pin

Force Chatbot Security Pin. Require the member security PIN for sensitive supported chatbot and SMS actions, including members who have not opted in.

Default: false. Source: DepartmentSettingTypes.ForceChatbotSecurityPin.

<a id="setting-requirepasswordresetviaemail"></a>
## Require Password Reset Via Email

Require Password Reset Via Email. Use single-use recovery links for administrator-initiated member password resets, instead of selecting a replacement password.

Default: false. Source: DepartmentSettingTypes.RequirePasswordResetViaEmail.

<a id="setting-departmentoperatingprofile"></a>
## Department Operating Profile

Department Operating Profile. Declared organization, workforce, dispatch, seasonal, language and policy context. Recommendations never infer licenses or clinical scope from this profile.

Default: unknown context, no selected archetypes. Source: DepartmentSettingTypes.DepartmentOperatingProfile.

<a id="profile-archetypes"></a>
## Operating sectors

Operating sectors. Choose all sectors served by the department. A pack suggests administrative reviews; it does not establish qualification or clinical scope.

Default: none. Source: DepartmentOperatingProfile.Archetypes.

<a id="profile-workforcemix"></a>
## Workforce mix

Workforce mix. Describe the workforce so setup guidance can reflect career, volunteer, contract or combined operations.

Default: unknown. Source: DepartmentOperatingProfile.WorkforceMix.

<a id="profile-declaredmembercount"></a>
## Declared member count

Declared member count. Optional planning estimate; actual membership and subscription counts come from their owning sources.

Default: unset. Source: DepartmentOperatingProfile.DeclaredMemberCount.

<a id="profile-authoritativesystemreferences"></a>
## Authoritative system references

Authoritative system references. Short declared source-system labels. Saving this list does not verify a connection, an import or the freshness of external data.

Default: empty. Source: DepartmentOperatingProfile.AuthoritativeSystemReferences.

<a id="profile-dispatchmodel"></a>
## Dispatch model

Dispatch model. Record whether dispatch is central, self-directed, external or a combination. Verify each routing path through its owning settings.

Default: unknown. Source: DepartmentOperatingProfile.DispatchModel.

<a id="profile-operatinghours"></a>
## Operating hours

Operating hours. Describe continuous, scheduled, on-call or seasonal operation. This is context and does not create shifts or guarantee coverage.

Default: unknown. Source: DepartmentOperatingProfile.OperatingHours.

<a id="profile-sitegroupreferences"></a>
## Sites and groups

Sites and groups. Reference existing numeric group IDs in this department. This does not grant group access or change dispatch scope.

Default: empty. Source: DepartmentOperatingProfile.SiteGroupReferences.

<a id="profile-mutualaid"></a>
## Mutual aid participation

Mutual aid participation. Declare whether mutual aid is part of operations. Agreements, access and response eligibility require separate review.

Default: unknown. Source: DepartmentOperatingProfile.MutualAid.

<a id="profile-languagecodes"></a>
## Working languages

Working languages. Languages used by the team. This preference does not prove interpreter availability or translate operational records.

Default: empty. Source: DepartmentOperatingProfile.LanguageCodes.

<a id="profile-accessibilityneeds"></a>
## Accessibility preferences

Accessibility preferences. Record screen-reader, caption, large-text or plain-language needs for setup planning. Verify client behavior with the people who use it.

Default: empty. Source: DepartmentOperatingProfile.AccessibilityNeeds.

<a id="profile-staffingpolicyreferences"></a>
## Staffing policy documents

Staffing policy documents. Reference existing department document IDs for approved staffing policies. The save checks existence and expiry, not policy approval or adequacy.

Default: empty. Source: DepartmentOperatingProfile.StaffingPolicyReferences.

<a id="profile-qualificationpolicyreferences"></a>
## Qualification policy documents

Qualification policy documents. Reference existing department document IDs for approved qualification policies. Do not infer a license, scope of practice or deployment qualification from a role label.

Default: empty. Source: DepartmentOperatingProfile.QualificationPolicyReferences.

<a id="profile-continuityprocedurereferences"></a>
## Continuity procedure documents

Continuity procedure documents. Reference existing department document IDs for approved continuity procedures. Test fallback communications and ownership through the approved procedure.

Default: empty. Source: DepartmentOperatingProfile.ContinuityProcedureReferences.

<a id="profile-seasonstartmonthday"></a>
## Season start

Season start. Start of a declared seasonal period, in MM-DD format. Provide both start and end; this does not enable or disable resources on those dates.

Default: unset. Source: DepartmentOperatingProfile.SeasonStartMonthDay.

<a id="profile-seasonendmonthday"></a>
## Season end

Season end. End of a declared seasonal period, in MM-DD format. A period can cross a calendar year; verify actual rosters separately.

Default: unset. Source: DepartmentOperatingProfile.SeasonEndMonthDay.

<a id="profile-reviewedonutc"></a>
## Profile review time

Profile review time. Server-recorded time of the most recent validated profile save; it does not certify operational readiness.

Default: unset. Source: DepartmentOperatingProfile.ReviewedOnUtc.

<a id="profile-revision"></a>
## Profile revision

Profile revision. Server revision used to reject concurrent overwrites. Reload when another administrator has saved a newer profile.

Default: 0. Source: DepartmentOperatingProfile.Revision.

<a id="profile-expectedemailpollintervalminutes"></a>
## Expected email polling interval (minutes)

Expected email polling interval (minutes). Optional maximum time between recorded mailbox polls, from 1 to 10080 minutes. Blank means no declared polling expectation. This checks configured email connectors, not how often calls arrive; it does not measure SMS, CAD push or API health. Review the host polling schedule before setting it.

Default: unset. Source: DepartmentOperatingProfile.ExpectedEmailPollIntervalMinutes.

<a id="permission-createcall"></a>
## Create calls

Create calls. Allows creating calls, and with them the call rights that editing, closing, re-opening and deleting also need, on the web, in the apps, from the Logs page and in the chatbot. People who don't qualify can still view calls. With no saved rule everyone may.

Default: No permission row: everyone; no group lock. Source: PermissionTypes.CreateCall.

<a id="permission-createnote"></a>
## Create notes

Create notes. Allows creating notes from the apps. On the web, creating, editing and deleting notes always needs a department administrator, whatever this rule says. Admin-only notes stay visible to department administrators only. With no saved rule everyone may create notes in the apps.

Default: No permission row: everyone may create notes in the apps; on the web creating, editing and deleting notes always needs a department admin. Source: PermissionTypes.CreateNote.

<a id="permission-viewpersonalinfo"></a>
## View personal information

View personal information. Decides who sees members' email addresses, phone numbers and mailing addresses on the personnel pages, the Personnel report (on screen and when scheduled by email), the profile editor and the apps. People without it still see names, groups, roles and statuses, and everyone always sees their own details. With no saved rule everyone may. Protected fields additionally need a Protected Data Grant.

Default: No permission row: everyone; no group lock. Source: PermissionTypes.ViewPersonalInfo.

<a id="permission-viewgroupusers"></a>
## View personnel

View personnel. Decides which members a person can see in personnel lists, profiles, grids, dispatch pickers, reports, search, the apps' roster and status feeds. With the group-only box ticked, group administrators and role holders see only their own group (department administrators always see everyone). With no saved rule everyone sees everyone.

Default: No permission row: everyone; no group lock. Source: PermissionTypes.ViewGroupUsers.

<a id="permission-viewgroupunits"></a>
## View units

View units. Decides which units a person can see in unit lists, pickers, the map, crew assignments, search, the chatbot and the apps, and whose status they may set. With the group-only box ticked, people see only units of their own station (department administrators always see every unit). With no saved rule everyone sees every unit.

Default: No permission row: everyone; no group lock. Source: PermissionTypes.ViewGroupUnits.

<a id="permission-canseepersonnellocations"></a>
## View personnel locations

View personnel locations. Decides whose positions a member may see: the map, live tracking, nearest-responder distances and travel times, status positions in personnel and call activity, check-ins and personnel event history. With the group-only box ticked, people see only members of their own group. With no saved rule everyone may. A position is withheld, not the person.

Default: No permission row: everyone; no group lock. Source: PermissionTypes.CanSeePersonnelLocations.

<a id="permission-canseeunitlocations"></a>
## View unit locations

View unit locations. Decides whose unit positions a member may see: the map, live tracking, nearest-unit distances and travel times, unit event history and reports, check-ins, route deviations and call activity. With the group-only box ticked, people see only units of their own station. With no saved rule everyone may. A position is withheld, not the unit.

Default: No permission row: everyone; no group lock. Source: PermissionTypes.CanSeeUnitLocations.

<a id="permission-addpersonnel"></a>
## Add Personnel

Add Personnel. Allows adding, inviting and reactivating members. Choose department and group administrators to let group administrators add people to their own group; members they reactivate or add from an existing account also go into the group administrator's own group. With no saved rule only department administrators may. Group administrators who should also remove people need this set to include them, because removal checks it too.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.AddPersonnel.

<a id="permission-removepersonnel"></a>
## Remove Personnel

Remove Personnel. Allows removing members from the department. Choose department and group administrators to let group administrators remove people in their own group — they also need Add Personnel set the same way, because the removal page checks it. A group administrator can never remove a department administrator or the managing member. With no saved rule only department administrators may.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.RemovePersonnel.

<a id="permission-createtraining"></a>
## Create Training

Create Training. Who can create trainings and author their content and quiz (the edit page shows the answer key). Assigning and reporting (Assign Training) and archiving or deleting (Delete Training) are separate rules that follow this one until they are saved. Everyone can still view and complete the trainings assigned or open to them. With no saved rule only department administrators may.

Default: No permission row: department admins create and author trainings; everyone can view the trainings assigned or open to them; Assign Training and Delete Training follow this row. Source: PermissionTypes.CreateTraining.

<a id="permission-assigntraining"></a>
## Assign Training

Assign Training. Who can assign trainings to members, set due dates, extensions and exemptions, and read the training report and attempt history. Group admins act only on their own group's members. Until it is saved it follows Create Training.

Default: No permission row: follows the Create Training row (department admins only when neither is saved). Source: PermissionTypes.AssignTraining.

<a id="permission-deletetraining"></a>
## Delete Training

Delete Training. Who can archive, restore and delete trainings. A training someone completed is archived instead of deleted, so its completions stay on the record. Until it is saved it follows Create Training.

Default: No permission row: follows the Create Training row (department admins only when neither is saved). Source: PermissionTypes.DeleteTraining.

<a id="permission-authorprotocols"></a>
## Author Protocols

Author Protocols. Allows creating, copying and importing protocols, editing their library fields, triggers, questions and audience, writing drafts and submitting them for approval, disabling protocols and exporting the library. Authors also see drafts, the review queue and every protocol whatever its audience. Approving and publishing need Approve Protocols. With no saved rule only department administrators may.

Default: No permission row: department admins only. Source: PermissionTypes.AuthorProtocols.

<a id="permission-approveprotocols"></a>
## Approve Protocols

Approve Protocols. Allows approving or rejecting submitted protocol versions, publishing them now or on a set date, recording "reviewed, no change", retiring and deleting protocols, placing a protocol's legal hold, and managing categories and their approval steps. Each approval is signed with a fresh step-up or a passkey. With no saved rule only department administrators may.

Default: No permission row: department admins only. Source: PermissionTypes.ApproveProtocols.

<a id="permission-manageprotocolacknowledgments"></a>
## Manage Protocol Acknowledgments

Manage Protocol Acknowledgments. Reserved for read-and-sign acknowledgments: who will assign protocols for members to read and sign, exempt members and read the acknowledgment report. Read-and-sign is not available yet, so saving this rule changes nothing today. With no saved rule only department administrators may.

Default: No permission row: department admins only. Source: PermissionTypes.ManageProtocolAcknowledgments.

<a id="permission-managecourses"></a>
## Manage Courses

Manage Courses. Allows building courses and question banks, uploading course video, submitting, approving and publishing course versions, assigning courses and reading course results. Each approval is signed with a fresh step-up. Courses are part of the LMS add-on, which is not available yet; Resgrid Academy lessons can be assigned with this permission or Assign Training. With no saved rule only department administrators may.

Default: No permission row: department admins only. Source: PermissionTypes.ManageCourses.

<a id="permission-gradeassessments"></a>
## Grade Assessments

Grade Assessments. Allows grading free-text answers waiting in the grading queue and reading assessment item analysis. Nobody grades their own attempt. Part of the LMS add-on, which is not available yet. With no saved rule only department administrators may.

Default: No permission row: department admins only. Source: PermissionTypes.GradeAssessments.

<a id="permission-reviewdispatchcases"></a>
## Review Dispatch Cases

Review Dispatch Cases. Allows sampling calls for dispatch case review, scoring the recorded case against a dispatch case rubric, sending the review to the dispatcher and reading the compliance report. Nobody reviews their own calls. With no saved rule only department administrators may.

Default: No permission row: department admins only. Source: PermissionTypes.ReviewDispatchCases.

<a id="permission-recordtrainingcompletions"></a>
## Record Training Completions

Record Training Completions. Allows recording that members completed a training or LMS course they took together (from a class roster, or from a course's enrolment list or a training's assignee list with a note). Each record shows it was recorded by an instructor, and the recorder can void it with a reason. Group admins record only for their own group's members. With no saved rule only department administrators may.

Default: No permission row: department admins only. Source: PermissionTypes.RecordTrainingCompletions.

<a id="permission-managecontentsets"></a>
## Manage Content Sets

Manage Content Sets. Allows publishing protocol and course sets to linked departments, subscribing to theirs, deciding the update inbox, customizing the blocks a publisher left open, answering proposals and sponsoring a linked department's LMS seats. Any member can propose a change upstream. With no saved rule only department administrators may.

<a id="setting-contentsetsettings"></a>
## Set update time

Set update time. The local time accepted protocol and course set updates go into force on or after their effective date (default 07:00, so nothing changes mid-shift by surprise), and who is told when an update arrives, a proposal is answered, a sponsorship changes or an LTI tool needs registering: the administrators (unless turned off) and members of the roles chosen.

<a id="protocol-sets"></a>
## Protocol Sets

Protocol Sets Publish protocols to linked departments as a regional set, or subscribe to a county, medical director or corporate set. Locked content follows the publisher, customizable blocks keep local phone numbers and resources, every new version arrives as a redline in the update inbox and goes into force at your update time, and members propose changes upstream.

<a id="linked-department-protocols"></a>
## Linked Department Protocols

Linked Department Protocols Read a mutual-aid partner's published protocols over a Department Link that shares Protocols, read-only and only the version in force. Restricted, licensed and uploaded-document protocols are never shared, and turning the share off cuts access at once.

<a id="department-link-acceptance"></a>
## Signed Link Acceptance

Signed Link Acceptance Accepting a link from another organization is a step-up signature by one of your administrators, bound to the two organizations and what each shares at that moment; without the step-up the link stays unaccepted.

<a id="course-sets"></a>
## Course Sets

Course Sets Publish trainings (and, with the LMS, courses and learning paths) to linked organizations as a set; subscribers receive their own copies, locked or open as the publisher chooses, and each update arrives in their inbox to go into force at their update time. Trainings need no add-on, and quiz changes flow down with each update.

<a id="lms-course-sets"></a>
## LMS Courses And Paths In Sets

LMS Courses And Paths In Sets LMS courses and learning paths travel in course sets: hosted video and SCORM or cmi5 packages are shared with the subscriber, an LTI lesson uses the subscriber's own registration with the tool (its managers are told to finish it), and a path points at the subscriber's copies of its courses. Needs the subscriber's LMS add-on or the publisher's sponsorship.

<a id="lms-sponsorship"></a>
## Sponsored LMS Seats

Sponsored LMS Seats Pay for a linked department's LMS-enabled members from your own packs, optionally up to a seat limit. The sponsored department needs no add-on, may be on the free plan, runs your course sets only and sees who pays; ending the sponsorship or the link lapses access with the usual lapse rules.

<a id="permission-createdocument"></a>
## Create Document

Create Document. Allows uploading documents. People can edit or delete only the documents they uploaded, unless they are department administrators. Everyone can still view documents. With no saved rule everyone may upload.

Default: No permission row: everyone may upload documents; no group lock. Source: PermissionTypes.CreateDocument.

<a id="permission-createcalendarentry"></a>
## Create Calendar Entry

Create Calendar Entry. Allows creating and editing calendar entries and calendar types, and editing calendar check-ins from the apps. Editing or deleting an entry also needs to be its creator or a department administrator, and only the attendee themselves or someone who can edit the event may remove an attendee. Everyone can still view the calendar. With no saved rule everyone may.

Default: No permission row: everyone may create, edit and delete calendar entries; no group lock. Source: PermissionTypes.CreateCalendarEntry.

<a id="permission-createlog"></a>
## Create Log

Create Log. Allows writing logs. Everyone can still view them. Once Records is activated, legacy logs become read-only and this rule is copied to Create Record and Finalize Records. With no saved rule everyone may.

Default: No permission row: everyone may create logs; no group lock. Source: PermissionTypes.CreateLog.

<a id="permission-createshift"></a>
## Create Shift

Create Shift. Allows creating, editing and deleting shifts and work shifts, and managing sign-ups for every group. Group administrators always manage sign-ups for their own groups. Members signing up — on the web, in the apps or through the chatbot — must follow the shift's rules (no past days, assigned shifts need an assignment, group shifts need the member's group). Everyone can still view shifts. With no saved rule only department administrators may change shifts.

Default: No permission row: department admins create, edit and delete shifts; everyone can view; group admins still manage sign-ups for their own groups. Source: PermissionTypes.CreateShift.

<a id="permission-adjustinventory"></a>
## Adjust Inventory

Adjust Inventory. Allows receiving, adjusting and archiving inventory, using stock from Records, and is the starting rule for Transfer and Issue until those are saved. With no saved rule only department administrators may, and the Security screen shows that.

Default: No permission row: department admins only for inventory changes and Records inventory usage; the Security screen preselects Department admins. Source: PermissionTypes.AdjustInventory.

<a id="permission-createmessage"></a>
## Create Message

Create Message. Allows sending messages, posting and reacting in chat, starting direct messages and using the chatbot, including sending a message by text to the chatbot. People without it can still read and delete their messages but cannot mark them read or edit chat posts. With no saved rule everyone may.

Default: No permission row: everyone may send messages and post in chat; no group lock. Source: PermissionTypes.CreateMessage.

<a id="permission-deletecall"></a>
## Delete Call

Delete Call. Allows deleting calls. With the group-only box ticked, only department administrators and people whose group was dispatched on the call may delete it — also when the rule is Everyone. People also need the call rights from Create Call. With no saved rule anyone who can see the call may delete it.

Default: No permission row: any member who can see the call may delete it; group-only lock off. Source: PermissionTypes.DeleteCall.

<a id="permission-closecall"></a>
## Close Call

Close Call. Allows closing and re-opening calls. With the group-only box ticked, only department administrators and people whose group was dispatched on the call may close or re-open it — also when the rule is Everyone. On the web, in the apps and in the chatbot people also need the call rights from Create Call. With no saved rule anyone who can see the call may close it.

Default: No permission row: any member who can see the call may close or re-open it; group-only lock off. Source: PermissionTypes.CloseCall.

<a id="permission-addcalldata"></a>
## Add Call Data

Add Call Data. Allows adding notes, images, files and video feeds to calls on the web and in the apps. People must also be able to see the call, and attaching a file on the web additionally needs call edit rights (department administrators or the member who logged the call). With the group-only box ticked, only department administrators and people whose group was dispatched on the call may add to it — also when the rule is Everyone. With no saved rule anyone who can see the call may.

Default: No permission row: anyone who can see the call may add notes, images, files and video feeds; group-only lock off. Source: PermissionTypes.AddCallData.

<a id="permission-contactedit"></a>
## Contact Edit

Contact Edit. Allows adding and editing contacts, categories, pre-plans, hazards and site files. With no saved rule everyone may. The edit page shows the whole contact, so anyone who can edit can also read; protected fields stay redacted without a Protected Data Grant.

Default: No permission row: everyone may add and edit contacts; no group lock. Source: PermissionTypes.ContactEdit.

<a id="permission-contactview"></a>
## Contact View

Contact View. Allows viewing contacts, their notes, calls and files on the web and in the apps, finding them in search, and seeing contact details on calls: site information, contact alerts, the call contact pickers and the call export. With no saved rule everyone may, except inventory purchasing, which shows vendor contacts only to department administrators until you save a rule. Routes and the contact edit page can also show contact details.

Default: No permission row: everyone may view contacts; no group lock. Exception: inventory purchasing and vendor-contact reads treat a missing row as department admins only. Source: PermissionTypes.ContactView.

<a id="permission-contactdelete"></a>
## Contact Delete

Contact Delete. Allows deleting contacts, categories, pre-plans, premise hazards and site files. With no saved rule everyone may. A contact with open billing in Business Ops cannot be deleted.

Default: No permission row: everyone may delete contacts; no group lock. Source: PermissionTypes.ContactDelete.

<a id="permission-createworkflow"></a>
## Create Workflow

Create Workflow. Allows creating, editing and deleting workflows, previewing their conditions, reading step configuration in the apps and API, and seeing the rendered output of workflow runs. With no saved rule only department administrators can change workflows, but every member can open the Workflows page and read definitions; saving Department admins also hides workflows from everyone else. Steps that send a Records export also need Export Records and Manage Record Reports. Free plans are limited in how many workflows they can have.

Default: No permission row: department admins create, edit and delete workflows; every member can open and read workflows. The screen shows a disabled "not saved" option, and saving Department admins also hides workflows from non-admins on the web and in the API. Source: PermissionTypes.CreateWorkflow.

<a id="permission-manageworkflowcredentials"></a>
## Manage Workflow Credentials

Manage Workflow Credentials. Allows creating, editing and deleting the credentials workflows use to reach outside services. Secrets are always masked. With no saved rule only department administrators can change them, but members can see credential names and types; saving Department admins hides those too, on the web and in the API. A credential that delivers a Records export also needs Export Records and Manage Record Reports to edit. Only useful alongside Create Workflow.

Default: No permission row: department admins create, edit and delete credentials; every member can list credential names and types (secrets are always masked). The screen shows a disabled "not saved" option, and saving Department admins also hides credentials from non-admins on the web and in the API. Source: PermissionTypes.ManageWorkflowCredentials.

<a id="permission-viewworkflowruns"></a>
## View Workflow Runs

View Workflow Runs. Allows viewing workflow run history, logs, health and pending runs. What a run sent (its rendered output and result) is shown only to people who may edit workflows. With no saved rule every member can view runs and only department administrators can cancel or clear them; saving Department admins hides runs from everyone else, and saving Everyone lets everyone cancel and clear.

Default: No permission row: every member can view workflow runs, logs, health and pending runs; only department admins can cancel or clear them. The screen shows a disabled "not saved" option, and saving Department admins hides runs from non-admins. Rendered output and results show only to people who may edit workflows. Source: PermissionTypes.ViewWorkflowRuns.

<a id="permission-viewudffields"></a>
## View custom field Fields

View custom field Fields. Governs reading and saving custom field values through the apps and API. It is not shown on any configuration screen, so everyone keeps view and update access. Each value also follows its field's visibility setting and the record it belongs to: a member reads or fills in values only on calls, units, people and contacts they can see or edit. Managing custom field definitions always requires a department administrator.

Default: Not shown on any configuration screen. No permission row: every member may view and fill in custom field values through the API; department admins always have full access. Source: PermissionTypes.ViewUdfFields.

<a id="permission-manageroutes"></a>
## Manage Routes

Manage Routes. Governs routes. It is not shown on any configuration screen, so members can view routes, group administrators can also create and edit them, and only department administrators can delete. Starting a route and checking in at stops need edit access. Viewing routes also shows the department's contacts.

Default: Not shown on any configuration screen. No permission row: every member may view routes and group admins may also create and edit them; department admins always have full access. Source: PermissionTypes.ManageRoutes.

<a id="permission-deletelog"></a>
## Delete Log

Delete Log. Allows deleting logs. It is separate from Create Log. Once Records is activated, legacy logs become read-only and this rule is copied to Delete Record. With no saved rule everyone may.

Default: No permission row: everyone may delete logs; no group lock. Source: PermissionTypes.DeleteLog.

<a id="permission-usecalendarsync"></a>
## Use Calendar Sync

Use Calendar Sync. Allows members to get a personal calendar subscription link for their phone or desktop calendar. The feed is re-checked on every fetch, so narrowing this stops existing feeds, and a member removed from or disabled in the department stops receiving it. With no saved rule everyone may. The operator must also have calendar feeds turned on.

Default: No permission row: everyone may turn on calendar sync and the feed is served; no group lock. Source: PermissionTypes.UseCalendarSync.

<a id="permission-dispatchapplogin"></a>
## Dispatch App Login

Dispatch App Login. Decides who counts as a dispatcher. The Dispatch app checks it at sign-in (the server itself does not block sign-in), and dispatchers can read and are notified on dispatch-visible chat channels, including incident command and group channels. With no saved rule every active member counts. Saving it refreshes chat access.

Default: No permission row: every active member counts as a dispatcher; no group lock. Source: PermissionTypes.DispatchAppLogin.

<a id="permission-commandapplogin"></a>
## Command App Login

Command App Login. Decides who can run incident command: sign in to the Incident Command app, establish or reopen command, and work the boards where they hold incident capabilities (the incident commander and whoever established command hold all of them; assigned ICS roles grant parts). With no saved rule every active member may. When you save a narrower rule, the people it allows also get command-assist capabilities on any board. Who reads boards is View Command Boards, which follows this rule until it has its own.

Default: No permission row: every active member may run incident command; no group lock. Command assist is only granted when a rule other than Everyone is saved. Source: PermissionTypes.CommandAppLogin.

<a id="permission-viewcommandboards"></a>
## View Command Boards

View Command Boards. Decides who can read every command board: the board, accountability, needs, timeline, map, roles, voice, resources and reports, the apps' offline sync of boards, the active commands list and dashboard widget, and the chatbot's incident answers. Members on an incident always read that incident's board: command staff, lane leads, members dispatched to the call directly or through their group, a personnel role or a unit they crew, and resources placed on the board. With no saved rule it follows Command App Login, and with neither rule every active member may.

Default: No permission row: the Command App Login row decides; with neither row every active member may read every board; no group lock. Members on an incident always read that incident's board. Source: PermissionTypes.ViewCommandBoards.

<a id="permission-managedepartmentdataprotection"></a>
## Manage Department Data Protection

Manage Department Data Protection. Not offered on the Security screen, because no runtime check reads it: the Data Protection pages require a department administrator, and enrollment, offboarding, step-up exemptions and billing are limited to the managing member. A rule saved earlier is kept but has no effect.

Default: Not offered on the Security screen; not read by any runtime check. Source: PermissionTypes.ManageDepartmentDataProtection.

<a id="permission-viewprotectedcalldata"></a>
## View Protected Call Data

View Protected Call Data. Controls who may receive protected call details by SMS or voice after entering their PIN. On-screen and app reveal is not governed by it; that follows call access plus a current Protected Data Grant. Applies only when Advanced Data Protection is enrolled and PIN release is turned on.

Default: No permission row: everyone (every active member who can view the call is eligible for SMS and voice PIN release); no group lock. Source: PermissionTypes.ViewProtectedCallData.

<a id="permission-editprotectedcalldata"></a>
## Edit Protected Call Data

Edit Protected Call Data. Not offered on the Security screen, because no runtime check reads it: editing protected call fields follows the normal call edit permissions plus a current Protected Data Grant. A rule saved earlier is kept but has no effect.

Default: Not offered on the Security screen; not read by any runtime check. Source: PermissionTypes.EditProtectedCallData.

<a id="permission-viewprotectedpersonneldata"></a>
## View Protected Personnel Data

View Protected Personnel Data. Not offered on the Security screen, because no runtime check reads it: anyone who can view a member and holds a current Protected Data Grant can reveal that member's protected fields. A rule saved earlier is kept but has no effect.

Default: Not offered on the Security screen; not read by any runtime check. Source: PermissionTypes.ViewProtectedPersonnelData.

<a id="permission-viewprotectedcontactdata"></a>
## View Protected Contact Data

View Protected Contact Data. Not offered on the Security screen, because no runtime check reads it: anyone who can view contacts and holds a current Protected Data Grant can reveal protected contact fields. A rule saved earlier is kept but has no effect.

Default: Not offered on the Security screen; not read by any runtime check. Source: PermissionTypes.ViewProtectedContactData.

<a id="permission-viewprotectedoperationaldata"></a>
## View Protected Operational Data

View Protected Operational Data. Not offered on the Security screen, because no runtime check reads it: revealing protected logs, documents, calendar entries, custom fields and records follows each module's own access rules plus a current Protected Data Grant. A rule saved earlier is kept but has no effect.

Default: Not offered on the Security screen; not read by any runtime check. Source: PermissionTypes.ViewProtectedOperationalData.

<a id="permission-exportprotecteddata"></a>
## Export Protected Data

Export Protected Data. Not offered on the Security screen, because no runtime check reads it: each export follows its own module's export permission. A rule saved earlier is kept but has no effect.

Default: Not offered on the Security screen; not read by any runtime check. Source: PermissionTypes.ExportProtectedData.

<a id="permission-configureprotecteddataegress"></a>
## Configure Protected Data Egress

Configure Protected Data Egress. Controls who may administer protected workflows and their release approvals. Turning a protected workflow on, approving or renewing it also needs a fresh step-up, and the SMS and voice PIN release settings additionally require a department administrator. Applies only when Advanced Data Protection is enrolled.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ConfigureProtectedDataEgress.

<a id="permission-breakglassprotecteddata"></a>
## Break Glass Protected Data

Break Glass Protected Data. A retired identifier kept so older saved settings still load. It grants nothing, is not shown on the Security screen and cannot be saved; staff support access is controlled by the support-consent setting on the Data Protection page.

Default: Not offered on the Security screen; not read by any runtime check. Source: PermissionTypes.BreakGlassProtectedData.

<a id="permission-createrecord"></a>
## Create Record

Create Record. Allows authoring Records, including incident reports, field records, evidence and attachments. With no saved rule everyone may author. Takes effect once Records is activated; activation copies your Create Log rule here. Field apps also need their Records field flags.

Default: No permission row: everyone; no group lock. Source: PermissionTypes.CreateRecord.

<a id="permission-deleterecord"></a>
## Delete Record

Delete Record. Allows voiding Records and cancelling drafts — without it people cannot cancel even their own drafts. A member may void or cancel only Records they wrote, own, review or approve, unless they are a department administrator or the administrator of the Record's group. With no saved rule everyone may. Takes effect once Records is activated; activation copies your Delete Log rule here.

Default: No permission row: everyone; no group lock. Source: PermissionTypes.DeleteRecord.

<a id="permission-reviewrecords"></a>
## Review Records

Review Records. Allows reviewing Records and returning them for correction, and assigning review work. With no saved rule department and group administrators may. A definition's reviewer roles can narrow it further. Quality review also needs the Records quality review module.

Default: No permission row: department and group admins; no group lock. Source: PermissionTypes.ReviewRecords.

<a id="permission-approverecords"></a>
## Approve Records

Approve Records. Allows approving Records. Approval is currently done from the apps and API; on the web it only widens the accountability view. With no saved rule only department administrators may. An approver can never approve their own Record, and a definition's approver roles can narrow it further.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ApproveRecords.

<a id="permission-finalizerecords"></a>
## Finalize Records

Finalize Records. Allows finalizing Records, which locks them as the official version. A member may finalize Records they wrote, own, review or approve; finalizing someone else's needs Review Records (Approve Records once approved, Amend Records for an amendment) or a department administrator. Amendments and corrections are also completed by finalizing, so anyone who amends needs this too. With no saved rule everyone may. Activation copies your Create Log rule here.

Default: No permission row: everyone; no group lock. Source: PermissionTypes.FinalizeRecords.

<a id="permission-amendrecords"></a>
## Amend Records

Amend Records. Allows opening an amendment on a finalized Record and abandoning it. The amendment is completed by finalizing, so pair this with Finalize Records. With no saved rule department and group administrators may.

Default: No permission row: department and group admins; no group lock. Source: PermissionTypes.AmendRecords.

<a id="permission-submitrecords"></a>
## Submit Records

Submit Records. Allows submitting Records to a reporting destination such as NERIS. Submitters must be able to see the Record; reading the submission exchange history also needs View Restricted Records, and validating a submission requires a department administrator. With no saved rule only department administrators may.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.SubmitRecords.

<a id="permission-exportrecords"></a>
## Export Records

Export Records. Allows printing and exporting Records, documents, deployment reports, saved-report CSVs and bulk packets. With no saved rule everyone may. Restricted sections additionally need View Restricted Records; managing export templates, opening other people's export runs and emailing a packet to an outside address need Manage Record Reports.

Default: No permission row: everyone; no group lock. Source: PermissionTypes.ExportRecords.

<a id="permission-sharerecordsexternally"></a>
## Share Records Externally

Share Records Externally. Reserved for sharing Records with other groups or outside agencies. No current feature uses it, so it is not offered on the Security screen; it defaults to department administrators only, and a rule saved earlier is kept for when sharing arrives.

Default: Not offered on the Security screen; the claim is still issued (department admins only with no saved row) but nothing uses it yet. Source: PermissionTypes.ShareRecordsExternally.

<a id="permission-viewrestrictedrecords"></a>
## View Restricted Records

View Restricted Records. Allows seeing restricted Record sections such as casualty, exposure and investigation detail. It is also required to include restricted content in disclosures, exports and export runs, and to read the submission exchange history. With no saved rule only department administrators may.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ViewRestrictedRecords.

<a id="permission-viewlegacyrecords"></a>
## View Legacy Records

View Legacy Records. Reserved for viewing legacy Logs history from Records. No current page uses it, so it is not offered on the Security screen; legacy history follows the Logs permissions. A rule saved earlier is kept.

Default: Not offered on the Security screen; the claim is still issued (everyone with no saved row) but nothing uses it yet. Source: PermissionTypes.ViewLegacyRecords.

<a id="permission-viewgrouprecords"></a>
## View Group Records

View Group Records. Limits which groups' Records people can see. Scoping applies only when this rule's group-only box is ticked and Records Settings sets group visibility to group-scoped; then non-administrators see their own group's Records plus any they authored or take part in. The action and role choices on this row do not change visibility.

Default: No permission row: no group scoping — every active member sees all department Records. Source: PermissionTypes.ViewGroupRecords.

<a id="permission-managerecorddefinitions"></a>
## Manage Record Definitions

Manage Record Definitions. Allows creating and editing Record definitions in the designer. Publishing also needs Publish Record Definitions, and managing Record custom fields additionally requires a department administrator. With no saved rule only department administrators may.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ManageRecordDefinitions.

<a id="permission-publishrecorddefinitions"></a>
## Publish Record Definitions

Publish Record Definitions. Allows publishing and retiring Record definitions so people can use them. It only works together with Manage Record Definitions. With no saved rule only department administrators may.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.PublishRecordDefinitions.

<a id="permission-managerecordreports"></a>
## Manage Record Reports

Manage Record Reports. Allows creating, scheduling and running saved Record reports, managing export templates, opening template and scheduled export runs, and emailing bulk packets to outside addresses; it also shows the accountability view. Export templates and CSV downloads also need Export Records, and restricted reports and runs need View Restricted Records. A bulk packet always opens only for the member who built it. With no saved rule only department administrators may.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ManageRecordReports.

<a id="permission-managerecorddisclosures"></a>
## Manage Record Disclosures

Manage Record Disclosures. Allows working public-records and disclosure requests, including reading the disclosure queue and producing releases. Restricted content additionally needs View Restricted Records. With no saved rule only department administrators may.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ManageRecordDisclosures.

<a id="permission-managerecordlegalhold"></a>
## Manage Record Legal Hold

Manage Record Legal Hold. Allows placing and releasing legal holds, which stop Records from being purged. Holds on a specific Record are limited to Records the person can see. With no saved rule only department administrators may.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ManageRecordLegalHold.

<a id="permission-reassignrecorddrafts"></a>
## Reassign Record Drafts

Reassign Record Drafts. Allows handing someone else's draft Record to another active member. With no saved rule department and group administrators may, in any group they can see.

Default: No permission row: department and group admins; no group lock. Source: PermissionTypes.ReassignRecordDrafts.

<a id="permission-recordspreventionadmin"></a>
## Records Prevention Admin

Records Prevention Admin. Allows managing prevention data — occupancies, inspections, hydrants, permits and community risk reduction. Other members can still read it. Each area also needs its Records prevention module turned on. With no saved rule only department administrators may.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.RecordsPreventionAdmin.

<a id="permission-managechecklists"></a>
## Manage Checklists

Manage Checklists. Allows authoring, publishing, retiring and scheduling checklists and sending checklist work to other groups. It does not include reading results. With no saved rule only department administrators may. Needs Checklists turned on; failure actions that open work orders also need Work Orders and Readiness Pro.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ManageChecklists.

<a id="permission-viewchecklistresults"></a>
## View Checklist Results

View Checklist Results. Allows reading completed checklist results and history. With no saved rule department administrators see everything, group administrators see their own group's results, and everyone sees runs they created or witnessed. Untick the group-only box to let reviewers see every group.

Default: No permission row: department and group admins, with group admins limited to their own group; everyone still sees runs they created or witnessed. Source: PermissionTypes.ViewChecklistResults.

<a id="permission-manageworkorders"></a>
## Manage Work Orders

Manage Work Orders. Allows managing work orders across the department: approvals, costs, resolutions, holds, recurring maintenance, vendor charges and other groups' orders. It includes seeing every work order. With no saved rule only department administrators may. Needs Work Orders turned on and the Readiness Pro add-on.

Default: No permission row: department admins only; no group lock. Others can still raise work orders for their own group and work orders assigned to them. Source: PermissionTypes.ManageWorkOrders.

<a id="permission-viewallworkorders"></a>
## View All Work Orders

View All Work Orders. Allows seeing work orders beyond the ones you raised or are assigned. With no saved rule department administrators see all and group administrators see their own group's; the group-only box shows that limit ticked, so leave it ticked when you save if group administrators should stay limited to their group.

Default: No permission row: department and group admins, with group admins limited to their own group; everyone else sees work orders they raised or that are assigned to them or their roles. Source: PermissionTypes.ViewAllWorkOrders.

<a id="permission-transferinventory"></a>
## Transfer Inventory

Transfer Inventory. Allows moving stock between locations. Until you save a rule here it follows Adjust Inventory. With the group-only box ticked, people can only move stock between locations in their own group. Controlled substances also need Manage Controlled Substances.

Default: No permission row: follows the Adjust Inventory rule (action, roles and group lock); with neither saved, department admins only. Source: PermissionTypes.TransferInventory.

<a id="permission-issueinventory"></a>
## Issue Inventory

Issue Inventory. Allows issuing equipment and supplies to people and units, taking returns, issuing kits and adding new holders. Until you save a rule here it follows Adjust Inventory. Controlled substances also need Manage Controlled Substances and, where required, a witness.

Default: No permission row: follows the Adjust Inventory rule (action, roles and group lock); with neither saved, department admins only. Source: PermissionTypes.IssueInventory.

<a id="permission-managecontrolledsubstances"></a>
## Manage Controlled Substances

Manage Controlled Substances. Allows handling controlled-substance items: receiving, moving, issuing, using, witnessing, reporting and adding them to the catalog. It adds to the normal inventory permissions rather than replacing them, and witnessed steps need a second qualified person. With no saved rule only department administrators may.

Default: No permission row: department admins only; no group lock and no fallback rule. Source: PermissionTypes.ManageControlledSubstances.

<a id="permission-manageinvoicing"></a>
## Manage Invoicing

Manage Invoicing. Allows creating, sending, editing and voiding invoices, generating invoices from deployments, downloading the invoice packet, recording payments and managing rate cards and billing profiles. It includes viewing. With no saved rule only department administrators may. Needs Customer Invoicing turned on; changes need an active Business Ops add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ManageInvoicing.

<a id="permission-viewinvoicing"></a>
## View Invoicing

View Invoicing. Allows viewing invoices, aging, rate cards and contact billing profiles without changing them, including the invoice lists on deployments and contracts. The invoice packet (with time reports, receipts and compliance documents) needs Manage Invoicing. With no saved rule only department administrators may. Viewing keeps working if the Business Ops add-on lapses.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ViewInvoicing.

<a id="permission-managecertifications"></a>
## Manage Certifications

Manage Certifications. Allows adding, verifying, editing and removing other members' and units' certification records and continuing-education credits. It includes viewing. Members can always manage their own records, but nobody can verify their own, and changing your own verified record clears its verification. With no saved rule only department administrators may.

Default: No permission row: department admins only; no group lock. Every member can still manage their own certification records. Source: PermissionTypes.ManageCertifications.

<a id="permission-viewcertifications"></a>
## View Certifications

View Certifications. Allows viewing other members' and units' certifications, the expiring-certifications list and role eligibility. Members always see their own; group administrators need this permission too to see their members' records. With no saved rule only department administrators may. Protected fields need a Protected Data Grant.

Default: No permission row: department admins only; no group lock. Members always see their own records. Source: PermissionTypes.ViewCertifications.

<a id="permission-managecertificationsetup"></a>
## Manage Certification Setup

Manage Certification Setup. Allows managing the certification catalog, templates, role requirements and enforcement settings. It does not by itself show members' certification records; that needs View Certifications. With no saved rule only department administrators may.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ManageCertificationSetup.

<a id="permission-managebids"></a>
## Manage Bids

Manage Bids. Allows creating, editing and deleting bids and quotes and converting them into deployments. Converting and using the deployment wizard also need Manage Deployments, and bid cost cards need View Internal Costs. With no saved rule only department administrators may. Needs Contractor Billing and an active Business Ops add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ManageBids.

<a id="permission-managecontracts"></a>
## Manage Contracts

Manage Contracts. Allows viewing and managing service contracts and compliance documents, including the compliance checklist on a deployment's billing tab. With no saved rule only department administrators may. Needs Contractor Billing and an active Business Ops add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ManageContracts.

<a id="permission-managedeployments"></a>
## Manage Deployments

Manage Deployments. Allows creating and running deployments and external deployment orders: rosters, equipment, time, expenses and closeout, and marking expenses pre-approved. There is no separate view permission; members on a deployment's roster (directly or through their unit's crew on the roster) still see it — taking a seat on a deployed unit does not. With no saved rule only department administrators may. Creating from an external order also needs Create Record with Records activated.

Default: No permission row: department admins only; no group lock. Members on a deployment's roster (directly or through their unit's crew on the roster) still see it and file their own time and expenses; a live unit seat grants nothing. Source: PermissionTypes.ManageDeployments.

<a id="permission-approvetimereports"></a>
## Approve Time Reports

Approve Time Reports. Allows approving and voiding submitted deployment time reports, including from the Incident Command app. Nobody can approve a report they submitted or that carries their own hours, so those need a second approver; voiding is still allowed. Once a report is approved, its expenses — and expenses for that day — can no longer be changed. On the web the approver also needs to see the deployment. With no saved rule only department administrators may.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ApproveTimeReports.

<a id="permission-managemutualaidreimbursement"></a>
## Manage Mutual Aid Reimbursement

Manage Mutual Aid Reimbursement. Allows preparing, handing off and reconciling Cal OES MARS reimbursement work. Building the salary survey also needs View Compensation, and classifications with fewer than three members are left out. Rostered members can still fill in their own F-42 and expenses, but printed packets show them no reimbursement rates or totals. With no saved rule only department administrators may. Needs Cal OES MARS turned on and an active Business Ops add-on.

Default: No permission row: department admins only; no group lock. Rostered members still reach their own F-42 and expense drafts. Source: PermissionTypes.ManageMutualAidReimbursement.

<a id="permission-viewinternalcosts"></a>
## View Internal Costs

View Internal Costs. Allows seeing the department's internal costs: resource cost profiles, usage, field cost runs and margin cards. Running, freezing or deleting cost runs, editing cost profiles and changing other people's usage also need Manage Labor and Compensation. With no saved rule only department administrators may. Needs Internal Costing turned on and an active Business Ops add-on.

Default: No permission row: department admins only; no group lock. Rostered members still file their own resource usage. Source: PermissionTypes.ViewInternalCosts.

<a id="permission-manageworkforcecompensation"></a>
## Manage Labor and Compensation

Manage Labor and Compensation. Allows managing the employer profile, establishments, workers, employment and compensation records, and running cost runs. It includes viewing. Nobody can approve a compensation profile they last changed or their own pay. Pay amounts are protected and show redacted without a Protected Data Grant. Available on the web only. With no saved rule only department administrators may. Needs Internal Costing and an active Business Ops add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ManageWorkforceCompensation.

<a id="permission-viewworkforcecompensation"></a>
## View Compensation

View Compensation. Allows viewing the worker roster and compensation records without changing them, and is needed to build the Cal OES MARS salary survey. Amounts show redacted without a Protected Data Grant. Available on the web only. With no saved rule only department administrators may.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ViewWorkforceCompensation.

<a id="permission-managepaydatareporting"></a>
## Manage Pay Data Reporting

Manage Pay Data Reporting. Allows building California pay data reports in the wizard and seeing the per-worker snapshots behind them. Freezing and exporting a report needs Export Pay Data Reporting. Every member still answers their own demographic questions. Needs the pay data reporting module, an active Business Ops add-on and Advanced Data Protection enabled. With no saved rule only department administrators may.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ManagePayDataReporting.

<a id="permission-exportpaydatareporting"></a>
## Export Pay Data Reporting

Export Pay Data Reporting. Allows freezing, exporting and downloading California pay data reports, and reading report runs and their totals. Per-worker snapshots and building the report need Manage Pay Data Reporting. Needs an active Business Ops add-on and Advanced Data Protection enabled. With no saved rule only department administrators may.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ExportPayDataReporting.

<a id="permission-viewincidentbackoffice"></a>
## View Incident Back Office

View Incident Back Office. Allows opening the Incident Back Office desks and reading activations, periods, organization and logistics. Changes on each desk need that desk's own permission. With no saved rule every member may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: every member; no group lock. Source: PermissionTypes.ViewIncidentBackOffice.

<a id="permission-manageincidentactivation"></a>
## Manage Incident Activation

Manage Incident Activation. Allows opening activations, adding them to a complex, and closing, sealing or reopening them. With no saved rule only department administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ManageIncidentActivation.

<a id="permission-manageincidentperiods"></a>
## Manage Operational Periods

Manage Operational Periods. Allows creating and editing operational periods and approving their plan versions. With no saved rule department and group administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department and group admins; no group lock. Source: PermissionTypes.ManageIncidentPeriods.

<a id="permission-manageincidentpositions"></a>
## Manage Incident Positions

Manage Incident Positions. Allows editing the position catalog, position assignments and team rosters. With no saved rule department and group administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department and group admins; no group lock. Source: PermissionTypes.ManageIncidentPositions.

<a id="permission-manageincidentparticipants"></a>
## Manage Incident Participants

Manage Incident Participants. Allows enrolling and editing participants and sending their self-service links. The group-only box limits it to participants from the person's own group. With no saved rule department and group administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department and group admins; no group lock. Source: PermissionTypes.ManageIncidentParticipants.

<a id="permission-viewparticipantsensitive"></a>
## View Participant Sensitive Details

View Participant Sensitive Details. Allows seeing participants' emergency contacts and their medical, accessibility, allergen and dietary details. With no saved rule only department administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ViewParticipantSensitive.

<a id="permission-createincidentrequest"></a>
## Create Resource Requests

Create Resource Requests. Allows creating resource requests and mission packages. The group-only box limits it to requests for the person's own group. With no saved rule department and group administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department and group admins; no group lock. Source: PermissionTypes.CreateIncidentRequest.

<a id="permission-approveincidentrequest"></a>
## Approve Resource Requests

Approve Resource Requests. Allows approving or rejecting resource requests. The group-only box limits it to requests from the person's own group. With no saved rule only department administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ApproveIncidentRequest.

<a id="permission-orderincidentrequest"></a>
## Order Resource Requests

Order Resource Requests. Allows placing the order that fills an approved resource request, including calls against vendor agreements. With no saved rule only department administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.OrderIncidentRequest.

<a id="permission-receiveincidentrequest"></a>
## Receive Resource Requests

Receive Resource Requests. Allows recording arrivals and receipts against filled requests. The group-only box limits it to the person's own group. With no saved rule department and group administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department and group admins; no group lock. Source: PermissionTypes.ReceiveIncidentRequest.

<a id="permission-manageincidentfacilities"></a>
## Manage Incident Facilities

Manage Incident Facilities. Allows managing incident facilities, their capacity, agreements and services. With no saved rule department and group administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department and group admins; no group lock. Source: PermissionTypes.ManageIncidentFacilities.

<a id="permission-manageincidentlodging"></a>
## Manage Incident Lodging

Manage Incident Lodging. Allows managing lodging properties, room blocks, nightly inventory and bed assignments. With no saved rule only department administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ManageIncidentLodging.

<a id="permission-viewlodgingassignmentdetail"></a>
## View Lodging Assignment Details

View Lodging Assignment Details. Allows seeing room numbers and lodging restrictions on bed assignments and rooming lists. With no saved rule only department administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ViewLodgingAssignmentDetail.

<a id="permission-manageincidentfeeding"></a>
## Manage Incident Feeding

Manage Incident Feeding. Allows managing meal plans, demand, meal orders and meals served. With no saved rule department and group administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department and group admins; no group lock. Source: PermissionTypes.ManageIncidentFeeding.

<a id="permission-manageincidentservices"></a>
## Manage Support Services

Manage Support Services. Allows requesting and recording support services such as laundry or showers. The group-only box limits it to the person's own group. With no saved rule every member may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: every member; no group lock. Source: PermissionTypes.ManageIncidentServices.

<a id="permission-manageincidentgroundsupport"></a>
## Manage Ground Support

Manage Ground Support. Allows managing transport requests and movements, travel itineraries and fuel. With no saved rule department and group administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department and group admins; no group lock. Source: PermissionTypes.ManageIncidentGroundSupport.

<a id="permission-manageincidentcomms"></a>
## Manage Incident Communications

Manage Incident Communications. Allows managing the communications plan and radio and device assignments. With no saved rule department and group administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department and group admins; no group lock. Source: PermissionTypes.ManageIncidentComms.

<a id="permission-managevendorcatalog"></a>
## Manage Vendor Catalog

Manage Vendor Catalog. Allows adding and editing vendors, their locations, capabilities, availability and compliance documents. With no saved rule department and group administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department and group admins; no group lock. Source: PermissionTypes.ManageVendorCatalog.

<a id="permission-viewvendorprotected"></a>
## View Vendor Protected Details

View Vendor Protected Details. Allows seeing vendors' tax identifiers and remittance details. With no saved rule only department administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ViewVendorProtected.

<a id="permission-engagevendor"></a>
## Engage Vendors

Engage Vendors. Allows engaging vendors on an activation, recording capacity commitments and quotes, and sending vendor links. With no saved rule only department administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.EngageVendor.

<a id="permission-manageincidentprocurement"></a>
## Manage Incident Procurement

Manage Incident Procurement. Allows managing vendor agreements, requisitions, receipts, vendor invoices and payment packages. With no saved rule only department administrators may. Needs Incident Business turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ManageIncidentProcurement.

<a id="permission-commitincidentpurchase"></a>
## Commit Incident Purchases

Commit Incident Purchases. Allows recording a financial commitment for a purchase; this is the warranted-purchaser control. With no saved rule only department administrators may. Needs Incident Business turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.CommitIncidentPurchase.

<a id="permission-acceptvendorinvoice"></a>
## Accept Vendor Invoices

Accept Vendor Invoices. Allows accepting vendor invoices for payment after they are matched to receipts. With no saved rule only department administrators may. Needs Incident Business turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.AcceptVendorInvoice.

<a id="permission-manageincidentbudget"></a>
## Manage Incident Budget

Manage Incident Budget. Allows managing budget lines, spending delegations, funding codes and cost groups. With no saved rule only department administrators may. Needs Incident Business turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ManageIncidentBudget.

<a id="permission-authorizeincidentfunding"></a>
## Authorize Incident Funding

Authorize Incident Funding. Allows approving funding authorizations for the incident budget. With no saved rule only department administrators may. Needs Incident Business turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.AuthorizeIncidentFunding.

<a id="permission-enterincidenttime"></a>
## Enter Incident Time

Enter Incident Time. Allows entering time, equipment use and expenses. The group-only box limits it to people from the person's own group. With no saved rule department and group administrators may. Needs Incident Business turned on and an active Incident Back Office add-on.

Default: No permission row: department and group admins; no group lock. Source: PermissionTypes.EnterIncidentTime.

<a id="permission-certifyincidenttime"></a>
## Certify Incident Time

Certify Incident Time. Allows supervisors to certify entered time before approval. The group-only box limits it to people from the person's own group. With no saved rule department and group administrators may. Needs Incident Business turned on and an active Incident Back Office add-on.

Default: No permission row: department and group admins; no group lock. Source: PermissionTypes.CertifyIncidentTime.

<a id="permission-approveincidenttime"></a>
## Approve Incident Time

Approve Incident Time. Allows approving certified time for costing and payment. The group-only box limits it to people from the person's own group. With no saved rule only department administrators may. Needs Incident Business turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ApproveIncidentTime.

<a id="permission-viewincidentcost"></a>
## View Incident Cost

View Incident Cost. Allows seeing cost transactions, accruals and daily cost projections. With no saved rule only department administrators may. Needs Incident Business turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ViewIncidentCost.

<a id="permission-manageincidentcost"></a>
## Manage Incident Cost

Manage Incident Cost. Allows editing cost records, cost-share agreements and apportionment runs, and exporting cost reports. With no saved rule only department administrators may. Needs Incident Business turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ManageIncidentCost.

<a id="permission-manageincidentclaims"></a>
## Manage Incident Claims

Manage Incident Claims. Allows opening and working claims for injury, damage or loss. With no saved rule only department administrators may. Needs Incident Business turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ManageIncidentClaims.

<a id="permission-viewclaimprotected"></a>
## View Claim Protected Details

View Claim Protected Details. Allows seeing claim financials and case notes. With no saved rule only department administrators may. Needs Incident Business turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ViewClaimProtected.

<a id="permission-assembleincidentiap"></a>
## Assemble Incident Action Plans

Assemble Incident Action Plans. Allows assembling the incident action plan: its pages, page order, distribution list and print job. Approving and finalizing the plan record uses the Records permissions. With no saved rule only department administrators may. Needs the Incident Back Office turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.AssembleIncidentIap.

<a id="permission-viewcoordinationboards"></a>
## View Coordination Boards

View Coordination Boards. Allows reading coordination boards and the situation desk. The group-only box limits it to boards of the person's own group. With no saved rule every member may. Needs Incident Coordination turned on and an active Incident Back Office add-on.

Default: No permission row: every member; no group lock. Source: PermissionTypes.ViewCoordinationBoards.

<a id="permission-managecoordinationboards"></a>
## Manage Coordination Boards

Manage Coordination Boards. Allows creating and editing coordination boards, their items and the situation desk. The group-only box limits it to boards of the person's own group. With no saved rule department and group administrators may. Needs Incident Coordination turned on and an active Incident Back Office add-on.

Default: No permission row: department and group admins; no group lock. Source: PermissionTypes.ManageCoordinationBoards.

<a id="permission-sharecoordinationboards"></a>
## Share Coordination Boards

Share Coordination Boards. Allows sharing a coordination board with partner agencies. With no saved rule only department administrators may. Needs Incident Coordination turned on and an active Incident Back Office add-on.

Default: No permission row: department admins only; no group lock. Source: PermissionTypes.ShareCoordinationBoards.

<a id="table-department-name"></a>
## Department / Name

Department / Name. Department display name. Review member-facing labels and documents that snapshot the name; changing it does not rename historical artifacts.

Default: required. Source: Department.Name.

<a id="table-department-departmenttype"></a>
## Department / Department Type

Department / Department Type. Organization-type label, set on Department Settings. It never limits templates, packs or modules. The operating profile records several sectors; neither establishes clinical scope or response qualifications.

Default: unset. Source: Department.DepartmentType.

<a id="table-department-timezone"></a>
## Department / Time Zone

Department / Time Zone. Department time zone used by scheduling and local-date reports. Review overnight shifts and daylight-saving transitions before changing it.

Default: owning setup default. Source: Department.TimeZone.

<a id="table-department-use24hourtime"></a>
## Department / Use24 Hour Time

Department / Use24 Hour Time. Preferred time display. This does not change stored timestamps or the department time zone.

Default: unset. Source: Department.Use24HourTime.

<a id="table-department-managinguserid"></a>
## Department / Managing User ID

Department / Managing User ID. Managing-member identity. Subscription and high-risk account operations may require this member; change ownership through its explicit owning workflow.

Default: required. Source: Department.ManagingUserId.

<a id="table-department-code"></a>
## Department / Code

Department / Code. Department code used by supported identification and access flows. Review those consumers before changing it.

Default: owning setup default. Source: Department.Code.

<a id="table-department-apikey"></a>
## Department / API Key

Department / API Key. Department API credential. Never place it in setup notes or model input; manage and rotate it through the authorized workflow.

Default: generated. Source: Department.ApiKey.

<a id="table-department-publicapikey"></a>
## Department / Public API Key

Department / Public API Key. Credential used by supported public API consumers. Public in the field name does not make credential disclosure safe.

Default: generated. Source: Department.PublicApiKey.

<a id="table-department-sharedsecret"></a>
## Department / Shared Secret

Department / Shared Secret. Department integration secret. Rotation can affect integrations and needs coordinated verification.

Default: generated. Source: Department.SharedSecret.

<a id="table-department-linkcode"></a>
## Department / Link Code

Department / Link Code. Department linking code. Review who can use a link and rotate it using the owning workflow.

Default: unset. Source: Department.LinkCode.

<a id="table-department-showwelcome"></a>
## Department / Show Welcome

Department / Show Welcome. Legacy welcome flag. Restored setup dismissal is per administrator and does not use this flag as evidence of setup completion.

Default: legacy. Source: Department.ShowWelcome.

<a id="table-departmentsecuritypolicy-requiremfa"></a>
## Department Security Policy / Require MFA

Department Security Policy / Require MFA. Department-wide MFA policy, distinct from the administrator-only setting. Review enrollment, supported factors and recovery before enforcement.

Default: false. Source: DepartmentSecurityPolicy.RequireMfa.

<a id="table-departmentsecuritypolicy-requiresso"></a>
## Department Security Policy / Require SSO

Department Security Policy / Require SSO. Requires the supported SSO policy gate. Verify the active identity provider and local recovery arrangements before removing a sign-in path.

Default: false. Source: DepartmentSecurityPolicy.RequireSso.

<a id="table-departmentsecuritypolicy-sessiontimeoutminutes"></a>
## Department Security Policy / Session Timeout Minutes

Department Security Policy / Session Timeout Minutes. Idle timeout in minutes; zero defers to host behavior. Verify policy-managed sessions and each supported client rather than assuming immediate global logout.

Default: 0. Source: DepartmentSecurityPolicy.SessionTimeoutMinutes.

<a id="table-departmentsecuritypolicy-maxconcurrentsessions"></a>
## Department Security Policy / Max Concurrent Sessions

Department Security Policy / Max Concurrent Sessions. Maximum policy-managed concurrent sessions per user; zero means unlimited for this setting. Existing sessions and enforcement rollout must be checked separately.

Default: 0. Source: DepartmentSecurityPolicy.MaxConcurrentSessions.

<a id="table-departmentsecuritypolicy-allowedipranges"></a>
## Department Security Policy / Allowed IP Ranges

Department Security Policy / Allowed IP Ranges. Allowed login network ranges. Empty adds no range restriction. Review legitimate responder networks and recovery access before narrowing it.

Default: empty. Source: DepartmentSecurityPolicy.AllowedIpRanges.

<a id="table-departmentsecuritypolicy-passwordexpirationdays"></a>
## Department Security Policy / Password Expiration Days

Department Security Policy / Password Expiration Days. Password age in days; zero disables this policy's age limit. Verify local-password and SSO behavior separately.

Default: 0. Source: DepartmentSecurityPolicy.PasswordExpirationDays.

<a id="table-departmentsecuritypolicy-minpasswordlength"></a>
## Department Security Policy / Min Password Length

Department Security Policy / Min Password Length. Minimum length offered by the policy editor. Actual password validators and supported credential-change paths remain authoritative.

Default: editor minimum 8. Source: DepartmentSecurityPolicy.MinPasswordLength.

<a id="table-departmentsecuritypolicy-requirepasswordcomplexity"></a>
## Department Security Policy / Require Password Complexity

Department Security Policy / Require Password Complexity. Stored complexity preference. Consumer and editor coverage must be verified before relying on this field; it is not proof that an existing password meets a rule.

Default: false. Source: DepartmentSecurityPolicy.RequirePasswordComplexity.

<a id="table-departmentsecuritypolicy-dataclassificationlevel"></a>
## Department Security Policy / Data Classification Level

Department Security Policy / Data Classification Level. Department classification label. Selecting it does not enroll ADP, establish compliance or automatically classify every record correctly.

Default: 0. Source: DepartmentSecurityPolicy.DataClassificationLevel.

<a id="table-departmentsecuritypolicy-allowpasskeysforloginmfa"></a>
## Department Security Policy / Allow Passkeys For Login MFA

Department Security Policy / Allow Passkeys For Login MFA. Accepts a passkey registered in the requesting app as MFA for sign-in, department entry and step-up. Authenticator codes are always accepted. Changing it advances the MFA policy version; sessions whose only second factor is now disallowed verify again. Managing member only.

Default: true. Source: DepartmentSecurityPolicy.AllowPasskeysForLoginMfa.

<a id="table-departmentsecuritypolicy-allowpasskeysforadp"></a>
## Department Security Policy / Allow Passkeys For ADP

Department Security Policy / Allow Passkeys For ADP. Accepts a passkey for protected-data access, ADP management and protected-workflow approvals. Authenticator codes are always accepted. Changing it ends current protected-data access, so members verify again. Managing member only.

Default: true. Source: DepartmentSecurityPolicy.AllowPasskeysForAdp.

<a id="table-departmentsecuritypolicy-allowfederatedmfaforloginmfa"></a>
## Department Security Policy / Allow Federated MFA For Login MFA

Department Security Policy / Allow Federated MFA For Login MFA. Accepts the identity provider's MFA for sign-in and step-up. Requires a tested MFA mapping on the SSO configuration; without one the provider's claims never count. Managing member only.

Default: false. Source: DepartmentSecurityPolicy.AllowFederatedMfaForLoginMfa.

<a id="table-departmentsecuritypolicy-allowfederatedmfaforadp"></a>
## Department Security Policy / Allow Federated MFA For ADP

Department Security Policy / Allow Federated MFA For ADP. Accepts the identity provider's MFA for protected data. Requires a tested MFA mapping; changing it ends current protected-data access. Managing member only.

Default: false. Source: DepartmentSecurityPolicy.AllowFederatedMfaForAdp.

<a id="table-departmentsecuritypolicy-allowresponderapproval"></a>
## Department Security Policy / Allow Responder Approval

Department Security Policy / Allow Responder Approval. Accepts approval with the member's Responder app passkey wherever the matching passkey setting is on; never for security changes or account sign-in methods. Changing it also ends current protected-data access. Managing member only.

Default: true. Source: DepartmentSecurityPolicy.AllowResponderApproval.

<a id="table-departmentsecuritypolicy-acceptrecentloginmfaforadp"></a>
## Department Security Policy / Accept Recent Login MFA For ADP

Department Security Policy / Accept Recent Login MFA For ADP. Lets MFA completed at sign-in in the same session open protected data within the step-up window, for methods the protected-data settings allow. Changing it ends current protected-data access. Managing member only.

Default: true. Source: DepartmentSecurityPolicy.AcceptRecentLoginMfaForAdp.

<a id="table-departmentsecuritypolicy-acceptrecentunlockmfaforadp"></a>
## Department Security Policy / Accept Recent Unlock MFA For ADP

Department Security Policy / Accept Recent Unlock MFA For ADP. Lets the operator's fresh unlock verification on a shared device open protected data. Changing it ends current protected-data access. Managing member only.

Default: true. Source: DepartmentSecurityPolicy.AcceptRecentUnlockMfaForAdp.

<a id="table-departmentsecuritypolicy-sharedidlelockminutes"></a>
## Department Security Policy / Shared Idle Lock Minutes

Department Security Policy / Shared Idle Lock Minutes. Minutes without operator activity before a shared vehicle tablet or workstation session locks, from 1 to 15. Background updates and incoming alerts are not activity. A stricter value reaches running sessions at their next request. Managing member only.

Default: 5. Source: DepartmentSecurityPolicy.SharedIdleLockMinutes.

<a id="table-departmentsecuritypolicy-sharedshifthours"></a>
## Department Security Policy / Shared Shift Hours

Department Security Policy / Shared Shift Hours. Hours after sign-in when a shared session ends whatever the activity, from 1 to 24. A shorter value ends running sessions sooner; a longer one never extends them. Managing member only.

Default: 12. Source: DepartmentSecurityPolicy.SharedShiftHours.

<a id="table-departmentsecuritypolicy-sharedmoderequiredapps"></a>
## Department Security Policy / Shared Mode Required Apps

Department Security Policy / Shared Mode Required Apps. Unit, IC and Dispatch sessions this department always runs as shared sessions, whatever the installation is set to; sign-ins that do not name their app count too. Needs app versions that support shared mode. Managing member only.

Default: none. Source: DepartmentSecurityPolicy.SharedModeRequiredApps.

<a id="table-departmentssoconfig-ssoprovidertype"></a>
## Department SSO Config / SSO Provider Type

Department SSO Config / SSO Provider Type. Selects the identity protocol and provider configuration. OIDC and SAML have different metadata, certificates and callback requirements.

Default: required. Source: DepartmentSsoConfig.SsoProviderType.

<a id="table-departmentssoconfig-isenabled"></a>
## Department SSO Config / Is Enabled

Department SSO Config / Is Enabled. Makes this provider configuration available to the supported SSO flow. A saved enabled flag is not a successful sign-in test.

Default: false. Source: DepartmentSsoConfig.IsEnabled.

<a id="table-departmentssoconfig-clientid"></a>
## Department SSO Config / Client ID

Department SSO Config / Client ID. Client registration identifier from the identity provider. Keep tenant and audience registration consistent with the owning SSO flow.

Default: unset. Source: DepartmentSsoConfig.ClientId.

<a id="table-departmentssoconfig-encryptedclientsecret"></a>
## Department SSO Config / Encrypted Client Secret

Department SSO Config / Encrypted Client Secret. Encrypted provider client secret. The secret is entered and rotated on the authorized provider editor; Admin Assist only describes its purpose.

Default: unset. Source: DepartmentSsoConfig.EncryptedClientSecret.

<a id="table-departmentssoconfig-authority"></a>
## Department SSO Config / Authority

Department SSO Config / Authority. OIDC issuer or authority. Verify the intended tenant, discovery metadata and callback registration before enabling it.

Default: unset. Source: DepartmentSsoConfig.Authority.

<a id="table-departmentssoconfig-metadataurl"></a>
## Department SSO Config / Metadata URL

Department SSO Config / Metadata URL. SAML provider metadata location. Review the trusted provider and its current signing material through the SSO editor.

Default: unset. Source: DepartmentSsoConfig.MetadataUrl.

<a id="table-departmentssoconfig-entityid"></a>
## Department SSO Config / Entity ID

Department SSO Config / Entity ID. SAML service-provider identifier registered at the identity provider. It must agree with the configured integration.

Default: unset. Source: DepartmentSsoConfig.EntityId.

<a id="table-departmentssoconfig-assertionconsumerserviceurl"></a>
## Department SSO Config / Assertion Consumer Service URL

Department SSO Config / Assertion Consumer Service URL. Stored SAML callback configuration. Use the endpoint supplied by the owning SSO setup and verify the provider registration.

Default: unset. Source: DepartmentSsoConfig.AssertionConsumerServiceUrl.

<a id="table-departmentssoconfig-idpssourl"></a>
## Department SSO Config / IdP SSO URL

Department SSO Config / IdP SSO URL. SAML provider sign-in address that sign-in started from Resgrid sends its request to. Verify it against the provider metadata before relying on it.

Default: unset. Source: DepartmentSsoConfig.IdpSsoUrl.

<a id="table-departmentssoconfig-encryptedidpcertificate"></a>
## Department SSO Config / Encrypted IdP Certificate

Department SSO Config / Encrypted IdP Certificate. Encrypted provider verification certificate material. Review expiry and rotation through SSO; this description is not certificate validation.

Default: unset. Source: DepartmentSsoConfig.EncryptedIdpCertificate.

<a id="table-departmentssoconfig-encryptedsigningcertificate"></a>
## Department SSO Config / Encrypted Signing Certificate

Department SSO Config / Encrypted Signing Certificate. Encrypted signing certificate and private-key material. Do not copy it into notes, logs or assistant messages.

Default: unset. Source: DepartmentSsoConfig.EncryptedSigningCertificate.

<a id="table-departmentssoconfig-attributemappingjson"></a>
## Department SSO Config / Attribute Mapping JSON

Department SSO Config / Attribute Mapping JSON. Maps provider attributes to supported member fields. Validate identity matching and missing attributes with an approved test account.

Default: unset. Source: DepartmentSsoConfig.AttributeMappingJson.

<a id="table-departmentssoconfig-federatedmfamappingjson"></a>
## Department SSO Config / Federated MFA Mapping JSON

Department SSO Config / Federated MFA Mapping JSON. Which provider sign-in results count as MFA for provider step-up. A changed mapping, issuer or client needs a new successful test before provider MFA is accepted again.

Default: unset. Source: DepartmentSsoConfig.FederatedMfaMappingJson.

<a id="table-departmentssoconfig-allowlocallogin"></a>
## Department SSO Config / Allow Local Login

Department SSO Config / Allow Local Login. Allows a local-password path alongside this provider where policy permits. Review the department-wide SSO requirement and recovery plan together.

Default: true. Source: DepartmentSsoConfig.AllowLocalLogin.

<a id="table-departmentssoconfig-autoprovisionusers"></a>
## Department SSO Config / Auto Provision Users

Department SSO Config / Auto Provision Users. Allows the supported sign-in flow to create members. Review identity matching, capacity and default assignments before enabling it.

Default: false. Source: DepartmentSsoConfig.AutoProvisionUsers.

<a id="table-departmentssoconfig-defaultrankid"></a>
## Department SSO Config / Default Rank ID

Department SSO Config / Default Rank ID. Department rank assigned by supported automatic provisioning. It does not grant permissions or prove qualifications.

Default: unset. Source: DepartmentSsoConfig.DefaultRankId.

<a id="table-departmentssoconfig-scimenabled"></a>
## Department SSO Config / SCIM Enabled

Department SSO Config / SCIM Enabled. Enables supported SCIM provisioning for this configuration. Review identity lifecycle, deactivation and token access separately from interactive sign-in.

Default: false. Source: DepartmentSsoConfig.ScimEnabled.

<a id="table-departmentssoconfig-encryptedscimbearertoken"></a>
## Department SSO Config / Encrypted SCIM Bearer Token

Department SSO Config / Encrypted SCIM Bearer Token. Encrypted credential for inbound SCIM requests. Rotate through the explicit token workflow and update the provider before retiring an old credential.

Default: unset. Source: DepartmentSsoConfig.EncryptedScimBearerToken.

<a id="department-settings"></a>
## Department Settings

Department Settings Department address, time zone and core settings. Sets the time zone and address everything else uses, and keeps administrative access limited and recoverable. An administrator sets the department time zone, hides modules the department will not use, requires two-factor sign-in for administrators and adds a second administrator. Confirm the address and time zone; dates, maps and schedules depend on them.

<a id="document-numbering"></a>
## Document Numbering

Document Numbering How work order, invoice, bid and daily time report numbers are written, and the day their numbering year starts for fiscal-year numbering. Sets the time zone and address everything else uses, and keeps administrative access limited and recoverable. An administrator sets the department time zone, hides modules the department will not use, requires two-factor sign-in for administrators and adds a second administrator. Know your department address and time zone, and choose at least two people who will administer Resgrid.

<a id="terminology"></a>
## Terminology

Terminology Rename modules and built-in record types to the words your department uses, such as Incident instead of Run. Sets the time zone and address everything else uses, and keeps administrative access limited and recoverable. An administrator sets the department time zone, hides modules the department will not use, requires two-factor sign-in for administrators and adds a second administrator. Know your department address and time zone, and choose at least two people who will administer Resgrid.

<a id="module-settings"></a>
## Module settings

Module settings Turn optional modules on or off for the department. Reduce navigation clutter while understanding retained data and add-on prerequisites. An administrator sets the department time zone, hides modules the department will not use, requires two-factor sign-in for administrators and adds a second administrator. Hide modules you will not use so members see a simpler menu.

<a id="permissions"></a>
## Permissions

Permissions Who can create calls, see personal information and perform other sensitive actions. Review who can perform sensitive work and which resources they may see. An administrator sets the department time zone, hides modules the department will not use, requires two-factor sign-in for administrators and adds a second administrator. Review each permission; some default to every member.

<a id="two-factor"></a>
## Two-Factor Authentication

Two-Factor Authentication Two-factor sign-in for member accounts. Sets the time zone and address everything else uses, and keeps administrative access limited and recoverable. An administrator sets the department time zone, hides modules the department will not use, requires two-factor sign-in for administrators and adds a second administrator. Enroll every administrator in two-factor sign-in.

<a id="security-policy"></a>
## Security and session policy

Security and session policy Two-factor requirements, password rules and session limits. Review lockout and recovery risks with an enrolled administrator. An administrator sets the department time zone, hides modules the department will not use, requires two-factor sign-in for administrators and adds a second administrator. Require two-factor sign-in for administrators and review password and session rules.

<a id="sso"></a>
## Single sign-on and provisioning

Single sign-on and provisioning Let members sign in through your identity provider, with optional automatic account provisioning. Review identity mapping, local fallback and recovery before enforcing SSO. An administrator sets the department time zone, hides modules the department will not use, requires two-factor sign-in for administrators and adds a second administrator. Know your department address and time zone, and choose at least two people who will administer Resgrid.

<a id="audit-history"></a>
## Audit history

Audit history See who changed department settings and when. Identify what changed and who should verify the effect. An administrator sets the department time zone, hides modules the department will not use, requires two-factor sign-in for administrators and adds a second administrator. Know your department address and time zone, and choose at least two people who will administer Resgrid.

<a id="api-keys"></a>
## API keys

API keys Let another system call the Resgrid API for the department with its own key, scopes, expiry and allowed addresses instead of a member's login. Integrations keep working when people leave, can only do what their key allows and can be revoked at once. An administrator sets the department time zone, hides modules the department will not use, requires two-factor sign-in for administrators and adds a second administrator. Know your department address and time zone, and choose at least two people who will administer Resgrid.

<a id="account-sessions"></a>
## Account sessions and recovery

Account sessions and recovery Your own sign-in sessions, password and two-factor settings. Keep personal account recovery distinct from department-wide settings. An administrator sets the department time zone, hides modules the department will not use, requires two-factor sign-in for administrators and adds a second administrator. Know your department address and time zone, and choose at least two people who will administer Resgrid.

<a id="setup-help"></a>
## Setup help and support

Setup help and support Setup Wizard, Setup Report and Admin Assist. Keep learning, configuration and verification as separate steps. An administrator sets the department time zone, hides modules the department will not use, requires two-factor sign-in for administrators and adds a second administrator. Know your department address and time zone, and choose at least two people who will administer Resgrid.

<a id="setting-rssfeedkeyforactivecalls"></a>
## Rss Feed Key For Active Calls

Rss Feed Key For Active Calls. Credential for the active-call feed. Treat the feed and its key as sensitive; rotate through its owning screen.

Default: unset. Source: DepartmentSettingTypes.RssFeedKeyForActiveCalls.

<a id="setting-texttocallnumber"></a>
## Text To Call Number

Text To Call Number. Provisioned inbound text number. Provisioning has external effects and must use the normal text settings flow.

Default: unset. Source: DepartmentSettingTypes.TextToCallNumber.

<a id="setting-texttocallimportformat"></a>
## Text To Call Import Format

Text To Call Import Format. Parser used for inbound text call creation. Verify the sender format with a controlled example on the owning screen.

Default: owning parser default. Source: DepartmentSettingTypes.TextToCallImportFormat.

<a id="setting-texttocallsourcenumbers"></a>
## Text To Call Source Numbers

Text To Call Source Numbers. Allowed inbound source numbers for text calls and commands. Empty sources prevent accepted intake; never display raw numbers in Admin Assist.

Default: empty. Source: DepartmentSettingTypes.TextToCallSourceNumbers.

<a id="setting-enabletexttocall"></a>
## Enable Text To Call

Enable Text To Call. Configured text-call preference. Enforcement differs by provider and chatbot path; the stored value alone does not prove intake is stopped or accepted. Review sender classification and perform an explicit controlled verification on the owning screen.

Default: false. Source: DepartmentSettingTypes.EnableTextToCall.

<a id="setting-internaldispatchemail"></a>
## Internal Dispatch Email

Internal Dispatch Email. Assigned internal dispatch email address. Presence can be shown; the address stays on the authorized import screen.

Default: unset. Source: DepartmentSettingTypes.InternalDispatchEmail.

<a id="setting-callssortorder"></a>
## Calls Sort Order

Calls Sort Order. Sort order for displayed calls; does not reorder provider delivery or change call priority.

Default: owning view default. Source: DepartmentSettingTypes.CallsSortOrder.

<a id="setting-dispatchshiftinsteadofgroup"></a>
## Dispatch Shift Instead Of Group

Dispatch Shift Instead Of Group. Expand dispatched groups using the resolved on-duty roster. An empty shift result falls back to group members; preview both cases.

Default: false. Source: DepartmentSettingTypes.DispatchShiftInsteadOfGroup.

<a id="setting-autosetstatusforshiftdispatchpersonnel"></a>
## Auto Set Status For Shift Dispatch Personnel

Auto Set Status For Shift Dispatch Personnel. Apply the configured status to shift-dispatched personnel. This only takes effect with shift dispatch enabled.

Default: false. Source: DepartmentSettingTypes.AutoSetStatusForShiftDispatchPersonnel.

<a id="setting-shiftcalldispatchpersonnelstatustoset"></a>
## Shift Call Dispatch Personnel Status To Set

Shift Call Dispatch Personnel Status To Set. Personnel status applied when shift dispatch occurs. Minus one leaves status unchanged; use an existing approved status.

Default: -1. Source: DepartmentSettingTypes.ShiftCallDispatchPersonnelStatusToSet.

<a id="setting-shiftcallreleasepersonnelstatustoset"></a>
## Shift Call Release Personnel Status To Set

Shift Call Release Personnel Status To Set. Personnel status applied on call release for the shift-dispatch path. Minus one leaves status unchanged.

Default: -1. Source: DepartmentSettingTypes.ShiftCallReleasePersonnelStatusToSet.

<a id="setting-unitdispatchalsodispatchtoassignedpersonnel"></a>
## Unit Dispatch Also Dispatch To Assigned Personnel

Unit Dispatch Also Dispatch To Assigned Personnel. Expand unit dispatch to its assigned crew. Review duplicate routes, current assignments and recipient permissions.

Default: false. Source: DepartmentSettingTypes.UnitDispatchAlsoDispatchToAssignedPersonnel.

<a id="setting-unitdispatchalsodispatchtogroup"></a>
## Unit Dispatch Also Dispatch To Group

Unit Dispatch Also Dispatch To Group. Expand unit dispatch to the station group. This can substantially increase the recipient audience.

Default: false. Source: DepartmentSettingTypes.UnitDispatchAlsoDispatchToGroup.

<a id="setting-checkintimersautoenablefornewcalls"></a>
## Check In Timers Auto Enable For New Calls

Check In Timers Auto Enable For New Calls. Automatically start configured check-in timers for new calls. Review timer targets and escalation before enabling it.

Default: false. Source: DepartmentSettingTypes.CheckInTimersAutoEnableForNewCalls.

<a id="setting-unitcalldispatchstatustoset"></a>
## Unit Call Dispatch Status To Set

Unit Call Dispatch Status To Set. Unit status applied during call dispatch, by meaning: each unit gets the first active status of its own status set (its unit type's custom set, or the built-in statuses) with that base type. Values: -1 default (Dispatched, Responding or En Route on a custom set, built-in Responding otherwise); -2 leave the status unchanged; 1000 + an ActionBaseTypes value for a status meaning; 0 to 12 a built-in unit status saved before status meanings, which a unit on a custom set reads by its base type. A unit whose set has no matching status keeps its status, and Dispatch Settings lists those unit types. Unit-type overrides take precedence.

Default: -1. Source: DepartmentSettingTypes.UnitCallDispatchStatusToSet.

<a id="setting-unitcallreleasestatustoset"></a>
## Unit Call Release Status To Set

Unit Call Release Status To Set. Unit status applied at call release (the call is closed, or the unit is taken off it) to the units still working the call, by meaning in the same way as the dispatch status (same values; the default is the set's Cleared, Available or In Quarters status, built-in Released otherwise). A unit already back in service, out of service, unavailable or on a break, or working another open call keeps its status; custom statuses are read by their base type. Review unit-type overrides before assuming one department-wide effect.

Default: -1. Source: DepartmentSettingTypes.UnitCallReleaseStatusToSet.

<a id="setting-unitcallstatusoverridesbyunittype"></a>
## Unit Call Status Overrides By Unit Type

Unit Call Status Overrides By Unit Type. Per-unit-type call status overrides. Existing type-specific values take precedence over department defaults.

Default: empty overrides. Source: DepartmentSettingTypes.UnitCallStatusOverridesByUnitType.

<a id="setting-dispatchrecommendationmode"></a>
## Dispatch Recommendation Mode

Dispatch Recommendation Mode. Dispatch recommendation selection mode. Review run cards, eligible resources and human dispatch responsibility.

Default: 0. Source: DepartmentSettingTypes.DispatchRecommendationMode.

<a id="setting-dispatchrecommendationautodispatch"></a>
## Dispatch Recommendation Auto Dispatch

Dispatch Recommendation Auto Dispatch. Allow the recommendation path to dispatch automatically where enabled. Preview recipients and verify local procedures first.

Default: false. Source: DepartmentSettingTypes.DispatchRecommendationAutoDispatch.

<a id="setting-dispatchrecommendationconfig"></a>
## Dispatch Recommendation Config

Dispatch Recommendation Config. Location, ETA, rest, crew and move-up settings used by recommendation consumers. External ETA calls occur only in the owning operational flow.

Default: DispatchRecommendationConfig constructor. Source: DepartmentSettingTypes.DispatchRecommendationConfig.

<a id="setting-dispatchresponsecodes"></a>
## Dispatch Response Codes

Dispatch Response Codes. Optional per-unit response codes (for example C3 for lights and siren) that conditional run card rows propose, and whether crews see them on unit pushes and in the field apps. Empty means codes appear nowhere.

Default: DispatchResponseCodesConfig constructor. Source: DepartmentSettingTypes.DispatchResponseCodes.

<a id="setting-newcallfieldpolicy"></a>
## New Call Field Policy

New Call Field Policy. Controls which built-in fields and sections the call forms show and which must be filled in, on the web, in Omni and through the API. Hidden fields cannot be required; name, nature, priority and type are always shown and required. Hiding all four dispatch lists replaces them with an optional All Call, so a call left without it pages nobody.

Default: visible, not required. Source: DepartmentSettingTypes.NewCallFieldPolicy.

<a id="setting-groupdispatchscopeconfig"></a>
## Group Dispatch Scope Config

Group Dispatch Scope Config. Group-subtree dispatch visibility with explicit role exceptions. Preview each actor and target scope before changing it.

Default: disabled, no department-wide role exceptions. Source: DepartmentSettingTypes.GroupDispatchScopeConfig.

<a id="setting-callnumberingconfig"></a>
## Call Numbering Config

Call Numbering Config. How new call numbers are written: the call number pattern, sequence digits and numbering year start (for calls numbered by a fiscal year), edited on Call Settings, plus a raised next number for departments continuing numbers issued before Resgrid. Each call takes its number from its sequence once, so two calls logged together never share a number. Changing the pattern never renumbers calls that already have a number.

Default: legacy pattern {YY}-{SEQ}, unpadded. Source: DepartmentSettingTypes.CallNumberingConfig.

<a id="setting-emailcallupdates"></a>
## Updates from your CAD

Updates from your CAD. Update the existing call when your CAD sends a corrected or follow-up email for the same call ID. The call ID comes from the email format, from the text after a label, or from a pattern. Each field (address and map location, priority, call type, name, nature, notes) is updated or only noted; a value changed in Resgrid is kept by default; everyone is paged again when the address or priority changes by default; units an update adds are paged. An email for a call closed within the window (60 minutes by default) is noted on it, and configured close or cancel phrases close or cancel the matched call through the normal close rules. Active911, R&R, Ottawa/Kingston/Toronto and Parkland County 2 keep their built-in behaviour until the page is saved.

Default: off (built-in behaviour for the four formats above). Source: DepartmentSettingTypes.EmailCallUpdates.

<a id="setting-emaillabeledfields"></a>
## Labels for the Labeled fields format

Labels for the Labeled fields format. Used when your email format is "Labeled fields". Enter the label your CAD writes in front of each value. Separate alternatives with |. A value runs to the end of its line or to the next known label on the same line. Call types and priorities are matched by name, units by unit name.

Default: no labels. Source: DepartmentSettingTypes.EmailLabeledFields.

<a id="field-callnumberingconfig-pattern"></a>
## Call Numbering Config / Pattern

Call Numbering Config / Pattern. Fixed text (letters, digits, - _ . / :) plus the {YYYY} or {YY}, {MM}, {DD}, {HH} (hour, 00-23), {MIN} (minute) and {SEQ} tokens, for example FD-{YYYY}-{SEQ} or {DD}-{MM}-{YYYY}-{HH}:{MIN}-{SEQ}. {SEQ} appears exactly once, and each other token at most once. The sequence starts again at 1 whenever the date the pattern writes changes: yearly with a year, monthly with {MM}, daily with {DD}, never without a date; the hour and minute are written into each number but never restart it. {MM} needs a year, {DD} needs {MM}, {HH} needs {DD} and {MIN} needs {HH}. Dates and times are the department's local time of the call. Document numbers (setting 117) and Records numbers do not take {HH}, {MIN} or ':'. A new pattern starts its own sequence, which the department can raise to continue earlier numbers. Until a pattern is saved, calls keep the {YY}-{SEQ} numbers Resgrid has always issued.

Default: unset. Source: CallNumberingConfig.Pattern.

<a id="field-callnumberingconfig-sequencewidth"></a>
## Call Numbering Config / Sequence Width

Call Numbering Config / Sequence Width. Digits the sequence is padded to with leading zeros, from 1 (no padding, as the legacy numbers are) to 8. The sequence is counted as a number, so changing the width never restarts it.

Default: 1. Source: CallNumberingConfig.SequenceWidth.

<a id="field-callnumberingconfig-yearstartmonth"></a>
## Call Numbering Config / Year Start Month

Call Numbering Config / Year Start Month. Month the numbering year starts (1 to 12), for a department numbering calls by a fiscal year. A yearly pattern starts again at 1 on that month and day, in the department's local time, and {YYYY} and {YY} write the fiscal year's name. Patterns with {MM} or {DD} always write and restart on the calendar date. Unset (0) is January, the calendar year. Saving a new year start does not renumber calls; the sequence for the new year carries on from any number with the same text already issued.

Default: 0 (January). Source: CallNumberingConfig.YearStartMonth.

<a id="field-callnumberingconfig-yearstartday"></a>
## Call Numbering Config / Year Start Day

Call Numbering Config / Year Start Day. Day of the month the numbering year starts, from 1 to that month's last day; February 29 is refused so the year starts on the same date every year. Unset (0) is the 1st.

Default: 0 (the 1st). Source: CallNumberingConfig.YearStartDay.

<a id="field-callnumberingconfig-yearlabel"></a>
## Call Numbering Config / Year Label

Call Numbering Config / Year Label. Which year {YYYY} and {YY} write for a numbering year that does not start on January 1: 0, the year it ends in (the usual fiscal-year convention, so a year starting November 1, 2026 is 2027), or 1, the year it starts in. Ignored for a January 1 start.

Default: 0 (the year it ends in). Source: CallNumberingConfig.YearLabel.

<a id="field-unittypecallstatusoverridesetting-overrides"></a>
## Unit Type Call Status Override Setting / Overrides

Unit Type Call Status Override Setting / Overrides. Per-unit-type dispatch and release status overrides. The owning consumer resolves these before department defaults.

Default: empty. Source: UnitTypeCallStatusOverrideSetting.Overrides.

<a id="field-unittypecallstatusoverride-unittypeid"></a>
## Unit Type Call Status Override / Unit Type ID

Unit Type Call Status Override / Unit Type ID. Existing unit type receiving this override. A unit type name is not a qualification or staffing clearance.

Default: unset. Source: UnitTypeCallStatusOverride.UnitTypeId.

<a id="field-unittypecallstatusoverride-dispatchstatus"></a>
## Unit Type Call Status Override / Dispatch Status

Unit Type Call Status Override / Dispatch Status. Option of this unit type's own status set applied on dispatch, ahead of the department's dispatch status; minus one uses the department's dispatch status.

Default: -1. Source: UnitTypeCallStatusOverride.DispatchStatus.

<a id="field-unittypecallstatusoverride-releasestatus"></a>
## Unit Type Call Status Override / Release Status

Unit Type Call Status Override / Release Status. Option of this unit type's own status set applied on release, ahead of the department's release status; minus one uses the department's release status.

Default: -1. Source: UnitTypeCallStatusOverride.ReleaseStatus.

<a id="field-newcallfieldpolicy-rules"></a>
## New Call Field Policy / Rules

New Call Field Policy / Rules. One rule per configured field: shown or hidden, optional or required. A field with no rule is shown and optional, and a stored rule hiding Dispatch Recipients (from before the lists had their own keys) hides all four lists.

Default: empty. Source: NewCallFieldPolicy.Rules.

<a id="field-newcallfieldrule-key"></a>
## New Call Field Rule / Key

New Call Field Rule / Key. The field the rule is for: address, geolocation, what3words, pluscode, destinationPoi, indoorLocation, mapPage, note, contactName, contactInfo, primaryContact, additionalContacts, externalId, incidentId, referenceId, protocols, linkedCall, dispatchPersonnel, dispatchGroups, dispatchRoles, dispatchUnits, dispatchList, checkInTimers, part2Consent, dispatchOn, pendingCall. Name, nature, priority and type have no key: they are always shown and required.

Default: unset. Source: NewCallFieldRule.Key.

<a id="field-newcallfieldrule-visible"></a>
## New Call Field Rule / Visible

New Call Field Rule / Visible. Whether the field or section is on the call forms. A hidden field keeps its stored value when a call is edited. Dispatch Recipients has no setting of its own: it shows while any of the four dispatch lists does, and with all four hidden the forms offer an optional All Call instead.

Default: true. Source: NewCallFieldRule.Visible.

<a id="field-newcallfieldrule-required"></a>
## New Call Field Rule / Required

New Call Field Rule / Required. Whether a shown field must be filled in before a call is created or an edit is saved. Plus Code, Additional Contacts, Check-In Timers, Part 2 Consent, Save as Pending and the four dispatch lists can only be hidden; requiring Dispatch Recipients means at least one recipient from any list. Review imports and each client before making a field mandatory.

Default: false. Source: NewCallFieldRule.Required.

<a id="field-groupdispatchscopeconfig-enabled"></a>
## Group Dispatch Scope Config / Enabled

Group Dispatch Scope Config / Enabled. Limit supported dispatch views to the member's group subtree. Department administrators and configured department-wide roles keep department-wide access.

Default: false. Source: GroupDispatchScopeConfig.Enabled.

<a id="field-groupdispatchscopeconfig-departmentwideroleids"></a>
## Group Dispatch Scope Config / Department Wide Role IDs

Group Dispatch Scope Config / Department Wide Role IDs. Existing roles allowed department-wide dispatch views while group scoping is enabled. This changes view scope, not licensing or operational qualification.

Default: empty. Source: GroupDispatchScopeConfig.DepartmentWideRoleIds.

<a id="field-dispatchrecommendationconfig-maxlocationageseconds"></a>
## Dispatch Recommendation Config / Max Location Age Seconds

Dispatch Recommendation Config / Max Location Age Seconds. Exclude older unit positions from closest-unit candidates; zero removes the age limit. IncludeStaleLocations changes this behavior. A unit measured from its station (In Quarters status, or no GPS fix) never ages out.

Default: 1800. Source: DispatchRecommendationConfig.MaxLocationAgeSeconds.

<a id="field-dispatchrecommendationconfig-maxradiusmeters"></a>
## Dispatch Recommendation Config / Max Radius Meters

Dispatch Recommendation Config / Max Radius Meters. Maximum candidate distance in meters; zero removes the radius cap. This is not a response-time or route-safety guarantee.

Default: 0. Source: DispatchRecommendationConfig.MaxRadiusMeters.

<a id="field-dispatchrecommendationconfig-includestalelocations"></a>
## Dispatch Recommendation Config / Include Stale Locations

Dispatch Recommendation Config / Include Stale Locations. Permit positions beyond the configured age limit in closest-unit selection, marked stale. Review the risk of outdated positions.

Default: false. Source: DispatchRecommendationConfig.IncludeStaleLocations.

<a id="field-dispatchrecommendationconfig-personnelmaxlocationageseconds"></a>
## Dispatch Recommendation Config / Personnel Max Location Age Seconds

Dispatch Recommendation Config / Personnel Max Location Age Seconds. Maximum age of personnel positions for closest-unit candidate selection; zero removes the age limit.

Default: 1800. Source: DispatchRecommendationConfig.PersonnelMaxLocationAgeSeconds.

<a id="field-dispatchrecommendationconfig-useroutedeta"></a>
## Dispatch Recommendation Config / Use Routed ETA

Dispatch Recommendation Config / Use Routed ETA. Re-rank shortlisted candidates using provider travel estimates. The operational path can make external provider calls; Admin Assist does not. The configured turnout times are added to each drive time.

Default: false. Source: DispatchRecommendationConfig.UseRoutedEta.

<a id="field-dispatchrecommendationconfig-closestunitranking"></a>
## Dispatch Recommendation Config / Closest Unit Ranking

Dispatch Recommendation Config / Closest Unit Ranking. How closest-unit picks and the nearest-unit board order candidates: fastest response (turnout plus travel, with routed drive times when enabled; the default) or shortest straight-line distance. Ranking by distance applies to closest-unit run cards only (station-based cards keep their order) and still looks up drive times when routed ETAs are on: run card picks and the nearest-unit board show them, and conditional run card time conditions measure the picks on them, but they never change the order.

Default: FastestResponse. Source: DispatchRecommendationConfig.ClosestUnitRanking.

<a id="field-dispatchrecommendationconfig-etashortlistsize"></a>
## Dispatch Recommendation Config / ETA Shortlist Size

Dispatch Recommendation Config / ETA Shortlist Size. Number of straight-line candidates per requirement sent for routed ETA when enabled. The owning validator caps provider work.

Default: 5. Source: DispatchRecommendationConfig.EtaShortlistSize.

<a id="field-dispatchrecommendationconfig-inquartersturnoutseconds"></a>
## Dispatch Recommendation Config / In-Quarters Turnout Seconds

Dispatch Recommendation Config / In-Quarters Turnout Seconds. Seconds a unit at its station needs before it is moving; applies to In Quarters statuses and units with no GPS position. Added to travel time when ranking closest units; zero means none.

Default: 0. Source: DispatchRecommendationConfig.InQuartersTurnoutSeconds.

<a id="field-dispatchrecommendationconfig-mobileturnoutseconds"></a>
## Dispatch Recommendation Config / Mobile Turnout Seconds

Dispatch Recommendation Config / Mobile Turnout Seconds. Seconds a unit that is already out and available needs before it is moving; applies to units ranked from GPS. Added to travel time when ranking closest units; zero means none.

Default: 0. Source: DispatchRecommendationConfig.MobileTurnoutSeconds.

<a id="field-dispatchrecommendationconfig-personnelturnoutseconds"></a>
## Dispatch Recommendation Config / Personnel Turnout Seconds

Dispatch Recommendation Config / Personnel Turnout Seconds. Seconds a person needs before they are moving; added to their travel time when run card rows that accept people rank them against units. Zero means none.

Default: 0. Source: DispatchRecommendationConfig.PersonnelTurnoutSeconds.

<a id="field-dispatchrecommendationconfig-waitingcallautodispatch"></a>
## Dispatch Recommendation Config / Waiting Call Auto Dispatch

Dispatch Recommendation Config / Waiting Call Auto Dispatch. When a unit becomes available that matches a call awaiting resources, send it to the highest-priority waiting call automatically instead of only alerting dispatchers. Run cards can override this; off by default.

Default: false. Source: DispatchRecommendationConfig.WaitingCallAutoDispatch.

<a id="field-dispatchrecommendationconfig-waitingcallmaxresponseseconds"></a>
## Dispatch Recommendation Config / Waiting Call Max Response Seconds

Dispatch Recommendation Config / Waiting Call Max Response Seconds. Limit for automatic dispatch to calls awaiting resources: a freed unit whose estimated response is longer than this is alerted, not sent. Zero means no limit; the alert itself always fires.

Default: 0. Source: DispatchRecommendationConfig.WaitingCallMaxResponseSeconds.

<a id="field-dispatchrecommendationconfig-restperiodminutes"></a>
## Dispatch Recommendation Config / Rest Period Minutes

Dispatch Recommendation Config / Rest Period Minutes. Deprioritize recently dispatched resources for this duration; zero disables rotation. This does not infer fatigue or medical fitness.

Default: 0. Source: DispatchRecommendationConfig.RestPeriodMinutes.

<a id="field-dispatchrecommendationconfig-unitminimumstaffinglevel"></a>
## Dispatch Recommendation Config / Unit Minimum Staffing Level

Dispatch Recommendation Config / Unit Minimum Staffing Level. Minimum configured unit staffing level for recommendation eligibility; zero disables the gate. Units without defined seats pass, and run cards may override it.

Default: 0. Source: DispatchRecommendationConfig.UnitMinimumStaffingLevel.

<a id="field-dispatchrecommendationconfig-moveuprecommendationsenabled"></a>
## Dispatch Recommendation Config / Move Up Recommendations Enabled

Dispatch Recommendation Config / Move Up Recommendations Enabled. Run the station coverage move-up pass after selection. Recommendations remain subject to approved local dispatch procedures.

Default: false. Source: DispatchRecommendationConfig.MoveUpRecommendationsEnabled.

<a id="table-departmentcallemail-hostname"></a>
## Department Call Email / Hostname

Department Call Email / Hostname. Mailbox host for email intake. Review the approved provider and network reachability without exposing credentials in setup guidance.

Default: unset. Source: DepartmentCallEmail.Hostname.

<a id="table-departmentcallemail-port"></a>
## Department Call Email / Port

Department Call Email / Port. Mailbox connection port. It must match the selected provider and TLS mode.

Default: editor/provider default. Source: DepartmentCallEmail.Port.

<a id="table-departmentcallemail-usessl"></a>
## Department Call Email / Use SSL

Department Call Email / Use SSL. Mailbox transport encryption option. Verify actual provider compatibility and successful polling; a stored flag alone does not prove transport protection.

Default: editor/provider default. Source: DepartmentCallEmail.UseSsl.

<a id="table-departmentcallemail-username"></a>
## Department Call Email / Username

Department Call Email / Username. Mailbox account identifier used for intake. It remains on the authorized configuration screen.

Default: unset. Source: DepartmentCallEmail.Username.

<a id="table-departmentcallemail-password"></a>
## Department Call Email / Password

Department Call Email / Password. Mailbox credential. Do not copy it into Admin Assist; rotate it through the mailbox and owning editor together.

Default: unset. Source: DepartmentCallEmail.Password.

<a id="table-departmentcallemail-formattype"></a>
## Department Call Email / Format Type

Department Call Email / Format Type. Selects the inbound email parser. Verify representative permitted source messages without sending an operational page unintentionally.

Default: owning parser default. Source: DepartmentCallEmail.FormatType.

<a id="table-departmentcallemail-lastcheck"></a>
## Department Call Email / Last Check

Department Call Email / Last Check. Last recorded polling timestamp. Missing or future timestamps are unknown; quiet call volume is not proof of a poll failure.

Default: unset. Source: DepartmentCallEmail.LastCheck.

<a id="table-departmentcallemail-isfailure"></a>
## Department Call Email / Is Failure

Department Call Email / Is Failure. Latest recorded mailbox failure flag. Verify a new poll before treating it as a continuing outage or a resolved problem.

Default: false. Source: DepartmentCallEmail.IsFailure.

<a id="table-departmentcallemail-errormessage"></a>
## Department Call Email / Error Message

Department Call Email / Error Message. Provider/import error details on the authorized screen. Admin Assist uses a normalized failure indication and does not copy provider bodies.

Default: unset. Source: DepartmentCallEmail.ErrorMessage.

<a id="setting-personnelonunitsetunitstatus"></a>
## Personnel On Unit Set Unit Status

Personnel On Unit Set Unit Status. Allow personnel status updates to influence their assigned unit status. Review automation and crew assignment together.

Default: false. Source: DepartmentSettingTypes.PersonnelOnUnitSetUnitStatus.

<a id="setting-statusholdtoconfirm"></a>
## Hold to Set Status

Hold to Set Status. The Unit and Responder apps set a status with a two-second press and hold instead of a tap followed by Next or Submit, so a stray tap never sets a status and a crew cannot forget to confirm one. A status that needs a note, or a destination the app cannot fill from the active call, still opens that step. Off by default.

Default: false. Source: DepartmentSettingTypes.StatusHoldToConfirm.

<a id="setting-closecallwhenunitsclear"></a>
## Close Call When Last Unit Clears

Close Call When Last Unit Clears. An active call closes on its own when the last unit dispatched to it goes from working the call to back in service or out of service: a status whose base type is Available, In Quarters, Cleared, Completed, On Patrol, Unavailable, Not Responding or Maintenance. Every other dispatched unit must also have reported such a status since its dispatch. Only units count. A call with no unit dispatched, a call under an active incident command, a unit still returning, transporting or at the hospital, a unit that never reported after its dispatch, and a custom status with no base type all leave the call open for the dispatcher. The close is recorded with a note naming the last unit, applies the release statuses (which skip units back in service or out of service) and notifies nobody. Off by default.

Default: false. Source: DepartmentSettingTypes.CloseCallWhenUnitsClear.

<a id="setting-transportdestinationchecks"></a>
## Transport Destination Checks

Transport Destination Checks. A call can name the destination its patient or load goes to (the call's Destination). For a status whose base type is Transporting or At Hospital, the Omni, Unit and Dispatch apps pre-select that destination whether or not this is on, and show when a different destination is chosen. With this on, choosing a different destination needs a reason, entered as the status note, and Resgrid adds a call note naming the unit, the status, both destinations and the reason. A unit that then reports At Hospital at the same destination is not noted twice. A status set from an app that predates this setting is saved without asking; its call note then says no reason was given. Off by default.

Default: false. Source: DepartmentSettingTypes.TransportDestinationChecks.

<a id="setting-dispatchrenotifyconfig"></a>
## Dispatch Re-notification

Dispatch Re-notification. Sends a dispatch again to personnel and units that have not acknowledged it, as a push notification titled Reminder 1/3 and so on. Any status set after the dispatch acknowledges it: a person setting any status, such as Responding or Not Responding, or a unit setting any status, with or without a destination. Statuses Resgrid applies itself (dispatch statuses, scheduled resets, maintenance holds) do not count. Re-notification stops when the recipient acknowledges, after the last attempt, when the call closes, when the person or unit is taken off the call, or when the department turns it off. Text message, email, voice call and chat platform re-notification are built and each follows the member's own notification settings, but for system stability every channel other than push is locked off, as are the interval (5 minutes) and the number of re-notifications (3, so 15 minutes): the settings page shows them disabled, and the server stores and sends push only whatever is posted. Units are re-notified by push only. Off by default.

Default: off (DispatchRenotifyConfig constructor: 5 minutes, 3 re-notifications, push). Source: DepartmentSettingTypes.DispatchRenotifyConfig.

<a id="setting-enabletextcommand"></a>
## Enable Text Command

Enable Text Command. Configured text-command preference. Dispatch-source patterns and verified member command identity are different checks. Provider and chatbot paths do not enforce this switch uniformly; review the active consumer before changing it.

Default: false. Source: DepartmentSettingTypes.EnableTextCommand.

<a id="setting-ttslanguage"></a>
## Tts Language

Tts Language. Text-to-speech language for supported voice notifications. Confirm supported voices and pronunciation with a human-run test.

Default: owning voice default. Source: DepartmentSettingTypes.TtsLanguage.

<a id="call-types-priorities"></a>
## Call types and priorities

Call types and priorities The call types and priorities used to classify calls. Use consistent reporting and run-card matching terms. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Add the call types your dispatchers use and set their priorities. Give each call type a class and each priority a meaning so reports and the apps can group and sort them. A call type can also have a custom icon, shown beside its calls in lists, the apps and on the map. A priority's meaning also colors its calls' map pins, and the Map Icon Legend lists each priority under its color.

<a id="custom-statuses"></a>
## Personnel and unit statuses

Personnel and unit statuses The statuses members and units report, such as Responding or On Scene. Use terminology familiar to the department while preserving automation meaning. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Review the built-in statuses and add your own where your terms differ. Give every option a meaning (base type): dispatch, availability, notifications and the apps read the meaning, not the button text. In Quarters tells dispatch recommendations a unit is at its station. The Map Icon Legend lists each status under the map color its meaning gives it; one listed under gray has no meaning yet.

<a id="dispatch-settings"></a>
## Dispatch Settings

Dispatch Settings How calls page personnel: by group or by shift, and related automatic status changes. Consistent call types and statuses make dispatching, response tracking and reporting reliable from the first call. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Choose whether dispatch follows groups or shifts, and review the automatic status options: the unit status on dispatch and release is chosen by meaning from each unit type's own status set. Set closest-unit turnout times if crews need time to leave the station.

<a id="email-intake"></a>
## Email and CAD call intake

Email and CAD call intake Create calls automatically from CAD, paging or email dispatches. Review parsing, routing and observed integration failures before activation. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Choose your CAD or paging format and connect the mailbox that receives dispatches.

<a id="run-cards"></a>
## Run cards and recommendations

Run cards and recommendations Recommended resources for each call type or area. Review candidate coverage before enabling operational use. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Build run cards for your most common calls, then turn on recommendations.

<a id="calls"></a>
## Calls

Calls Active calls and their dispatch status. Closing a call can notify everyone on it, and a call run under an active incident command is closed from the command first. Consistent call types and statuses make dispatching, response tracking and reporting reliable from the first call. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Collect your call types, the statuses members report while responding, and the details of any CAD or paging system that should create calls.

<a id="new-call"></a>
## New Call

New Call Create and dispatch a call. Consistent call types and statuses make dispatching, response tracking and reporting reliable from the first call. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Collect your call types, the statuses members report while responding, and the details of any CAD or paging system that should create calls.

<a id="call-locations"></a>
## Call locations, destinations and routes

Call locations, destinations and routes A call's location can be picked from a point of interest, its destination can be a point of interest or a free address, coordinates or what3words, and it can carry a pre-defined route that the unit working it runs in the app, stop by stop. Consistent call types and statuses make dispatching, response tracking and reporting reliable from the first call. Add the places you pick up from and deliver to as points of interest, and create active route plans with a start and an end for runs you repeat. On New Call, pick the location's point of interest, the destination and a route; a blank call location takes the route start and a blank destination the route end. It works on new, pending, scheduled and recurring calls, on Update Call and in the Omni Dispatch app. Picking a point of interest fills the address and the map pin, which can still be changed. A destination POI and a free destination location are exclusive: choosing one clears the other. A plus code only finds a point and is never stored: on New Call, Update Call and Add Archived Call, Find Plus Code under the location places the call pin and fills a blank address, Find Plus Code under the destination location places the destination, and a save with a code still typed in looks it up first. A short code on its own is read near the department's map centre. The New Call Form Fields rule for Plus Code can hide the location's plus code but never require it; require GPS Coordinates instead. The route picker lists only active route plans whose start and end can be found, and is hidden when there are none; a route never overwrites a location or destination already filled. When a unit makes the call its active call, the Omni Unit app starts the route run, leads the unit to the route start first and checks in at each stop; View Call lists the call's route runs. The New Call Form Fields rule for Destination covers a destination POI and a free destination alike.

<a id="archived-calls"></a>
## Archived Calls

Archived Calls Closed and past calls. Consistent call types and statuses make dispatching, response tracking and reporting reliable from the first call. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Collect your call types, the statuses members report while responding, and the details of any CAD or paging system that should create calls.

<a id="pending-calls"></a>
## Pending Calls

Pending Calls Calls saved for a dispatcher to send later, such as follow-ups another system feeds in; nobody is notified until the call is dispatched. Consistent call types and statuses make dispatching, response tracking and reporting reliable from the first call. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Collect your call types, the statuses members report while responding, and the details of any CAD or paging system that should create calls.

<a id="scheduled-calls"></a>
## Scheduled Calls

Scheduled Calls Calls that go out by themselves at a future time and can be sent early or rescheduled, with a calendar of every scheduled and recurring call. Consistent call types and statuses make dispatching, response tracking and reporting reliable from the first call. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Collect your call types, the statuses members report while responding, and the details of any CAD or paging system that should create calls.

<a id="recurring-calls"></a>
## Recurring calls

Recurring calls Calls that repeat on a schedule, such as transfers every Tuesday and Thursday at 08:00, 13:00 and 19:00. Each one becomes a scheduled call ahead of its time and goes out by itself, or a dispatcher is alerted to send it. Consistent call types and statuses make dispatching, response tracking and reporting reliable from the first call. Open Scheduled Calls and choose Recurring Calls, then New Recurring Call, or press Repeat on a call you already sent. Fill in the call details, facilities and contact, who to dispatch and who to alert, then the schedule, when each call is created and who sends each call. Check the next dispatches and the calendar before you save. Times are the department's local time. Each call is created the chosen time ahead of its dispatch (one hour to seven days, one day by default) and then shows in Scheduled Calls, where it can be changed, sent early or deleted on its own. A call is never created for a time more than 10 minutes past, and saving or resuming never sends one at once. When a call goes out, units and people who are unavailable are left off it; if nobody is left, it is held as a pending call. Needs attention lists held, missed, failed and due calls and alerts the people chosen under Who to alert. Also use the matching run card adds that run card's resources when the call goes out. The pickup facility's point of interest becomes each call's location, and a destination and a route can be set. A recurring call can be paused with a reason and a date to resume, and changing it can replace upcoming calls already created. Anyone who can view calls sees recurring calls and the calendar; anyone who can create calls can send, skip or create a single run; whoever created a recurring call, and department admins, change, pause or delete it.

<a id="call-templates"></a>
## Call templates

Call templates Reusable call text and note templates that speed up call entry. Reduce repeated entry while keeping dispatch decisions explicit. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Collect your call types, the statuses members report while responding, and the details of any CAD or paging system that should create calls.

<a id="checkin-setup"></a>
## Check-in timers

Check-in timers Timers that remind responders on a call to check in, with escalation when they do not. Review lone-worker and response follow-up with approved local procedures. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Collect your call types, the statuses members report while responding, and the details of any CAD or paging system that should create calls.

<a id="text-intake"></a>
## Text call intake and commands

Text call intake and commands Create calls and accept member commands by text message. Make approved inbound sources and their operational effects explicit. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Collect your call types, the statuses members report while responding, and the details of any CAD or paging system that should create calls.

<a id="call-settings"></a>
## Call Settings

Call Settings The mailbox and format used to import calls from CAD or paging email, and the call number format, next number and numbering year start. Consistent call types and statuses make dispatching, response tracking and reporting reliable from the first call. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Collect your call types, the statuses members report while responding, and the details of any CAD or paging system that should create calls.

<a id="setting-unitssortorder"></a>
## Units Sort Order

Units Sort Order. Sort order for unit lists; does not change unit eligibility or dispatch recommendation ranking.

Default: owning view default. Source: DepartmentSettingTypes.UnitsSortOrder.

<a id="units"></a>
## Units

Units Your apparatus, vehicles and response teams. Units let dispatchers send the right resources and let everyone see which apparatus is available. Engine 1 and Rescue 1 are added with their unit types and assigned to Station 1. Add each unit with its type and home station group.

<a id="unit-types"></a>
## Unit types

Unit types Categories such as Engine, Ladder or Ambulance. Group comparable resources without assuming staffing or qualification. Engine 1 and Rescue 1 are added with their unit types and assigned to Station 1. Create unit types before adding units so each unit can be classified. Give each type a class, such as Engine or Ambulance, and choose its status set. Upload a custom icon for a type (PNG, JPEG, GIF or WebP, up to 1 MB, square works best) so its units are easy to tell apart in lists, the apps and on the map.

<a id="hardware-tracking"></a>
## Hardware location tracking

Hardware location tracking GPS devices that report unit locations. Review device ownership, stale-position behavior and fallback sources. Engine 1 and Rescue 1 are added with their unit types and assigned to Station 1. Register each tracking device and link it to its unit.

<a id="new-unit"></a>
## New Unit

New Unit Add an apparatus, vehicle or team. Units let dispatchers send the right resources and let everyone see which apparatus is available. Engine 1 and Rescue 1 are added with their unit types and assigned to Station 1. List your apparatus or response teams with their types and home stations. Teams that respond only as individuals can mark this module not applicable.

<a id="unit-staffing"></a>
## Unit Staffing

Unit Staffing Assign crew members to units. Units let dispatchers send the right resources and let everyone see which apparatus is available. Engine 1 and Rescue 1 are added with their unit types and assigned to Station 1. List your apparatus or response teams with their types and home stations. Teams that respond only as individuals can mark this module not applicable.

<a id="setting-enablemodernnotifications"></a>
## Enable Modern Notifications

Enable Modern Notifications. Retired. Modern notification sounds are now on for every department and user; there is nothing to set.

Default: unused. Source: DepartmentSettingTypes.EnableModernNotifications.

<a id="module-messagingdisabled"></a>
## Messaging availability

Messaging availability. Controls availability of messages and announcements. Disabling this module can remove navigation and block module operations; it does not delete its records. Turning it on still requires the applicable subscription, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.MessagingDisabled.

<a id="module-messagingnameoverride"></a>
## Messaging menu name

Messaging menu name. Optional display name for messages and announcements in consumers that support the override. It changes the label, not permissions or functionality. This release has no separate name-override control in Module Settings.

Default: unset. Source: DepartmentModuleSettings.MessagingNameOverride.

<a id="module-chatdisabled"></a>
## Chat availability

Chat availability. Shows or hides Chat in the web app's navigation, dashboard widgets and search. Hiding it does not delete data or change permissions. Chat in the mobile apps is not affected.

Default: false. Source: DepartmentModuleSettings.ChatDisabled.

<a id="table-departmentnotification-eventtype"></a>
## Department Notification / Event Type

Department Notification / Event Type. Event that can trigger this notification rule. Match the event to a real administrative responsibility and expected frequency.

Default: required. Source: DepartmentNotification.EventType.

<a id="table-departmentnotification-userstonotify"></a>
## Department Notification / Users To Notify

Department Notification / Users To Notify. Explicit member recipients. Membership and channel eligibility are evaluated separately at send time.

Default: empty. Source: DepartmentNotification.UsersToNotify.

<a id="table-departmentnotification-rolestonotify"></a>
## Department Notification / Roles To Notify

Department Notification / Roles To Notify. Personnel-role recipients. Role membership can change; a role label is not a qualification or delivery guarantee.

Default: empty. Source: DepartmentNotification.RolesToNotify.

<a id="table-departmentnotification-groupstonotify"></a>
## Department Notification / Groups To Notify

Department Notification / Groups To Notify. Department-group recipients. Review group membership and lock-to-group behavior together.

Default: empty. Source: DepartmentNotification.GroupsToNotify.

<a id="table-departmentnotification-locktogroup"></a>
## Department Notification / Lock To Group

Department Notification / Lock To Group. Restricts applicable event processing to group scope. Review the selected event's actual source group and recipient resolver.

Default: false. Source: DepartmentNotification.LockToGroup.

<a id="table-departmentnotification-selectedgroupsadminsonly"></a>
## Department Notification / Selected Groups Admins Only

Department Notification / Selected Groups Admins Only. Limits selected-group recipients to their administrators in supported event paths. It does not designate department administrators automatically.

Default: false. Source: DepartmentNotification.SelectedGroupsAdminsOnly.

<a id="table-departmentnotification-departmentadmins"></a>
## Department Notification / Department Admins

Department Notification / Department Admins. Includes department administrators in supported notification processing. Review overlap and channel eligibility before estimating volume.

Default: false. Source: DepartmentNotification.DepartmentAdmins.

<a id="table-departmentnotification-everyone"></a>
## Department Notification / Everyone

Department Notification / Everyone. Selects the department-wide recipient option. Review actual active membership, suppression and deduplication before use.

Default: false. Source: DepartmentNotification.Everyone.

<a id="table-departmentnotification-disabled"></a>
## Department Notification / Disabled

Department Notification / Disabled. Stored disabled marker. The inspected legacy notification processor does not consult this flag, so it must not be treated as a verified stop switch. Review the active consumer; queued and delivered notifications cannot be recalled.

Default: false. Source: DepartmentNotification.Disabled.

<a id="table-departmentnotification-beforedata"></a>
## Department Notification / Before Data

Department Notification / Before Data. Previous-state filter for applicable events. Review valid status IDs and the event-specific interpretation of an empty or wildcard value.

Default: unset. Source: DepartmentNotification.BeforeData.

<a id="table-departmentnotification-currentdata"></a>
## Department Notification / Current Data

Department Notification / Current Data. Current-state filter for applicable events. Compare it with the previous-state filter to avoid unintended volume.

Default: unset. Source: DepartmentNotification.CurrentData.

<a id="table-departmentnotification-upperlimit"></a>
## Department Notification / Upper Limit

Department Notification / Upper Limit. Upper threshold for event-specific alerts. Units and comparisons depend on the selected event.

Default: unset. Source: DepartmentNotification.UpperLimit.

<a id="table-departmentnotification-lowerlimit"></a>
## Department Notification / Lower Limit

Department Notification / Lower Limit. Lower threshold for event-specific alerts. Use approved local thresholds and verify the actual event interpretation.

Default: unset. Source: DepartmentNotification.LowerLimit.

<a id="table-departmentnotification-data"></a>
## Department Notification / Data

Department Notification / Data. Event-specific configuration payload. The owning editor validates its meaning; Admin Assist does not execute arbitrary stored expressions.

Default: unset. Source: DepartmentNotification.Data.

<a id="table-chatbotdepartmentconfig-isenabled"></a>
## Chatbot Department Config / Is Enabled

Chatbot Department Config / Is Enabled. Department chatbot availability. Platform feature rollout, account linking and action permissions are separate gates.

Default: false. Source: ChatbotDepartmentConfig.IsEnabled.

<a id="table-chatbotdepartmentconfig-allowedplatforms"></a>
## Chatbot Department Config / Allowed Platforms

Chatbot Department Config / Allowed Platforms. Allowed chatbot platform codes; the stored asterisk represents the platform default. Review each connected provider and its account-linking requirements.

Default: *. Source: ChatbotDepartmentConfig.AllowedPlatforms.

<a id="table-chatbotdepartmentconfig-maxsessionsperuser"></a>
## Chatbot Department Config / Max Sessions Per User

Chatbot Department Config / Max Sessions Per User. Maximum chatbot sessions per member in supported consumers. This is separate from authenticated web/mobile session limits.

Default: 3. Source: ChatbotDepartmentConfig.MaxSessionsPerUser.

<a id="table-chatbotdepartmentconfig-sessionttlminutes"></a>
## Chatbot Department Config / Session TTL Minutes

Chatbot Department Config / Session TTL Minutes. Chatbot session lifetime in minutes. Expiry does not revoke an unrelated Resgrid sign-in session.

Default: 30. Source: ChatbotDepartmentConfig.SessionTtlMinutes.

<a id="table-chatbotdepartmentconfig-allowdispatchviachatbot"></a>
## Chatbot Department Config / Allow Dispatch Via Chatbot

Chatbot Department Config / Allow Dispatch Via Chatbot. Permits supported chatbot dispatch actions subject to their own authorization and confirmation. Enabling it may allow real calls and pages.

Default: false. Source: ChatbotDepartmentConfig.AllowDispatchViaChatbot.

<a id="table-chatbotdepartmentconfig-requireconfirmationforstatuschange"></a>
## Chatbot Department Config / Require Confirmation For Status Change

Chatbot Department Config / Require Confirmation For Status Change. Requests confirmation for supported status changes. Review each platform consumer before assuming all commands are covered.

Default: false. Source: ChatbotDepartmentConfig.RequireConfirmationForStatusChange.

<a id="table-chatbotdepartmentconfig-llmapiendpoint"></a>
## Chatbot Department Config / LLM API Endpoint

Chatbot Department Config / LLM API Endpoint. Optional existing chatbot model endpoint. Phase 0 Admin Assist does not call it and does not use chatbot configuration as an AI entitlement.

Default: unset. Source: ChatbotDepartmentConfig.LlmApiEndpoint.

<a id="table-chatbotdepartmentconfig-llmapikey"></a>
## Chatbot Department Config / LLM API Key

Chatbot Department Config / LLM API Key. Credential for the chatbot model provider. Never copy it into setup metadata or public reference content.

Default: unset. Source: ChatbotDepartmentConfig.LlmApiKey.

<a id="table-chatbotdepartmentconfig-llmmodelname"></a>
## Chatbot Department Config / LLM Model Name

Chatbot Department Config / LLM Model Name. Model identifier used by the existing chatbot integration. This is separate from the later Admin Assist open-source inference deployment.

Default: unset. Source: ChatbotDepartmentConfig.LlmModelName.

<a id="table-chatbotdepartmentconfig-messagesperuserperminute"></a>
## Chatbot Department Config / Messages Per User Per Minute

Chatbot Department Config / Messages Per User Per Minute. Optional per-member chatbot rate bound. Review provider limits and burst behavior; this is not a delivery guarantee.

Default: host default. Source: ChatbotDepartmentConfig.MessagesPerUserPerMinute.

<a id="table-chatbotdepartmentconfig-messagesperdepartmentperminute"></a>
## Chatbot Department Config / Messages Per Department Per Minute

Chatbot Department Config / Messages Per Department Per Minute. Optional department chatbot rate bound. Consider shared demand and provider quotas before changing it.

Default: host default. Source: ChatbotDepartmentConfig.MessagesPerDepartmentPerMinute.

<a id="table-chatbotdepartmentconfig-requirelinkingconfirmation"></a>
## Chatbot Department Config / Require Linking Confirmation

Chatbot Department Config / Require Linking Confirmation. Requires confirmation in the supported account-linking flow. Linking must not bypass verified identity or departmental authorization.

Default: true. Source: ChatbotDepartmentConfig.RequireLinkingConfirmation.

<a id="table-chatbotdepartmentconfig-proactivenotificationsenabled"></a>
## Chatbot Department Config / Proactive Notifications Enabled

Chatbot Department Config / Proactive Notifications Enabled. Allows supported proactive chatbot notifications. Recipient permissions, channel configuration and sending workflows remain authoritative.

Default: false. Source: ChatbotDepartmentConfig.ProactiveNotificationsEnabled.

<a id="notifications"></a>
## Notification rules

Notification rules Rules that alert chosen people when calls, statuses or other events happen. Reduce missed follow-up and review unnecessary message volume. An administrator creates notification rules for new calls, builds an Officers distribution list and runs a communication test before going live. Create notification rules for the events officers and administrators need to know about.

<a id="distribution-lists"></a>
## Distribution lists

Distribution lists Reusable recipient lists, including email lists. Keep audience ownership explicit and review recipients before sending. An administrator creates notification rules for new calls, builds an Officers distribution list and runs a communication test before going live. Create lists for groups you message often, such as Officers.

<a id="communication-tests"></a>
## Communication Tests

Communication Tests Test messages that show whether members can be reached. Collect observed test evidence; registration alone does not prove delivery. An administrator creates notification rules for new calls, builds an Officers distribution list and runs a communication test before going live. Run a communication test before going live and after major changes.

<a id="chat"></a>
## Chat

Chat Team chat for members. A dispatch only helps if it reaches people. This is where you confirm members can actually be contacted. An administrator creates notification rules for new calls, builds an Officers distribution list and runs a communication test before going live. Create channels for your teams once chat is enabled.

<a id="inbox"></a>
## Inbox

Inbox Messages sent to you. A dispatch only helps if it reaches people. This is where you confirm members can actually be contacted. An administrator creates notification rules for new calls, builds an Officers distribution list and runs a communication test before going live. Make sure members have added a mobile number or email address and installed the Responder app.

<a id="outbox"></a>
## Sent Messages

Sent Messages Messages you have sent. A dispatch only helps if it reaches people. This is where you confirm members can actually be contacted. An administrator creates notification rules for new calls, builds an Officers distribution list and runs a communication test before going live. Make sure members have added a mobile number or email address and installed the Responder app.

<a id="compose-message"></a>
## New Message

New Message Send a message, poll or callback request to members. A dispatch only helps if it reaches people. This is where you confirm members can actually be contacted. An administrator creates notification rules for new calls, builds an Officers distribution list and runs a communication test before going live. Make sure members have added a mobile number or email address and installed the Responder app.

<a id="chatbot-settings"></a>
## Chatbot and Assistant integration settings

Chatbot and Assistant integration settings Connect the Resgrid Assistant to chat platforms and choose what it may do. Keep automated access and confirmations under administrator control. An administrator creates notification rules for new calls, builds an Officers distribution list and runs a communication test before going live. Make sure members have added a mobile number or email address and installed the Responder app.

<a id="setting-bigboardmapzoomlevel"></a>
## Big Board Map Zoom Level

Big Board Map Zoom Level. Initial Big Board map zoom. An unset override lets the consuming board select its default.

Default: unset. Source: DepartmentSettingTypes.BigBoardMapZoomLevel.

<a id="setting-bigboardpagerefresh"></a>
## Big Board Page Refresh

Big Board Page Refresh. Big Board refresh interval. Review load and the age of displayed information before changing it.

Default: unset. Source: DepartmentSettingTypes.BigBoardPageRefresh.

<a id="setting-bigboardmapcenteraddress"></a>
## Big Board Map Center Address

Big Board Map Center Address. Address used to center the Big Board. Address details remain on their owning screen.

Default: unset. Source: DepartmentSettingTypes.BigBoardMapCenterAddress.

<a id="setting-bigboardhideunavailable"></a>
## Big Board Hide Unavailable

Big Board Hide Unavailable. Hide unavailable resources on the Big Board; this affects visibility, not their dispatch eligibility.

Default: false. Source: DepartmentSettingTypes.BigBoardHideUnavailable.

<a id="setting-bigboardmapcentergpscoordinates"></a>
## Big Board Map Center Gps Coordinates

Big Board Map Center Gps Coordinates. Explicit board-center coordinates take precedence over address geocoding. Partial coordinates do not overwrite the saved center.

Default: unset. Source: DepartmentSettingTypes.BigBoardMapCenterGpsCoordinates.

<a id="responder-app"></a>
## Responder application

Responder application The member app for receiving dispatches, responding and setting status. Separate member device setup from department configuration and verify each member's channels. Members install the Responder app, a tablet in Engine 1 runs the Unit app and a station screen shows the Big Board. Have every member install the Responder app, sign in and allow notifications.

<a id="unit-app"></a>
## Unit application

Unit application The in-vehicle app that represents an apparatus. Review device identity, assigned crew and unit communications together. Members install the Responder app, a tablet in Engine 1 runs the Unit app and a station screen shows the Big Board. Install the Unit app on each apparatus tablet and select its unit.

<a id="dispatch-app"></a>
## Dispatch application

Dispatch application The dispatcher view for creating and managing calls. Give dispatch personnel a focused operating surface alongside web administration. Members install the Responder app, a tablet in Engine 1 runs the Unit app and a station screen shows the Big Board. Give dispatch access to the people who dispatch, then have them sign in.

<a id="incident-command"></a>
## Incident command and accountability

Incident command and accountability Incident command and accountability for incident commanders. Prepare command structures and access before an exercise or response. Members install the Responder app, a tablet in Engine 1 runs the Unit app and a station screen shows the Big Board. Give command access to the officers who run incidents.

<a id="big-board"></a>
## Big Board

Big Board A station display of calls, units and personnel. Choose display refresh, location age and visibility appropriate to a station screen. Members install the Responder app, a tablet in Engine 1 runs the Unit app and a station screen shows the Big Board. Open the Big Board on a station screen and choose what it shows.

<a id="dashboard"></a>
## Dashboard

Dashboard The department home page: current status, staffing and activity. The classic dashboard shows the personnel status table with everyone's status and staffing, the member's own Actions and Staffing Level panels, and Department Info (department ID, code and text-to-call number). When the Dashboard.Modern feature flag is on, the home page is the customizable widget dashboard instead, and each member can still choose Use the classic dashboard.

<a id="home-dashboard"></a>
## Customizable home dashboard

Customizable home dashboard The new home page (Home, /User/Home/Dashboard) is a board of live widgets such as Active Calls, My Status, Unit Status Board, Operations Map, Upcoming Events and Checklists Due - more than 70 widgets covering every module and paid add-on, grouped in eleven categories. Each member arranges their own board: which widgets, their size (XS, S, M, L, XL or Full), their order and their settings. Boards are saved on the server per member and per department, so the same board appears in any browser the member signs in on; a member can keep up to five boards. Every widget shows only what the member could see on the module page itself: the same permissions, module switches, feature flags, add-ons and visibility rules. A widget the member loses access to stays in the layout, hidden, and returns when access does. Widgets for calls, units, personnel, checklists and chat update live; the others refresh every 1 to 60 minutes. Members who never customize see the department's dashboard template or the built-in default for their role. The new dashboard is behind the Dashboard.Modern feature flag, off by default, and members can always switch back to the classic dashboard.

<a id="dashboard-templates"></a>
## Dashboard templates

Dashboard templates Department admins only (there is no permission to delegate it): on the home dashboard open More dashboard options (the ... button) and choose Dashboard templates, or go to /User/Dashboard/Templates. A template is a board - widgets, sizes, order and settings - with a Name, an optional Description, For (Everyone, one Personnel role or one Group), a Priority (lower numbers win when a member matches several templates) and a Department default checkbox (at most one per department; members not matched by a role or group template see it). New template opens the dashboard in template mode, starting from the built-in administrator board: add widgets, drag, size, open Settings, then Save template. Previews show the admin's own data; each member sees only what their permissions allow, and widgets they cannot use are left out. Members who have never saved a board see template changes at once; members who customized keep their board until they choose Reset to department default. A department can keep up to 25 templates. Saves and deletes are recorded in the system audit trail as DashboardTemplateChanged.

<a id="dashboard-customize"></a>
## Customizing the home dashboard

Customizing the home dashboard Members press Customize. Drag a widget by its handle (press and hold on a touch screen) or focus it and use the arrow keys; pick a size chip or press + and -; remove it with the remove button or Delete, with Undo in the notice. Add widget opens the gallery with category tabs (Operations, Personnel, Map & weather, Communications, Planning, Readiness, Records, Business Ops, Incident Back Office, Intelligence, Administration) and a search box; a few widgets, such as Unit Status Board and My Note, can be added more than once. Each widget menu has Refresh, Settings, Open module, size and Remove; Settings holds options such as rows, priorities, station, group or unit type filters and days ahead. Done saves, Cancel discards the changes, and Reset to department default replaces the board with the template that applies to the member or the built-in default. The More dashboard options menu holds Density (Comfortable or Compact), Wall display (hides the sidebar and top bar for a station screen; it never reveals protected data and does not change session time-outs), Add board (up to five boards; the Home board cannot be deleted) and Use the classic dashboard. A board holds up to 40 widgets. If the board was changed in another window, a banner offers Keep mine or Load latest.

<a id="dashboard-defaults"></a>
## Which dashboard a member sees

Which dashboard a member sees The board resolves in this order: (1) the member's own saved board; (2) the first role or group template that matches one of their personnel roles or their group, lowest Priority first, then by name; (3) the department default template; (4) the built-in default for their audience. Audience: Administrator is a department admin; Dispatcher / Officer can create calls or is a group admin; Member is everyone else. Built-in Member board: My Status, Active Calls, Upcoming Events, Inbox, My Shifts & Trades, Checklists Due, My Trainings, My Certifications, Chat, Weather Alerts, Personnel Status Board. Dispatcher / Officer: My Status, Call Activity, Units Summary, Staffing Summary, Active Calls, Unit Status Board, Operations Map, Awaiting Resources, Pending & Scheduled, Unit Status Alerts, Weather Alerts, Inbox, Personnel Status Board. Administrator: Setup Progress, Call Activity, Staffing Summary, My Status, Active Calls, Unit Status Board, Certification Expiry, Checklist Compliance, Work Orders, Records Overview, Accounts Receivable, Active Incidents, Plan & Add-ons, Workflow Health, Personnel Status Board. Widgets the member cannot use are dropped and the grid closes the gap. A member who has never saved has no stored board, so a template edit reaches them immediately and a role or group change takes effect at once; their first save makes the board theirs.

<a id="dashboard-addon-widgets"></a>
## Add-on widgets and locked cards

Add-on widgets and locked cards Widgets for paid add-ons appear only while the add-on is active: Push-To-Talk (PTT); Data Protection (Advanced Data Protection); Work Orders and My Work Orders (Readiness Pro); Accounts Receivable, Online Payments, Bids, Contracts, Cal OES MARS, Field Costing and Pay Data Completeness (Business Ops); Assistant and AI Dispatch Triage (Enhanced AI); Active Incidents, Incident Desk, Incident Resources, Check-in Queue, Incident Costs and Coordination Boards (Incident Back Office). Invoicing and Incident Back Office widgets keep showing read-only figures after the add-on lapses, as their modules do. Members never see upsell: a widget for an add-on the department lacks is simply not in their gallery. Department admins see those widgets in the Add widget gallery as locked cards (Available with the add-on, and View add-on, which opens its billing page); a locked card cannot be added, and only add-ons the installation offers are shown. A lapsed add-on never removes a widget from anyone's layout: it is hidden and returns when the add-on is bought again.

<a id="dashboard-data-protection"></a>
## Protected data on the dashboard

Protected data on the dashboard In a department enrolled in Advanced Data Protection, widgets are built on operational data - counts, statuses, call numbers and types, unit and personnel names, map pins, dates - which is never protected. Four widgets can carry a protected value, hidden by default: Inbox message subjects (shown as Protected message), Unit Status Board status notes and Personnel Status Board staffing notes (a shield where a note exists), and person contact names in Pre-plans Due for Review. Workflow Health never shows run error details. When a revealable value is on the board, the header shows Protected data hidden - Verify to reveal, which uses the same step-up as every other page; revealed widgets carry a Revealed badge, the reveal covers the whole page, every reveal is audited, and it ends when the department's verification window expires or the member presses Conceal. Message subjects and notes need View protected operational data (default: department and group admins); contact names need View protected contact data (default: department admins). A value the member's role cannot view shows Your role can't view this, and a member who can reveal nothing is never asked to verify; with the default permissions a member cannot read the subjects of their own messages in the Inbox widget, exactly as on the Inbox page. Wall display never reveals.

<a id="dashboard-classic"></a>
## Turning on the new dashboard and returning to classic

Turning on the new dashboard and returning to classic The widget dashboard is controlled by the Dashboard.Modern feature flag, evaluated per department and seeded off. Hosted departments ask Resgrid support to switch it on; self-hosted operators run Resgrid.Console --FeatureFlags --Key=Dashboard.Modern --DepartmentId=1 --On for one department, or without the department for everyone. While it is off every member sees the classic dashboard: the personnel status table, the Actions and Staffing Level panels and Department Info. While it is on, a member can choose More dashboard options, then Use the classic dashboard; the choice is saved per member per department and their boards are kept, and the classic page then shows a banner with Try the new dashboard. /User/Home/Dashboard?classic=true opens the classic page once without changing the choice, and if JavaScript is off or the dashboard cannot load the page links to the classic dashboard. Turning the flag off loses no saved boards. Admins can prepare a department default at /User/Dashboard/Templates before turning the flag on.

<a id="profile"></a>
## My Profile

My Profile Your own profile, contact details and notification preferences. Most members use Resgrid through the Responder app. Getting everyone installed and signed in is what lets dispatches reach them. Members install the Responder app, a tablet in Engine 1 runs the Unit app and a station screen shows the Big Board. Decide which devices each role will use and share the app download links with your members.

<a id="departments"></a>
## Your Departments

Your Departments Switch between the departments you belong to. Most members use Resgrid through the Responder app. Getting everyone installed and signed in is what lets dispatches reach them. Members install the Responder app, a tablet in Engine 1 runs the Unit app and a station screen shows the Big Board. Decide which devices each role will use and share the app download links with your members.

<a id="setting-stripecustomerid"></a>
## Stripe Customer Id

Stripe Customer Id. Billing-provider customer reference managed by subscription workflows; never edit this as an ordinary department setting.

Default: unset. Source: DepartmentSettingTypes.StripeCustomerId.

<a id="setting-braintreecustomerid"></a>
## Brain Tree Customer Id

Brain Tree Customer Id. Legacy billing reference. Use the subscription provider workflow, not a direct settings edit.

Default: unset. Source: DepartmentSettingTypes.BrainTreeCustomerId.

<a id="setting-paddlecustomerid"></a>
## Paddle Customer Id

Paddle Customer Id. Billing-provider customer reference. Managed by authorized billing flows and never returned in assistant evidence.

Default: unset. Source: DepartmentSettingTypes.PaddleCustomerId.

<a id="module-aidisabled"></a>
## AI availability

AI availability. Controls availability of optional AI assistance. Disabling this module can remove navigation and block module operations; it does not delete its records. Turning it on still requires the applicable subscription, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.AiDisabled.

<a id="subscription"></a>
## Base plans and capacity

Base plans and capacity Your plan, its personnel and unit limits, and your add-ons. Choose capacity for actual department needs without inferring prices or terms. Before inviting 40 new members, an administrator confirms the plan has room or asks the managing member to change it. Check the remaining room before adding many members or units.

<a id="setting-mappingpersonnellocationttl"></a>
## Mapping Personnel Location TTL

Mapping Personnel Location TTL. Maximum personnel location age in minutes. Zero keeps stale positions visible without an age limit; assess actual ping ages.

Default: 0. Source: DepartmentSettingTypes.MappingPersonnelLocationTTL.

<a id="setting-mappingunitlocationttl"></a>
## Mapping Unit Location TTL

Mapping Unit Location TTL. Maximum unit location age in minutes. Zero retains stale unit locations indefinitely in supported map consumers.

Default: 0. Source: DepartmentSettingTypes.MappingUnitLocationTTL.

<a id="setting-mappingpersonnelallowstatuswithnolocationtooverwrite"></a>
## Mapping Personnel Allow Status With No Location To Overwrite

Mapping Personnel Allow Status With No Location To Overwrite. Allow a personnel status without a location to replace the previous location-bearing state. This can remove a marker.

Default: false. Source: DepartmentSettingTypes.MappingPersonnelAllowStatusWithNoLocationToOverwrite.

<a id="setting-mappingunitallowstatuswithnolocationtooverwrite"></a>
## Mapping Unit Allow Status With No Location To Overwrite

Mapping Unit Allow Status With No Location To Overwrite. Allow a unit status without a location to overwrite earlier location data. Check hardware and app updates together.

Default: false. Source: DepartmentSettingTypes.MappingUnitAllowStatusWithNoLocationToOverwrite.

<a id="setting-weatheralertsenabled"></a>
## Weather Alerts Enabled

Weather Alerts Enabled. Enable supported weather-alert processing. Configure zones and communication behavior before relying on it.

Default: false. Source: DepartmentSettingTypes.WeatherAlertsEnabled.

<a id="setting-weatheralertminimumseverity"></a>
## Weather Alert Minimum Severity

Weather Alert Minimum Severity. Minimum severity eligible for the weather-alert view. Severity values come from the weather service, not local incident triage.

Default: owning weather default. Source: DepartmentSettingTypes.WeatherAlertMinimumSeverity.

<a id="setting-weatheralertautomessageseverity"></a>
## Weather Alert Auto Message Severity

Weather Alert Auto Message Severity. Minimum weather severity eligible for automatic messages. Review recipients, schedule and duplicate suppression.

Default: owning weather default. Source: DepartmentSettingTypes.WeatherAlertAutoMessageSeverity.

<a id="setting-weatheralertcallintegration"></a>
## Weather Alert Call Integration

Weather Alert Call Integration. Include supported weather context in call integration. It does not certify weather or route safety.

Default: false. Source: DepartmentSettingTypes.WeatherAlertCallIntegration.

<a id="setting-weatheralertcacheminutes"></a>
## Weather Alert Cache Minutes

Weather Alert Cache Minutes. Weather cache duration; longer caching changes freshness and provider load.

Default: owning weather default. Source: DepartmentSettingTypes.WeatherAlertCacheMinutes.

<a id="setting-weatheralertautomessageschedule"></a>
## Weather Alert Auto Message Schedule

Weather Alert Auto Message Schedule. Schedule for automatic weather messaging. Review the department time zone, overnight periods and daylight-saving changes.

Default: unset. Source: DepartmentSettingTypes.WeatherAlertAutoMessageSchedule.

<a id="setting-weatheralertexcludedevents"></a>
## Weather Alert Excluded Events

Weather Alert Excluded Events. Weather event types excluded from configured processing. Review exclusions against approved local procedures.

Default: empty. Source: DepartmentSettingTypes.WeatherAlertExcludedEvents.

<a id="setting-mappingusemapboxoverride"></a>
## Mapping Use Mapbox Override

Mapping Use Mapbox Override. Use the department map provider override. Turning it off on the owning screen removes the stored style and token.

Default: false. Source: DepartmentSettingTypes.MappingUseMapboxOverride.

<a id="setting-mappingmapboxstyleurl"></a>
## Mapping Mapbox Style Url

Mapping Mapbox Style Url. Custom map style reference. Verify the provider style and credential together; disabling the override deletes this value.

Default: unset. Source: DepartmentSettingTypes.MappingMapboxStyleUrl.

<a id="setting-mappingmapboxaccesstoken"></a>
## Mapping Mapbox Access Token

Mapping Mapbox Access Token. Map-provider access token. Only presence is reported. Disabling the override deletes it and requires re-entry to restore.

Default: unset. Source: DepartmentSettingTypes.MappingMapboxAccessToken.

<a id="setting-mappingprovideroverrides"></a>
## Your own map providers

Your own map providers. Department-owned routing, live traffic and address lookup accounts, tried before Resgrid's providers. Only which providers are set is reported, never the keys. Setting all three back to Resgrid default removes the setting.

Default: none (Resgrid's providers). Source: DepartmentSettingTypes.MappingProviderOverrides.

<a id="setting-destinationsuggestions"></a>
## Destination suggestions

Destination suggestions. Suggests the best destination for a call, and for a unit's status destination, from the department's destination rules (Mapping, Manage POIs, Destination Rules) and the incident location, with drive time and distance. Suggest and fill also fills a new call's empty destination with the best one; dispatchers and crews can still choose another. Off by default, because each suggestion looks up drive times.

Default: off (preferred capability window 3 minutes). Source: DepartmentSettingTypes.DestinationSuggestions.

<a id="setting-mappingmapstyle"></a>
## Mapping Map Style

Mapping Map Style. The Mapbox base map (Streets, Outdoors, Light, Dark, Satellite, Satellite with Streets, Navigation Day, Navigation Night, or one of the Mapbox gallery's community styles such as Blueprint, Frank, Moonlight or North Star) every department map shows in a light theme: the website and the Dispatch, Responder, Unit, IC and BigBoard apps. Automatic is Streets. The website uses it only when the server has a website Mapbox token. When the department Mapbox override is on, its custom style and token replace this on the website and in the apps. On a server that draws its own maps (self-hosted tiles) the settings page offers only the styles that server can render: no community styles, and satellite only when the server provides it.

Default: 0. Source: DepartmentSettingTypes.MappingMapStyle.

<a id="setting-mappingmapstylenight"></a>
## Mapping Map Style Night

Mapping Map Style Night. The base map the apps show when the device is in dark mode. Automatic pairs with the day style: road maps use Dark, Navigation Day uses Navigation Night, and Outdoors, the satellite styles and the community styles stay as they are. The website always uses the day style.

Default: 0. Source: DepartmentSettingTypes.MappingMapStyleNight.

<a id="setting-hardwaretrackingstaleafterseconds"></a>
## Hardware Tracking Stale After Seconds

Hardware Tracking Stale After Seconds. Age at which a hardware location becomes stale, clamped to at least one second. Review device reporting intervals.

Default: 180. Source: DepartmentSettingTypes.HardwareTrackingStaleAfterSeconds.

<a id="setting-hardwaretrackingmobilefallbackenabled"></a>
## Hardware Tracking Mobile Fallback Enabled

Hardware Tracking Mobile Fallback Enabled. Use supported mobile location fallback when hardware data is stale. Verify each device/source rather than assuming continuous tracking.

Default: true. Source: DepartmentSettingTypes.HardwareTrackingMobileFallbackEnabled.

<a id="setting-hardwaretrackinglocationretentiondays"></a>
## Hardware Tracking Location Retention Days

Hardware Tracking Location Retention Days. Tracking retention bounded by host configuration and applicable holds. Shortening retention can make data eligible for deletion.

Default: UnitTrackingConfig.DefaultLocationRetentionDays. Source: DepartmentSettingTypes.HardwareTrackingLocationRetentionDays.

<a id="setting-unitstatusthresholds"></a>
## Unit Status Thresholds

Unit Status Thresholds. Visual warning thresholds for time in a unit status. A highlight does not change the unit status or certify availability.

Default: no thresholds. Source: DepartmentSettingTypes.UnitStatusThresholds.

<a id="module-mappingdisabled"></a>
## Mapping availability

Mapping availability. Controls availability of maps and location views. Disabling this module can remove navigation and block module operations; it does not delete its records. Turning it on still requires the applicable subscription, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.MappingDisabled.

<a id="module-mappingnameoverride"></a>
## Mapping menu name

Mapping menu name. Optional display name for maps and location views in consumers that support the override. It changes the label, not permissions or functionality. This release has no separate name-override control in Module Settings.

Default: unset. Source: DepartmentModuleSettings.MappingNameOverride.

<a id="field-unitstatusthresholds-thresholds"></a>
## Unit Status Thresholds / Thresholds

Unit Status Thresholds / Thresholds. Board highlighting thresholds grouped by base status meaning. An empty list disables highlighting.

Default: empty. Source: UnitStatusThresholds.Thresholds.

<a id="field-unitstatusthresholds-acknowledgementnoterequirement"></a>
## Unit Status Thresholds / Acknowledgement Note Requirement

Unit Status Thresholds / Acknowledgement Note Requirement. Whether acknowledging or muting a status timer alert needs a reason: optional, required for alerts, or required for warnings and alerts. It does not change the timers or the unit status.

Default: Optional. Source: UnitStatusThresholds.AcknowledgementNoteRequirement.

<a id="field-unitstatusthreshold-basetype"></a>
## Unit Status Threshold / Base Type

Unit Status Threshold / Base Type. Base status meaning for a threshold, independent of custom status names and colors.

Default: 0. Source: UnitStatusThreshold.BaseType.

<a id="field-unitstatusthreshold-warnseconds"></a>
## Unit Status Threshold / Warn Seconds

Unit Status Threshold / Warn Seconds. Seconds in this status before a warning highlight; zero disables it. An alert at or before this threshold makes the row alert-only.

Default: 0. Source: UnitStatusThreshold.WarnSeconds.

<a id="field-unitstatusthreshold-alertseconds"></a>
## Unit Status Threshold / Alert Seconds

Unit Status Threshold / Alert Seconds. Seconds in this status before a higher-priority highlight; zero disables it. Highlighting does not send a page or change status.

Default: 0. Source: UnitStatusThreshold.AlertSeconds.

<a id="table-weatheralertzone-name"></a>
## Weather Alert Zone / Name

Weather Alert Zone / Name. Administrator-facing label for this weather zone. The name does not define its geographic coverage.

Default: required. Source: WeatherAlertZone.Name.

<a id="table-weatheralertzone-zonecode"></a>
## Weather Alert Zone / Zone Code

Weather Alert Zone / Zone Code. Provider weather-zone identifier. Verify the intended jurisdiction and source coverage.

Default: unset. Source: WeatherAlertZone.ZoneCode.

<a id="table-weatheralertzone-centergeolocation"></a>
## Weather Alert Zone / Center Geo Location

Weather Alert Zone / Center Geo Location. Center point used by supported radius-based weather coverage. Coordinate values stay on their owning map/editor.

Default: unset. Source: WeatherAlertZone.CenterGeoLocation.

<a id="table-weatheralertzone-radiusmiles"></a>
## Weather Alert Zone / Radius Miles

Weather Alert Zone / Radius Miles. Radius in miles for supported geographic matching. Review actual coverage and provider behavior; larger is not automatically safer.

Default: editor default. Source: WeatherAlertZone.RadiusMiles.

<a id="table-weatheralertzone-isactive"></a>
## Weather Alert Zone / Is Active

Weather Alert Zone / Is Active. Whether the zone participates in supported weather processing. Department weather settings and worker operation are additional requirements.

Default: false. Source: WeatherAlertZone.IsActive.

<a id="table-weatheralertzone-isprimary"></a>
## Weather Alert Zone / Is Primary

Weather Alert Zone / Is Primary. Marks the primary zone for consumers that select a default. Review other active zones rather than assuming they are disabled.

Default: false. Source: WeatherAlertZone.IsPrimary.

<a id="mapping"></a>
## Mapping

Mapping The live map of personnel, units and calls. A current map shows where responders and apparatus are and puts important locations in front of dispatchers. A department adds its stations and hospitals as points of interest and lets personnel locations expire after 60 minutes so old positions disappear. Set how long locations stay on the map in the mapping settings.

<a id="pois"></a>
## Points of Interest

Points of Interest Points of interest such as stations, hospitals and hydrants. A current map shows where responders and apparatus are and puts important locations in front of dispatchers. A department adds its stations and hospitals as points of interest and lets personnel locations expire after 60 minutes so old positions disappear. Add the locations dispatchers need to see, one at a time or by importing a KML, KMZ or CSV file; addresses in the file are looked up in the background. Give a POI type a custom icon to replace its marker on the map and in the apps. The import runs in the background: the POIs page shows each import's progress, retries addresses the lookup service could not answer and reports the rows it could not place. Department admins and hydrant managers can import, and department admins can remove POIs in bulk.

<a id="poi-capabilities"></a>
## POI capabilities

POI capabilities What your points of interest offer, such as a cath lab, stroke center, helipad or drafting site, ticked on each POI so dispatchers and crews can filter and rank destinations by what the patient or task needs. A current map shows where responders and apparatus are and puts important locations in front of dispatchers. Open Mapping, then Manage POIs, and choose POI Capabilities. Add what your destinations offer, then tick them on each POI, or add a Capabilities column to a POI import file. Department admins manage the list; deleting a capability takes it off every POI. An import file's Capabilities column (also Specialties or Services, several values separated by ;, , or |) tags each imported POI and adds any capability the department does not have yet. Capabilities show in the POI list, the web call form's destination list and the apps' destination pickers, where crews filter by one capability and see the nearest first. Destination rules can require or prefer them, and destination availability can mark one unavailable.

<a id="destination-availability"></a>
## Destination availability

Destination availability Whether each destination can take patients right now: open, limited, on diversion or closed, with the capabilities it cannot provide, a note and how long the report lasts. A current map shows where responders and apparatus are and puts important locations in front of dispatchers. Open Mapping, then Manage POIs, and choose Destination Availability, or use the Destination availability link under the call form's Destination. Choose {SET} on a destination when it reports a diversion, a closure or a capability that is down. Department admins and anyone who can create calls can set it. A report lasts from 1 to 24 hours or until someone changes it; setting it back to Open clears it, and an expired report reads as open. Every app badges a destination that is on diversion or closed and ranks it after those that can take the patient, and destination suggestions only fall back to it when nothing better is left. Nothing is refused: a crew with no alternative can still go there. The Omni Dispatch app's Destinations screen shows the same list.

<a id="destination-rules"></a>
## Destination rules

Destination rules Rules that choose the destination types and the required and preferred capabilities for a call's type and priority and the travelling unit's type, so Resgrid can suggest the nearest suitable destination with drive time and distance. A current map shows where responders and apparatus are and puts important locations in front of dispatchers. Open Mapping, then Manage POIs, and choose Destination Rules. Add a rule for each case, for example cardiac calls go to a hospital with a cath lab and a water tender goes to a drafting site, check them with Try it, then set Destination suggestions to Suggest destinations or Suggest and fill in the destination. A rule matches on call types, priorities and unit types. Rules for a unit type come first, then rules with more conditions; the first rule with destination types sets them, later ones narrow them, and a rule that would leave no type is set aside, which Try it shows with the reason. A preferred capability only wins within the preferred window (3 minutes by default) of the nearest destination that meets the requirements. Suggestions rank destinations that are ready first, then those on diversion, then those missing a required capability or with it down, then closed ones, and look up drive times for the five nearest that can take the patient. Suggestions are Off by default because each one looks up drive times. In Suggest and fill mode New Call fills an empty destination, never one somebody chose or a free destination location; Update Call only suggests. Department admins manage the rules and the setting.

<a id="custom-map-icons"></a>
## Custom map icons

Custom map icons Your own icons for unit types, call types, points of interest, personnel roles and stations, shown in lists, the apps and on the map. A current map shows where responders and apparatus are and puts important locations in front of dispatchers. Upload a square PNG with a transparent background, 128 × 128 px or larger, on the unit type, call type, POI type or role page; the page previews it on a light and a dark map. To choose which icon a person with several roles shows, order the roles on the Roles page. Use Status map icons or Priority map icons on the same page to add an icon for each status or priority. For stations, upload one icon for all stations, or give a station its own, under Groups > Station icons or on the station's Edit Group page. Icons can be PNG, JPEG, GIF (first frame) or WebP files up to 1 MB and between 16 and 4096 px on each edge; SVG is not accepted. Every icon is stored as a 128 × 128 px PNG and drawn at 32 × 32 px on the map and 20 × 20 px in lists, so a bold shape works better than fine detail or text. A type without a custom icon keeps its built-in map icon. A person's map pin uses the icon of the first role they hold, in the role order, that has one; people without one keep the default person marker. A call type's icon for a priority, and a unit type's or role's icon for a status, replaces its own icon on the map while that priority or status holds; without one, the type's or role's own icon carries a small badge in the color of the priority or status. Types and roles with no uploaded icon show the built-in pin in that color. A priority or status with no meaning set takes the color its name suggests, and shows a gray pin when the name suggests none. A station shows its own icon, else the icon for all stations, else the built-in station pin; station icons never change with status. Mapping > Map Icon Legend shows every combination.

<a id="map-icon-legend"></a>
## Map icon legend

Map icon legend Every icon the map shows for calls, units and personnel in each priority and status, with the status and priority colors. A current map shows where responders and apparatus are and puts important locations in front of dispatchers. Open Mapping, then Map Icon Legend. Department admins can choose Edit icons on a call type, unit type or role to upload an icon for a priority or status. In the Responder, Unit and IC apps, tap the legend button on the map to see the same legend. A call's icon follows its type and priority, a unit's its type and status, and a member's their role and status; staffing levels never change an icon. A member On Unit takes the color of their unit's status. Built-in pins take the color of the meaning of the status or priority: Available green, Dispatched amber, Responding red, On scene blue, Transporting purple, Returning teal, Staging and standby brown, On break slate, Unavailable charcoal; priorities Low green, Medium yellow, High orange, Emergency red. A status or priority with no meaning set takes the color its name suggests (such as Emergency or Out of Service) and shows a gray pin when the name suggests none, as does a unit or member that has set no status, so set meanings on the Custom Statuses and Call Priorities pages to be sure. The maps on New Call and Update Call, and the call maps in the Dispatch app, draw only available units and personnel; the dispatch lists still show everyone with their status. Each status and priority color lists the department's own statuses and priorities that show in it, and every name, icon and upload in the legend comes from the department's own call types, priorities, unit types, status sets, statuses and roles, so it changes as soon as one of them or an icon does. The legend lists On Unit under no color, since it follows the unit. In the apps the legend button is at the top right of the Responder map, above the recenter button on the Unit map, and above the layers button on the IC main map and on an incident's command map; Responder leaves personnel out of the legend for members who can't see personal information, as its map does.

<a id="map-visualizations"></a>
## Map visualizations

Map visualizations Heat maps, hexagon grids and overlays of calls, response times, station coverage, unit and personnel responses and movement, from a built-in library or saved by the department. A current map shows where responders and apparatus are and puts important locations in front of dispatchers. Open Mapping, then Visualizations, pick a library visualization such as Call hot zones, adjust its date range, filters and layers on the Customize tab, and save it. Department admins can share a saved visualization with everyone. Every visualization reads its data fresh under the viewer's own permissions: dispatch scope limits the calls, and the location visibility settings limit member and unit locations, so sharing a visualization never shares data. Response times run from the call being logged to the first unit on scene or responding, using status base types, so they appear only when units set statuses. Unit and member paths cover at most 7 days, status positions at most 12 months, and one view at most 100,000 calls.

<a id="road-closures"></a>
## Road closures

Road closures Closed and slowed roads and areas drawn on the map, with who closed them, why, a contact and the times they are in effect. Dispatch ranking routes around full closures and slows routes through partial ones. A current map shows where responders and apparatus are and puts important locations in front of dispatchers. Open Mapping, then Road Closures, and choose New Closure. Draw the closed road as a line or the area as a shape, choose full or partial, the traffic it stops and its times (use Repeat for a road closed only at night), and save. Set how units travel on each unit type (driving, cycling, on foot or air) so run cards and the nearest units list measure them the way they move. A full closure is routed around where the routing provider can avoid drawn areas (Valhalla) or closed road points (Mapbox, driving); with other providers the route is charged the closure's detour time and marked as crossing it. A partial closure, and a full one the call or the unit's start is inside, is driven through at the closure's speed. Each closure applies to the traffic it is set to stop: vehicles, bicycles or pedestrians; air units fly a straight line and never meet one. Anyone who can see calls can see closures; department admins and members who can create calls manage them.

<a id="weather"></a>
## Weather zones and alerts

Weather zones and alerts Weather alert zones and alert notifications. Review alert coverage and automatic-message settings before use. A department adds its stations and hospitals as points of interest and lets personnel locations expire after 60 minutes so old positions disappear. Draw the zones you cover and choose who is alerted.

<a id="map-layers"></a>
## Map Layers

Map Layers Add your own shapes and overlays to the map. A current map shows where responders and apparatus are and puts important locations in front of dispatchers. A department adds its stations and hospitals as points of interest and lets personnel locations expire after 60 minutes so old positions disappear. Confirm members allow location sharing in the Responder app, and decide how long a position should stay on the map.

<a id="live-routing"></a>
## Live Routing

Live Routing Directions and routes for responding resources. A current map shows where responders and apparatus are and puts important locations in front of dispatchers. A department adds its stations and hospitals as points of interest and lets personnel locations expire after 60 minutes so old positions disappear. Confirm members allow location sharing in the Responder app, and decide how long a position should stay on the map.

<a id="custom-maps"></a>
## Custom map layers

Custom map layers Upload your own map images, such as a campus or venue map. Give responders useful reference layers with an identified owner and update process. A department adds its stations and hospitals as points of interest and lets personnel locations expire after 60 minutes so old positions disappear. Confirm members allow location sharing in the Responder app, and decide how long a position should stay on the map.

<a id="indoor-maps"></a>
## Indoor maps

Indoor maps Floor plans and zones for buildings. Keep building references available to authorized users without inferring safe routes. A department adds its stations and hospitals as points of interest and lets personnel locations expire after 60 minutes so old positions disappear. Confirm members allow location sharing in the Responder app, and decide how long a position should stay on the map.

<a id="routes"></a>
## Routes

Routes Planned routes, such as patrols or transport runs, who is assigned to them, and routes a call can carry so the unit working it runs the planned stops. Prepare recurring route information with current source ownership. A department adds its stations and hospitals as points of interest and lets personnel locations expire after 60 minutes so old positions disappear. Confirm members allow location sharing in the Responder app, and decide how long a position should stay on the map. A route plan that is active and has a start and an end can be put on a call; the unit working the call then runs it in the Omni Unit app, and the run links back to the call.

<a id="setting-allowsignupsformultipleshiftgroups"></a>
## Allow Signups For Multiple Shift Groups

Allow Signups For Multiple Shift Groups. Allow a member to sign up for multiple shift groups. Review overlaps and local coverage rules separately.

Default: false. Source: DepartmentSettingTypes.AllowSignupsForMultipleShiftGroups.

<a id="setting-allowshiftoversignup"></a>
## Allow Signups Past Group Slots

Allow Signups Past Group Slots. Let members sign up for a shift group even when every slot its roles offer (required and optional) is taken. They still need one of the group's roles. Supervisors can always assign people past the slots.

Default: false. Source: DepartmentSettingTypes.AllowShiftOverSignup.

<a id="setting-shifttradeapprovers"></a>
## Shift trade approvers

Shift trade approvers. Who may pick which applicant takes a shift trade request and approve trades waiting for approval. Department admins always may; group admins, the person trading out and members of chosen roles are the department's choice.

Default: missing = department admins, group admins and the person who asked for the trade. Source: DepartmentSettingTypes.ShiftTradeApprovers.

<a id="setting-dispatcherdeskconfig"></a>
## Dispatcher desk

Dispatcher desk. Turns the dispatcher desk on and sets how it works: which work is offered to the dispatchers on duty in the Dispatch app (pending and scheduled calls, texts, phone calls, emails, chats), how long an offer waits and how many items a dispatcher holds before counting as busy, how many missed offers in a row set a dispatcher Away, and what happens to texts and phone calls when nobody is free (wait in the queue, Enhanced AI, or voicemail). It also sets what a call emailed to the dispatch address becomes (dispatched at once, saved as pending, or read by Enhanced AI into a pending call) and the break and duty rules: break reminders, automatic return from breaks, the minimum available, refusing breaks below it, no desk alerts on break, and whether dispatchers must be on a shift or may go on duty without one (Shifts, Dispatcher Desk, Dispatcher Desk Settings). Off by default; emailed calls are dispatched at once until Emailed calls is changed, which applies even while routing is off.

Default: missing = desk off; emailed calls are dispatched at once (email call mode 0). Once turned on: every kind of work is routed, 45-second offers, 1 item per dispatcher, Away after 2 missed offers in a row, texts wait in the queue, phone calls go to Enhanced AI voice intake (a voicemail without it), 5-minute break reminders, no automatic return, at least 1 dispatcher available, dispatchers must be on a shift. Source: DepartmentSettingTypes.DispatcherDeskConfig.

<a id="module-shiftsdisabled"></a>
## Shifts availability

Shifts availability. Controls availability of shift scheduling. Disabling this module can remove navigation and block module operations; it does not delete its records. Turning it on still requires the applicable subscription, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.ShiftsDisabled.

<a id="module-shiftsnameoverride"></a>
## Shifts menu name

Shifts menu name. Optional display name for shift scheduling in consumers that support the override. It changes the label, not permissions or functionality. This release has no separate name-override control in Module Settings.

Default: unset. Source: DepartmentModuleSettings.ShiftsNameOverride.

<a id="module-calendardisabled"></a>
## Calendar availability

Calendar availability. Controls availability of calendar events. Disabling this module can remove navigation and block module operations; it does not delete its records. Turning it on still requires the applicable subscription, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.CalendarDisabled.

<a id="module-calendarnameoverride"></a>
## Calendar menu name

Calendar menu name. Optional display name for calendar events in consumers that support the override. It changes the label, not permissions or functionality. This release has no separate name-override control in Module Settings.

Default: unset. Source: DepartmentModuleSettings.CalendarNameOverride.

<a id="shifts"></a>
## Shifts

Shifts Shift schedules, sign-ups and trades. Shifts show who is on duty and can drive dispatch, so calls page the people who are actually working. A career department sets up A, B and C shifts, and a volunteer department uses shift sign-ups for weekend duty crews. Create your shifts, assign groups and members, and decide whether dispatch follows shifts.

<a id="calendar"></a>
## Calendar

Calendar The department calendar for meetings, events and training nights. Shifts show who is on duty and can drive dispatch, so calls page the people who are actually working. A career department sets up A, B and C shifts, and a volunteer department uses shift sign-ups for weekend duty crews. Add recurring meetings and training nights.

<a id="new-calendar-item"></a>
## New Calendar Event

New Calendar Event Add an event to the department calendar. Shifts show who is on duty and can drive dispatch, so calls page the people who are actually working. A career department sets up A, B and C shifts, and a volunteer department uses shift sign-ups for weekend duty crews. Have your rotation or duty schedule, and decide whether dispatch should follow shifts.

<a id="workshifts"></a>
## Workshifts

Workshifts Repeating work schedules for staff. Review schedule ownership and local time separately from dispatch eligibility. A career department sets up A, B and C shifts, and a volunteer department uses shift sign-ups for weekend duty crews. Have your rotation or duty schedule, and decide whether dispatch should follow shifts.

<a id="shift-staffing"></a>
## Shift staffing

Shift staffing Who actually worked each shift day, approved by a supervisor, for assigned, signup and static shifts; a day nobody approves is staffed automatically from the shift. Shifts show who is on duty and can drive dispatch, so calls page the people who are actually working. Open Shifts, then Shift Staffing. Pick a day in Staffing by Day, past days included, check the automatic staffing, change the people if needed and choose Approve Staffing. Revert to Automatic drops an approved staffing.

<a id="on-shift-hours-report"></a>
## On-Shift Hours report

On-Shift Hours report Each person's hours on shift for a period across assigned, signup and static shifts, for the department, a group or one shift, split into approved and automatic hours. Shifts show who is on duty and can drive dispatch, so calls page the people who are actually working. Open Reports, then On-Shift Hours Report. Choose the period and whether it covers the department, a group or a shift. Approve the staffing of the days that need an exact record on the Shift Staffing page before running it for payroll or compliance.

<a id="dispatcher-desk"></a>
## Dispatcher Desk

Dispatcher Desk A live board of the dispatchers on duty and the work waiting for them. Pending and scheduled calls, texts, phone calls, emails and chats are offered to one free dispatcher at a time, who takes them in the Dispatch app on a phone or tablet. Work goes to someone who is free: a dispatcher holding work counts as busy, an offer nobody takes moves on to the next dispatcher, and work left waiting alerts everyone on duty. Dispatchers can work the desk from wherever they are, not only from a console. Two volunteer dispatchers cover a weekend from their phones. A text about a tree down across a road is offered to the one who is free and becomes a call, while the other is on a meal break and gets no desk alerts until they are back. Give your dispatchers the Dispatch app login permission and put them on a shift's dispatch roster. Then open Shifts, Dispatcher Desk, Dispatcher Desk Settings, turn on Route work to on-duty dispatchers, choose what to Send to the desk and save. Dispatchers sign in to the desk in the Dispatch app; the Dispatcher Desk page shows who is available, busy or on break and the open queue, and a desk supervisor can end a break or move work to someone else.

<a id="shift-dispatchers"></a>
## Dispatchers on duty

Dispatchers on duty Put dispatchers on Assigned and Sign-up shifts, on a dispatch-only shift or beside the responders, with the number of dispatch positions to fill, a minimum on the desk and a desk supervisor. The desk offers work only to dispatchers who are really on duty: those on the roster of a shift day running now, unless the department allows duty without a shift. Open dispatch positions show on the shift day, and On Duty Now lists the dispatchers with their desk state and breaks. A dispatch center runs dispatch-only A, B and C shifts with two dispatchers each, and a volunteer department adds one dispatcher position to its weekend sign-up shift. Open Shifts, create or edit a shift and turn on This shift takes dispatchers. Set Dispatchers needed and, on an Assigned shift, tick the people on the Dispatch roster and mark a desk supervisor; on a Sign-up shift, dispatchers choose Sign up as dispatcher on the shift day. Only people with the Dispatch app login permission can be added.

<a id="dispatcher-breaks"></a>
## Dispatcher Breaks

Dispatcher Breaks Plan breaks and meals for the day's dispatchers on one coverage timeline that shows who is on duty, who is on a break and where coverage would drop below the minimum. Breaks stay staggered so the desk is never left empty: Plan breaks spreads a shift's break plan across its dispatchers without overlaps, and the planner warns about overlapping breaks and gaps before they happen. Dispatchers are reminded before each break and, by default, get no desk alerts while on it. On a 12-hour shift with three dispatchers, Plan breaks places each one's 30-minute meal and two 15-minute breaks at different times, and each dispatcher is reminded five minutes before a break is due. Set a break plan on each shift, or a default break plan in Dispatcher Desk Settings. Then open Shifts, Dispatcher Desk, Dispatcher Breaks, choose a date and a shift day and use Plan breaks, or plan single breaks yourself. Desk supervisors and shift managers plan anyone's breaks; dispatchers plan their own only on shifts that allow it. A break is a length, so a break started early also ends early.

<a id="dispatcher-desk-settings"></a>
## Dispatcher Desk Settings

Dispatcher Desk Settings Turn routing on and choose which work goes to the desk, how long an offer waits, what happens to texts and phone calls when every dispatcher is busy, what an emailed call becomes, and the rules for breaks and duty. The department decides once how the desk behaves, from how quickly unanswered work moves on to whether a break may leave fewer dispatchers available than the minimum. A department sends texts and phone calls to the desk, gives each offer 60 seconds, lets Enhanced AI fill in a pending call when nobody is free, and saves emailed calls as pending for a dispatcher to review. Department administrators open Shifts, then Dispatcher Desk, Dispatcher Desk Settings. Turn on Route work to on-duty dispatchers, choose Send to the desk, the offer timeout and items per dispatcher, what happens when every dispatcher is busy, Emailed calls and the break rules, then save. The Enhanced AI choices need the Enhanced AI add-on.

<a id="dispatcher-desk-activity"></a>
## Dispatcher Activity

Dispatcher Activity Desk activity for up to 92 days: work by kind, completed, overflowed, cancelled and escalated items and the average time to answer, and for each dispatcher the offers accepted, declined and missed, the time to accept and complete, and breaks taken, missed or returned from late. See whether work is answered quickly, how often it overflows or escalates and whether breaks run on time, then adjust staffing, offer timeouts or break plans from the numbers. A supervisor sees that texts often escalated on Friday evenings and adds a second dispatcher to that shift. Department administrators and desk supervisors open Shifts, then Dispatcher Desk, Dispatcher Activity, choose the From and To dates and select Show.

<a id="module-trainingdisabled"></a>
## Training availability

Training availability. Controls availability of training. Disabling this module can remove navigation and block module operations; it does not delete its records. Turning it on still requires the applicable subscription, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.TrainingDisabled.

<a id="module-trainingnameoverride"></a>
## Training menu name

Training menu name. Optional display name for training in consumers that support the override. It changes the label, not permissions or functionality. This release has no separate name-override control in Module Settings.

Default: unset. Source: DepartmentModuleSettings.TrainingNameOverride.

<a id="module-certificationsdisabled"></a>
## Certifications availability

Certifications availability. Shows or hides Certifications in the web app's navigation, dashboard widgets and search. Hiding it does not delete data or change permissions. Certification enforcement is not affected.

Default: false. Source: DepartmentModuleSettings.CertificationsDisabled.

<a id="trainings"></a>
## Trainings

Trainings Training assignments with materials and optional quizzes. See who is qualified and whose certifications are expiring before it affects who can respond. EMT and Driver/Operator certification types are added with their renewal periods, and an annual refresher training is assigned to every member. Create a training and assign it to the members or groups who need it.

<a id="certification-types"></a>
## Certification Types

Certification Types The certifications and qualifications you track. See who is qualified and whose certifications are expiring before it affects who can respond. EMT and Driver/Operator certification types are added with their renewal periods, and an annual refresher training is assigned to every member. Add each certification type with its renewal period.

<a id="certification-dashboard"></a>
## Certification Dashboard

Certification Dashboard Who holds which certifications and what is expiring. See who is qualified and whose certifications are expiring before it affects who can respond. EMT and Driver/Operator certification types are added with their renewal periods, and an annual refresher training is assigned to every member. Record members' current certifications, then review what expires soon.

<a id="new-training"></a>
## New Training

New Training Create a training, with an optional quiz. See who is qualified and whose certifications are expiring before it affects who can respond. EMT and Driver/Operator certification types are added with their renewal periods, and an annual refresher training is assigned to every member. List the certifications your department tracks and their renewal periods.

<a id="certification-settings"></a>
## Certification Settings

Certification Settings What happens when a certification expires, grace periods and reminders. See who is qualified and whose certifications are expiring before it affects who can respond. EMT and Driver/Operator certification types are added with their renewal periods, and an annual refresher training is assigned to every member. List the certifications your department tracks and their renewal periods.

<a id="my-certifications"></a>
## My Certifications

My Certifications Your own certification records. See who is qualified and whose certifications are expiring before it affects who can respond. EMT and Driver/Operator certification types are added with their renewal periods, and an annual refresher training is assigned to every member. List the certifications your department tracks and their renewal periods.

<a id="setting-trainingrecordsettings"></a>
## Training record settings

Training record settings. How training records are kept: rounding of calendar check-in hours, whether members may submit outside training themselves, and the minimum years records are kept after a member leaves.

Default: no rounding, member submissions on, no minimum retention. Source: DepartmentSettingTypes.TrainingRecordSettings.

<a id="field-trainingrecordsettings-hoursroundingminutes"></a>
## Training record settings / Round check-in hours to

Training record settings / Round check-in hours to. Hours from check-in to check-out on calendar events that count as training are rounded to this step: 5, 6, 10, 15, 30 or 60 minutes, or 0 for the exact minute.

Default: 0 (exact to the minute). Source: TrainingRecordSettings.HoursRoundingMinutes.

<a id="field-trainingrecordsettings-hoursroundingmode"></a>
## Training record settings / Rounding direction

Training record settings / Rounding direction. Whether check-in hours round to the nearest step, down or up. It has no effect while hours are kept to the exact minute.

Default: 0 (nearest). Source: TrainingRecordSettings.HoursRoundingMode.

<a id="field-trainingrecordsettings-membersubmissionsdisabled"></a>
## Training record settings / Members may not submit outside training

Training record settings / Members may not submit outside training. When on, members cannot submit outside training themselves. Officers can still record it through an import or a certification credit.

Default: false (members may submit). Source: TrainingRecordSettings.MemberSubmissionsDisabled.

<a id="field-trainingrecordsettings-defaultretentionyears"></a>
## Training record settings / Keep records after a member leaves (years)

Training record settings / Keep records after a member leaves (years). Minimum years a former member's training records are kept, used when their category sets none. 0 sets no minimum.

Default: 0 (no minimum). Source: TrainingRecordSettings.DefaultRetentionYears.

<a id="training-transcript"></a>
## Training Transcript

Training Transcript Each member's course completions, training records, calendar attendance, imports and outside certificates, with their hours and whether they count. One record of every member's training and hours for renewals and audits, without adding up sign-in sheets by hand. Before renewing a certification, a member downloads their transcript as a PDF with hours by category, and the training officer opens the same transcript from the member's name. Open Trainings and choose My transcript. Officers who review a member's records pick the member on the transcript. Only verified records count toward hours.

<a id="field-trainingrecordsettings-submissionreviewmode"></a>
## Training record settings / Review of member submissions

Training record settings / Review of member submissions. Whether training hours members submit wait for a reviewer before they count: every submission (the default), none (accepted on submit and shown as self-reported), or only selected categories or above an hours threshold.

<a id="field-trainingrecordsettings-reviewcategoryids"></a>
## Training record settings / Categories that need review

Training record settings / Categories that need review. With review of selected submissions, submissions in these training categories wait for a reviewer.

<a id="field-trainingrecordsettings-reviewabovehours"></a>
## Training record settings / Review submissions above (hours)

Training record settings / Review submissions above (hours). With review of selected submissions, submissions of more hours than this wait for a reviewer. 0 means no threshold.

<a id="field-trainingrecordsettings-rolereviewersowngrouponly"></a>
## Training record settings / Role reviewers only for their own group

Training record settings / Role reviewers only for their own group. When on, reviewers named by role (Record Training Completions or Assign Training) review only the members of the group they are in. Department admins still review everyone and group admins their own group.

<a id="field-trainingrecordsettings-reviewernotices"></a>
## Training record settings / Reviewer notices

Training record settings / Reviewer notices. How reviewers hear about new submissions: a notice for each one (the default), one summary a day, or none (the Training reviews inbox and the home page item only).

<a id="training-submission-reviews"></a>
## Training reviews (members' training hours with evidence)

Training reviews (members' training hours with evidence) Members record continuing education or training hours done elsewhere with a certificate, photo, document or receipt; reviewers approve them, deny them or ask for more information. Outside training reaches the transcript with its proof, reviewed when the department wants it, and members are told at every step. A member uploads a photo of a hazardous materials refresher certificate with 8 hours; the training captain sees it in Training reviews, opens the photo and approves it. Choose the review in Training record settings and give training captains and instructors Record Training Completions on the Security page.

<a id="training-external-submissions"></a>
## Outside Training Submissions

Outside Training Submissions Members submit courses they completed outside the department, with hours and an optional certificate; an officer verifies or rejects each one in the records queue. Outside courses and continuing education reach the transcript with proof attached, and nothing counts until an officer has checked it. A paramedic finishes an online continuing education course and submits it with the certificate and 4 hours; the training officer verifies it in the queue and the hours count. Members choose Submit outside training on their transcript. Officers open the records queue to verify, reject or resolve possible duplicates. Submissions can be switched off in Training record settings.

<a id="training-categories"></a>
## Training Categories

Training Categories Group training hours into categories with optional hour targets and retention, start from templates for fire (ISO), EMS (NCCP), workplace safety (OSHA), industry, search and rescue, law enforcement, security, CPD, Canada or the EU, and categorize existing records in bulk. Hours add up the way your regulator, licensing body, insurer or accreditor asks for them, and targets show who is behind. A department applies the ISO template, sets an annual target for company training, then categorizes last year's drill records in bulk by course name. In Trainings, open Training records, then Training categories. Apply a template or add your own, then use Categorize history to give uncategorized records a category.

<a id="training-credit-mappings"></a>
## Certification Credit Mappings

Certification Credit Mappings Count a training category's verified hours toward a certification's renewal, for the whole category or only one part of it. Renewal hours fill in from the training members already do, instead of being typed onto each certification. Hours in the EMS continuing education category count toward the EMT certification, so a member's renewal progress moves as soon as a session is verified. In Trainings, open Training records, then Certification credits, and map a category to a certification type. Records completed before the mapping was turned on become credits only when you apply history.

<a id="training-history-backfill"></a>
## Training History Backfill

Training History Backfill Bring training completions and training records from before the transcript onto members' transcripts, with a dry run that counts what would be written. Transcripts start with the history your department already has in Resgrid instead of empty. An officer runs a dry run, sees how many past completions and training records were found, then starts the backfill, which keeps running in the background. In Trainings, open Training records, then History backfill, and run a dry run first. Backfilled records become certification credits only when you apply history to a certification type.

<a id="training-history-import"></a>
## Training History Import

Training History Import Import completions from another system with a CSV file: check how each row matched a member, fix the rows that did not match, then import. Years of records from a spreadsheet or a previous training system reach members' transcripts without retyping. A department leaving its old records system exports its completions, imports them, and picks the right member for the few rows matched by name only. In Trainings, open Training records, then Import training history. Download the template, fill in one row per completion with the member, course, date and hours, upload it and review the check before importing.

<a id="training-hours-report"></a>
## Training Hours Report

Training Hours Report Verified training hours by category, member or month, with CSV and PDF export and former members on request. Answer how many hours the department trained, and in what, for budgets, grants and annual reports. For the annual report, the chief exports last year's training hours by category as a PDF. In Trainings, open Training records, then Training hours. Choose the period and grouping, then export to CSV or PDF.

<a id="lms-seats"></a>
## LMS Seats

LMS Seats Switch the LMS on for members one at a time on their personnel page, or for a whole group at once, within the seats your packs buy. You pay only for the members you train through the LMS; everyone else keeps the free Training module. A combination department enables its 40 career staff for LMS courses and leaves its volunteers on free Training. In Personnel, use LMS seats to enable a group, or open a member and choose Enable LMS. Only active members can hold a seat; add a pack when the seats run out.

<a id="lms-packs"></a>
## LMS Packs

LMS Packs Buy the LMS add-on in 25-member packs and change the number of packs, with the price shown before any change. The bill follows the seats you need: graduated tiers make large rosters cheaper per member, and added packs are prorated. A department with 110 LMS-enabled members holds 5 packs; when it passes 125 it adds a sixth pack and pays the prorated difference at once. In Subscription, open the LMS card, choose the number of packs and check out. Removing packs takes effect at the next billing date.
<a id="my-courses"></a>
## My Courses

My Courses The courses assigned to a member, the courses they can join and the Resgrid Academy lessons, with progress and due dates. Members see what to finish next and pick up where they stopped, on the website or in the mobile app. A new member opens My courses, finishes the Getting started lesson and its short check, and the completion appears on their transcript. Open Trainings and choose Courses. Assigned courses come first; Resgrid Academy lessons are always there while the Training module is on.

<a id="academy"></a>
## Resgrid Academy

Resgrid Academy Short Resgrid video lessons, each with a three-question check, published by Resgrid for every department at no cost, plus role paths for members, dispatchers, officers and administrators. New members and officers learn the system the same way, and each finished lesson counts on their transcript under Systems training. The training officer assigns the Dispatcher path to new dispatch staff and follows which lessons each person has finished. Open Trainings and choose Academy. People who can assign training assign a lesson or a role path to members, roles or groups. Lessons for modules or add-ons the department does not use stay hidden.

<a id="lms-course-builder"></a>
## Course Builder

Course Builder Department-built courses with modules, video, documents, protocol lessons, assessments drawn from question banks and surveys, approved and versioned like protocols. Run the department's own training online, with scored assessments, seat time and completions that issue or renew certifications. The EMS coordinator builds a yearly refresher with a video and a 20-question test drawn from a bank with a pass mark of 80%, then assigns it to every EMT. Nothing to set up yet: the course builder is part of the LMS add-on, which is not available yet.

<a id="lms-grading"></a>
## Assessment Grading

Assessment Grading A queue of free-text answers waiting for a grader, and item analysis showing how hard each question is and how well it separates strong and weak results. Written answers get a fair, recorded grade, and weak questions are found and fixed. A training officer grades three short answers on last week's hazmat test; the analysis shows one question nearly everyone misses, so it is rewritten. Nothing to set up yet: grading is part of the LMS add-on, which is not available yet.

<a id="setting-learningrequirementsettings"></a>
## Learning requirement settings

Learning requirement settings. Training-backed enforcement for the department (off by default), the largest share of a role's members one enforcement run may remove, and the default at-risk warning for new requirements.

Default: enforcement off, 20%, 14 days. Source: DepartmentSettingTypes.LearningRequirementSettings.

<a id="field-learningrequirementsettings-enforcementenabled"></a>
## Learning requirement settings / Training-backed enforcement

Learning requirement settings / Training-backed enforcement. When on, a requirement that opts in can expire the certification it backs once a member is overdue past the grace period; the certification module then removes roles after its own grace period. Off by default, never while the LMS add-on is inactive, and turning it on first shows who would lose which role.

Default: false (off). Source: LearningRequirementSettings.EnforcementEnabled.

<a id="field-learningrequirementsettings-maxroleremovalsharepercent"></a>
## Learning requirement settings / Largest share of a role removed in one run

Learning requirement settings / Largest share of a role removed in one run. A run that would remove more than this percentage of a role's active members is refused, and the department admins are told. Default 20.

Default: 20. Source: LearningRequirementSettings.MaxRoleRemovalSharePercent.

<a id="field-learningrequirementsettings-defaultatriskdays"></a>
## Learning requirement settings / Default at-risk warning

Learning requirement settings / Default at-risk warning. Days before the due date a new requirement starts showing members at risk. Default 14.

Default: 14. Source: LearningRequirementSettings.DefaultAtRiskDays.

<a id="lms-requirements"></a>
## Learning Requirements

Learning Requirements Recurring and one-time training requirements by role, group, certification or shift: courses, trainings, hours per category, learning paths and passed skill evaluations, with due dates from when members join or are given a role. Everyone knows what they owe and by when, and officers see who is at risk before an audit. Every engineer completes the pump operations course every 12 months, and every member logs 24 hours of company training per calendar year. Open Trainings, choose Programs and add a requirement.

<a id="lms-compliance-matrix"></a>
## Compliance Matrix

Compliance Matrix Each member against each requirement: compliant, due later, at risk, overdue, exempt, on leave or not licensed, with CSV and PDF export and a group admin's own group. One page answers who is out of compliance and what is due next. A captain filters the matrix to their station and exempts a member on military leave. Open Trainings, choose Programs, then Compliance.

<a id="lms-learning-paths"></a>
## Learning Paths and Onboarding

Learning Paths and Onboarding Ordered courses, Academy lessons and trainings with prerequisites and electives; finishing a path can issue a certification. A path per role can be assigned automatically when someone joins or is given the role. New members follow the same onboarding with a due date, without anyone remembering to assign it. New dispatchers get the Dispatcher path with 30 days to finish it, and finishing it issues the department's dispatcher certification. Open Trainings, choose Programs, then Paths to build a path and Onboarding to assign it by role.

<a id="lms-recommendations"></a>
## Learning recommendations

Learning recommendations Recommends learning paths and courses to each member from their open requirements, the paths they are on, their competency gaps and the courses offered to their roles. Members see what to take next and why, and officers see the same for the members they supervise. A firefighter with no evidence for the ladder competency sees the ladder operations course recommended under My Training, with the reason. Open Trainings, choose Programs, then Recommendations; members also see Recommended on My Training. Explain with AI needs Enhanced AI. The ranking is the same for everyone with the same requirements and gaps; the optional explanation sends only course titles and reasons, never names.

<a id="module-documentsdisabled"></a>
## Documents availability

Documents availability. Controls availability of shared documents. Disabling this module can remove navigation and block module operations; it does not delete its records. Turning it on still requires the applicable subscription, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.DocumentsDisabled.

<a id="module-documentsnameoverride"></a>
## Documents menu name

Documents menu name. Optional display name for shared documents in consumers that support the override. It changes the label, not permissions or functionality. This release has no separate name-override control in Module Settings.

Default: unset. Source: DepartmentModuleSettings.DocumentsNameOverride.

<a id="module-notesdisabled"></a>
## Notes availability

Notes availability. Controls availability of notes. Disabling this module can remove navigation and block module operations; it does not delete its records. Turning it on still requires the applicable subscription, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.NotesDisabled.

<a id="module-notesnameoverride"></a>
## Notes menu name

Notes menu name. Optional display name for notes in consumers that support the override. It changes the label, not permissions or functionality. This release has no separate name-override control in Module Settings.

Default: unset. Source: DepartmentModuleSettings.NotesNameOverride.

<a id="module-contactsdisabled"></a>
## Contacts availability

Contacts availability. Shows or hides Contacts in the web app's navigation, dashboard widgets and search. Hiding it does not delete data or change permissions.

Default: false. Source: DepartmentModuleSettings.ContactsDisabled.

<a id="setting-protocollibrarysettings"></a>
## Protocol Library Settings

Protocol Library Settings. Department-wide rules on the Protocols Settings page: whether approvals need a passkey and read-and-sign a step-up, the review interval new protocols start with, and how long superseded and retired versions are kept, with a legal hold that stops any deletion. Missing means the standard signature levels, no default interval and every version kept. Only department administrators can change it.

Default: missing = the standard signature levels (approvals by a fresh step-up or a passkey), no default review interval, every version kept. Source: DepartmentSettingTypes.ProtocolLibrarySettings.

<a id="field-protocollibrarysettings-requirepasskeyforapproval"></a>
## Protocol Library Settings / Require Passkey for Approval

Protocol Library Settings / Require Passkey for Approval. Approvals and "reviewed, no change" stamps must be signed with a passkey; a step-up by authenticator code or sign-in provider is no longer enough. Approvers without a registered passkey cannot sign until they add one. Off, any fresh step-up or passkey signs.

Default: false (a fresh step-up or a passkey). Source: ProtocolLibrarySettings.RequirePasskeyForApproval.

<a id="field-protocollibrarysettings-readandsignrequiresstepup"></a>
## Protocol Library Settings / Read-and-Sign Requires Step-Up

Protocol Library Settings / Read-and-Sign Requires Step-Up. Read-and-sign acknowledgments will need a fresh step-up instead of the signed-in session alone. Read-and-sign is not available yet; the choice is saved for when it is.

Default: false (the signed-in session). Source: ProtocolLibrarySettings.ReadAndSignRequiresStepUp.

<a id="field-protocollibrarysettings-defaultreviewintervaldays"></a>
## Protocol Library Settings / Default Review Interval Days

Protocol Library Settings / Default Review Interval Days. Days between reviews for new and imported protocols that do not set their own interval, 0 to 3650; 0 sets none. Existing protocols keep theirs. Publishing a version or recording "reviewed, no change" restarts the interval, and the protocol's owner, or the department administrators when it has none, is reminded 30 and 7 days before a review is due and again when it is overdue.

Default: 0 (no review interval). Source: ProtocolLibrarySettings.DefaultReviewIntervalDays.

<a id="field-protocollibrarysettings-legalhold"></a>
## Protocol Library Settings / Legal Hold

Protocol Library Settings / Legal Hold. A department-wide legal hold: while it is on, no superseded or retired protocol version is deleted, whatever the retention days. An approver can also hold a single protocol from its edit page.

Default: false. Source: ProtocolLibrarySettings.LegalHold.

<a id="field-protocollibrarysettings-defaultcontentlocale"></a>
## Protocol Library Settings / Content Language

Protocol Library Settings / Content Language. The language protocols are written in. Members whose language has no approved translation of a version read it in this language, and block labels such as Caution are written in it. Blank means English.

Default: blank (English). Source: ProtocolLibrarySettings.DefaultContentLocale.

<a id="field-protocollibrarysettings-showdeterminantindispatch"></a>
## Protocol Library Settings / Determinant in dispatch texts

Protocol Library Settings / Determinant in dispatch texts. Adds the determinant and the protocol name of a call's case flow to the dispatch push title, the dispatch text message and the voice dispatch prompt. Off by default; with it off every dispatch text is as before.

Default: false. Source: ProtocolLibrarySettings.ShowDeterminantInDispatch.

<a id="field-protocollibrarysettings-retention"></a>
## Protocol Library Settings / Retention

Protocol Library Settings / Retention. Days each protocol kind (Dispatch, SOG, Clinical Guideline, Policy, Procedure, Reference) keeps a version after it is superseded or retired, up to 36500; after that a background job deletes it. 0 keeps that kind's versions. A version still attached to a call is never deleted, and nothing is deleted while a legal hold is on.

Default: empty (every version kept). Source: ProtocolLibrarySettings.Retention.

<a id="field-protocolkindretention-kind"></a>
## Protocol Kind Retention / Kind

Protocol Kind Retention / Kind. The protocol kind the entry applies to: 0 Dispatch, 1 SOG, 2 Clinical Guideline, 3 Policy, 4 Procedure, 5 Reference. One entry per kind; a kind without an entry keeps every version.

Default: unset. Source: ProtocolKindRetention.Kind.

<a id="field-protocolkindretention-keepdays"></a>
## Protocol Kind Retention / Keep Days

Protocol Kind Retention / Keep Days. Days after a version of that kind is superseded or retired before it may be deleted, 1 to 36500. An entry of 0 is dropped when saved, so the kind keeps its versions.

Default: 0 (keep). Source: ProtocolKindRetention.KeepDays.

<a id="setting-knowledgesettings"></a>
## Knowledge Settings

Knowledge Settings. Department-wide rules for knowledge answers on the Knowledge Settings page: whether the Documents module is used, and the department's dosing tool link. Missing means Documents are not used and there is no dosing link. Only department administrators can change it.

Default: missing = Documents not used, no dosing tool link. Source: DepartmentSettingTypes.KnowledgeSettings.

<a id="field-knowledgesettings-includedocuments"></a>
## Knowledge Settings / Include Documents

Knowledge Settings / Include Documents. Documents join the sources knowledge answers use. Admins-only documents are used only for administrators, and no document is used while Advanced Data Protection is on. Changing it rebuilds the department's knowledge index in the background.

Default: false. Source: KnowledgeSettings.IncludeDocuments.

<a id="field-knowledgesettings-dosingtoolurl"></a>
## Knowledge Settings / Dosing Tool Link

Knowledge Settings / Dosing Tool Link. An https link to the department's dosing tool. Answers that quote a dose show it; doses are only ever quoted from a source, and neither the model nor Resgrid calculates one. Anything but an https link is refused.

Default: unset (no dosing link). Source: KnowledgeSettings.DosingToolUrl.

<a id="contacts"></a>
## Contacts

Contacts Shared contacts such as hospitals, agencies and vendors. Members find the current procedure, preplan or phone number when they need it instead of searching email. A department uploads its standard operating procedures, adds hospital and mutual-aid contacts and creates a medical dispatch protocol. Add the outside contacts members call most.

<a id="documents"></a>
## Documents

Documents Shared documents such as procedures and preplans. Members find the current procedure, preplan or phone number when they need it instead of searching email. A department uploads its standard operating procedures, adds hospital and mutual-aid contacts and creates a medical dispatch protocol. Upload your most-used documents and organize them by category.

<a id="protocols"></a>
## Protocols

Protocols Dispatch protocols with scripted questions and instructions. Members find the current procedure, preplan or phone number when they need it instead of searching email. A department uploads its standard operating procedures, adds hospital and mutual-aid contacts and creates a medical dispatch protocol. Create protocols for the call types that need scripted questions. The library also holds SOGs, clinical guidelines, policies, procedures and references. A change is written as a draft, submitted to the Review Queue, approved with a step-up or passkey signature and published now or on a set date; any two versions can be compared, and a protocol that is still current can be stamped "reviewed, no change". Categories set approval steps, Settings (department administrators) sets signature, review and retention rules, and Import brings protocols in from an exchange file as drafts.

<a id="new-contact"></a>
## New Contact

New Contact Add a person or organization contact. Members find the current procedure, preplan or phone number when they need it instead of searching email. A department uploads its standard operating procedures, adds hospital and mutual-aid contacts and creates a medical dispatch protocol. Gather the documents and contacts members need most, and decide who keeps them current.

<a id="contact-categories"></a>
## Contact Categories

Contact Categories Categories for organizing contacts. Members find the current procedure, preplan or phone number when they need it instead of searching email. A department uploads its standard operating procedures, adds hospital and mutual-aid contacts and creates a medical dispatch protocol. Gather the documents and contacts members need most, and decide who keeps them current.

<a id="new-document"></a>
## Upload Document

Upload Document Upload a document. Members find the current procedure, preplan or phone number when they need it instead of searching email. A department uploads its standard operating procedures, adds hospital and mutual-aid contacts and creates a medical dispatch protocol. Gather the documents and contacts members need most, and decide who keeps them current.

<a id="notes"></a>
## Notes

Notes Short shared notes for the department. Members find the current procedure, preplan or phone number when they need it instead of searching email. A department uploads its standard operating procedures, adds hospital and mutual-aid contacts and creates a medical dispatch protocol. Gather the documents and contacts members need most, and decide who keeps them current.

<a id="new-note"></a>
## New Note

New Note Post a department note. Members find the current procedure, preplan or phone number when they need it instead of searching email. A department uploads its standard operating procedures, adds hospital and mutual-aid contacts and creates a medical dispatch protocol. Gather the documents and contacts members need most, and decide who keeps them current.

<a id="new-protocol"></a>
## New Protocol

New Protocol Create a dispatch protocol. Members find the current procedure, preplan or phone number when they need it instead of searching email. A department uploads its standard operating procedures, adds hospital and mutual-aid contacts and creates a medical dispatch protocol. Gather the documents and contacts members need most, and decide who keeps them current.

<a id="protocol-edit"></a>
## Edit Protocols

Edit Protocols Change a protocol's content, triggers and questions in place; calls keep the text they were dispatched with. Triggers, questions and answers keep their ids when edited, and each call shows the text as dispatched, marked when the protocol has changed since.

<a id="protocol-disable"></a>
## Disable Protocols

Disable Protocols Stop suggesting and attaching a protocol without deleting it. A disabled protocol stays in the library, leaves the apps' lists and offline reference data, and is refused when a call form or app tries to attach it.

<a id="call-protocol-removal"></a>
## Remove a Protocol from a Call

Remove a Protocol from a Call Take a mis-attached protocol off a call with a reason; the record keeps it, marked removed. On View Call, open the Protocols tab and use Remove from call; the removal is written to the department's audit log.

<a id="protocol-review-queue"></a>
## Protocol Review Queue

Protocol Review Queue Approve or reject submitted protocol versions with a signed approval, then publish them now or on a set date. Authors and approvers see the queue. An approver opens a submitted version to read the redline against the version in force; its open review comments must be resolved before it can be approved, and a rejection needs a comment. A category can require several steps, each for a named member or role. Every approval is a step-up or passkey (L2) signature bound to the version's exact content, and an approved version is published as a major or minor version, now or on its effective date.

<a id="protocol-review-stamp"></a>
## Record a Protocol Review

Record a Protocol Review Mark a published protocol "reviewed, no change" with a signed stamp, without a new version. The protocol's owner or an approver records it from the protocol's page. It is signed like an approval, restarts the review interval and its reminders, and members do not have to acknowledge the protocol again.

<a id="protocol-compare"></a>
## Compare Protocol Versions

Compare Protocol Versions See what changed between two versions of a protocol as a redline. Open Compare from the version history on a protocol's page. Members compare the versions they can read; authors and approvers can include drafts and versions in review.

<a id="protocol-categories"></a>
## Protocol Categories

Protocol Categories Group the library into categories and set the approval steps each category needs. Authors see the categories; approvers add, change and delete them. A category with no approval steps needs one approval from anyone with Approve Protocols, and a category still in use cannot be deleted.

<a id="protocol-settings"></a>
## Protocol Settings

Protocol Settings Set signature requirements, the default review interval, version retention and the department-wide legal hold. Department administrators only; saving asks for a recent two-factor step-up and is written to the department's audit log.

<a id="protocol-import"></a>
## Import Protocols

Import Protocols Bring protocols in from a Resgrid protocol exchange file as drafts for review. A file holds up to 500 protocols. Categories, priorities, call types, groups and roles are matched to yours by name, and anything without a match is left out and listed. Nothing is published by an import. Export writes the whole library, or chosen protocols, in the same format.

<a id="protocol-acknowledgments"></a>
## My Acknowledgments

My Acknowledgments Every member sees what they owe: protocols that require acknowledgment, assigned when a version is published and whenever the member joins its audience, due a set number of days later, with priority bulletins first. Reading the version and signing records the member, the time and the exact content. A minor version keeps an earlier signature; a major version asks again. A protocol can be linked to a training whose quiz must be passed.

<a id="protocol-compliance"></a>
## Acknowledgment Compliance

Acknowledgment Compliance The compliance grid lists every protocol in force that needs acknowledgment with its progress, and for one version every assigned member with their state, due date and signature time, filtered by group. Export it as CSV or PDF. Reminders go out at the due date and after, and overdue acknowledgments are escalated to the member's group admin. Standards tags and the proof-of-compliance report come with the LMS add-on.

<a id="dispatch-case-review"></a>
## Dispatch Case Review

Dispatch Case Review Officers sample calls by protocol, version, determinant, dispatcher and dates, score each recorded case question by question and instruction by instruction with a Records QA rubric beside the protocol's case flow, and send the review to the dispatcher. Reports show compliance by protocol, version, dispatcher and month, with CSV.

<a id="my-case-reviews"></a>
## My Case Reviews

My Case Reviews Dispatchers read the reviews of calls they handled, with every question and instruction scored and the reviewer's comments, acknowledge each one once and may reply once; the reviewer is notified of the reply.

<a id="protocol-bulletins"></a>
## Priority Bulletins

Priority Bulletins A priority bulletin carries short content, an audience, an expiry and must-read. It is in force at once, sent by push, and shown as a must-read card at the top of home until each member acknowledges it, never over the dispatch console or a call. An approver countersigns it afterwards or withdraws it, and it leaves the library by itself when it expires. Holders of Approve Protocols issue them.

<a id="protocol-bulk-upload"></a>
## Upload Protocol Documents

Upload Protocol Documents Upload many PDF and Word files, or a zip, into a category; each becomes a draft document-backed protocol whose title and number come from the file name, and zip folders can become categories. The file is the protocol's content with the same review, publish, audience and acknowledgment, and its text is read for search. A document in Documents can also be moved to Protocols, leaving a redirect at its old link.

<a id="protocol-block-editor"></a>
## Structured Protocol Content

Structured Protocol Content New protocol drafts are written in a block editor: headings, paragraphs, ordered steps, caution and warning boxes, "contact medical control" gates, tables, images, video links, attachments, links to other protocols, POIs, inventory items, checklists and trainings, medications, adult/pediatric variant groups and scope-of-practice tags. Every block has a stable anchor that review comments and AI citations point at. Medication doses are shown exactly as authored and never calculated; the department dosing-tool link appears with every medication. An older rich-text draft keeps its editor until an author chooses Convert to blocks; published versions and their hashes never change.

<a id="protocol-step-through"></a>
## Protocol Step-Through on Calls

Protocol Step-Through on Calls A Procedure or Dispatch protocol on a call can be stepped through like a checklist from View Call: each step is marked done, skipped or not applicable, timestamped by the server with who did it, and the log is kept on the call. Incident SOP checklists such as MAYDAY, RIT and hazmat entry are the department's Procedure protocols, started on a call from the same panel. Omni uses the same steps through the v4 API.

<a id="protocol-book"></a>
## Protocol PDF Book, QR Code and Print View

Protocol PDF Book, QR Code and Print View The PDF book holds a table of contents and every protocol in force with its version, effective date and approver, for one category or the whole library, limited to what the member may read. It is sealed when document sealing is configured and otherwise recorded as sealing pending. Each protocol also has a print view and a QR code poster for apparatus and station walls that opens the version in force.

<a id="protocol-translations"></a>
## Protocol Content Translations

Protocol Content Translations An author can translate a submitted or published version into another language; only the wording is translated, while doses, links, attachments and the structure stay as in the source. An approver signs each translation at L2, bound to the translation's own content, and the source version's hash and signatures never change. Members read the approved translation in their language, or the department's content language when there is none, set under Protocols settings.

<a id="protocol-public-pages"></a>
## Public Protocol Pages

Public Protocol Pages Publish a protocol's version in force, or a category as a set, at an opt-in public address with an embed code. Drafts, audience-restricted and licensed content are never shown, and a page can be revoked at any time.

<a id="protocol-template-library"></a>
## Protocol Template Library

Protocol Template Library Clone starter sets written by Resgrid (fire SOGs, search and rescue, industrial hazards, business continuity, law enforcement policies, security post orders, workplace policies and an EMS outline without clinical text) into the library as drafts in your language, with the source, licence and version kept on each version.

<a id="community-exchange"></a>
## Community Exchange

Community Exchange Share protocols and trainings with other departments for free. Department data is taken out, licensed content is refused, Resgrid reviews every contribution before it is listed, and other departments can rate, comment on and import a copy with the attribution kept.

<a id="protocol-open-format"></a>
## Protocol Open Format and Export

Protocol Open Format and Export Export the library or a category in the documented open JSON format (blocks, version history, decision graph, translations and an attachments list), as one HTML file or as the PDF book. Files in the older format still import.

<a id="protocol-analytics"></a>
## Protocol Analytics

Protocol Analytics See how protocols are used on calls by version, the determinant distribution and override rate, time on protocol, acknowledgment compliance and searches that found nothing, with a CSV export.

<a id="forms"></a>
## Forms

Forms Custom forms used on calls and dispatches. Members find the current procedure, preplan or phone number when they need it instead of searching email. A department uploads its standard operating procedures, adds hospital and mutual-aid contacts and creates a medical dispatch protocol. Gather the documents and contacts members need most, and decide who keeps them current.

<a id="shared-links"></a>
## Shared links

Shared links Share selected calls, units and personnel with a linked department. Review exposed data and link access before distribution. A department uploads its standard operating procedures, adds hospital and mutual-aid contacts and creates a medical dispatch protocol. Gather the documents and contacts members need most, and decide who keeps them current.

<a id="file-library"></a>
## Files and attachments

Files and attachments Files attached to documents and records. Keep reference material with its owner, permissions and retention policy. A department uploads its standard operating procedures, adds hospital and mutual-aid contacts and creates a medical dispatch protocol. Gather the documents and contacts members need most, and decide who keeps them current.

<a id="setting-recordsdefaultlifecyclepreset"></a>
## Records Default Lifecycle Preset

Records Default Lifecycle Preset. Default lifecycle for new department-owned definitions. Locked definitions retain their own lifecycle.

Default: owning records default. Source: DepartmentSettingTypes.RecordsDefaultLifecyclePreset.

<a id="setting-recordsreviewduehours"></a>
## Records Review Due Hours

Records Review Due Hours. Default administrative review deadline; a definition-level override takes precedence. This is not a statutory deadline inference.

Default: owning records default. Source: DepartmentSettingTypes.RecordsReviewDueHours.

<a id="setting-recordsnumberingconfig"></a>
## Records Numbering Config

Records Numbering Config. Department numbering for the built-in record types and incident reports: the number pattern, sequence digits, record type prefixes, numbering year start (for records numbered by a fiscal year) and raised next numbers. Numbers are assigned when a record is finalized. Department definitions with their own numbering keep it.

Default: RecordsNumberingConfig constructor. Source: DepartmentSettingTypes.RecordsNumberingConfig.

<a id="setting-recordssearchconfig"></a>
## Records Search Config

Records Search Config. Allowed record search scope and protected-data behavior. Search access remains bounded by current source permissions.

Default: RecordsSearchConfig constructor. Source: DepartmentSettingTypes.RecordsSearchConfig.

<a id="setting-recordsretentionpolicy"></a>
## Records Retention Policy

Records Retention Policy. Default and definition-specific retention, including prior-policy history. Holds and restricted-class rules can prevent purge eligibility.

Default: class defaults, no department override. Source: DepartmentSettingTypes.RecordsRetentionPolicy.

<a id="setting-recordsgroupvisibilitymode"></a>
## Records Group Visibility Mode

Records Group Visibility Mode. Whether record visibility is department-wide or group scoped. Group scope narrows existing permission and never grants access.

Default: 0. Source: DepartmentSettingTypes.RecordsGroupVisibilityMode.

<a id="setting-recordsgroupscopeconfig"></a>
## Records Group Scope Config

Records Group Scope Config. Reserved identifier with no editable behavior in this release. Do not present it as a shipped setting.

Default: unused. Source: DepartmentSettingTypes.RecordsGroupScopeConfig.

<a id="setting-recordsdisclosureconfig"></a>
## Records Disclosure Config

Records Disclosure Config. Disclosure review clock, redaction profile and release approver. Review local obligations through the authorized disclosure workflow.

Default: RecordsDisclosureConfig constructor. Source: DepartmentSettingTypes.RecordsDisclosureConfig.

<a id="setting-recordsnerisworkflowsenabled"></a>
## Records NERIS Workflows Enabled

Records NERIS Workflows Enabled. Turns this department's NERIS validation, submission and NERIS setup screens on or off. Other Records settings, records and history are unchanged; the NERIS profile and queued submissions are kept for re-enabling.

Default: true. Source: DepartmentSettingTypes.RecordsNerisWorkflowsEnabled.

<a id="module-logsdisabled"></a>
## Logs availability

Logs availability. Controls availability of legacy logs. Disabling this module can remove navigation and block module operations; it does not delete its records. Turning it on still requires the applicable subscription, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.LogsDisabled.

<a id="module-logsnameoverride"></a>
## Logs menu name

Logs menu name. Optional display name for legacy logs in consumers that support the override. It changes the label, not permissions or functionality. This release has no separate name-override control in Module Settings.

Default: unset. Source: DepartmentModuleSettings.LogsNameOverride.

<a id="module-recordsdisabled"></a>
## Records availability

Records availability. Shows or hides Records in the web app's navigation, dashboard widgets and search. Hiding it does not delete data or change permissions. Logs have their own switch.

Default: false. Source: DepartmentModuleSettings.RecordsDisabled.

<a id="module-reportsdisabled"></a>
## Reports availability

Reports availability. Controls availability of reports. Disabling this module can remove navigation and block module operations; it does not delete its records. Turning it on still requires the applicable subscription, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.ReportsDisabled.

<a id="module-reportsnameoverride"></a>
## Reports menu name

Reports menu name. Optional display name for reports in consumers that support the override. It changes the label, not permissions or functionality. This release has no separate name-override control in Module Settings.

Default: unset. Source: DepartmentModuleSettings.ReportsNameOverride.

<a id="field-recordsnumberingconfig-numberassignment"></a>
## Records Numbering Config / Number Assignment

Records Numbering Config / Number Assignment. Choose when numbers are assigned for definitions using department defaults. Review existing references before changing numbering behavior.

Default: OnFinalize. Source: RecordsNumberingConfig.NumberAssignment.

<a id="field-recordsnumberingconfig-resetyearly"></a>
## Records Numbering Config / Reset Yearly

Records Numbering Config / Reset Yearly. Restart record sequences each numbering year in the department time zone: the calendar year unless the department set a fiscal year start (YearStartMonth and YearStartDay).

Default: true. Source: RecordsNumberingConfig.ResetYearly.

<a id="field-recordsnumberingconfig-sequencewidth"></a>
## Records Numbering Config / Sequence Width

Records Numbering Config / Sequence Width. Digits in the SEQ part of the number pattern, zero-padded: 4 writes 0184. 3 to 8 digits; changing it keeps each sequence's next number. Department definitions with their own numbering keep their own width.

Default: 4. Source: RecordsNumberingConfig.SequenceWidth.

<a id="field-recordsnumberingconfig-includeyear"></a>
## Records Numbering Config / Include Year

Records Numbering Config / Include Year. Include the year between the record prefix and sequence number.

Default: true. Source: RecordsNumberingConfig.IncludeYear.

<a id="field-recordsnumberingconfig-pergroupsequence"></a>
## Records Numbering Config / Per Group Sequence

Records Numbering Config / Per Group Sequence. Use separate station/group sequences. This changes numbering, not record visibility.

Default: false. Source: RecordsNumberingConfig.PerGroupSequence.

<a id="field-recordsnumberingconfig-floors"></a>
## Records Numbering Config / Floors

Records Numbering Config / Floors. Raised next numbers, one per record-number sequence, for departments continuing numbers issued before Resgrid. A next number only rises, and numbers already issued always win, so a raised next number never causes a duplicate.

Default: empty. Source: RecordsNumberingConfig.Floors.

<a id="field-recordsnumberingconfig-pattern"></a>
## Records Numbering Config / Pattern

Records Numbering Config / Pattern. How record numbers are written for the built-in record types and incident reports, for example RUN-0153: fixed text plus the PREFIX, YYYY or YY, GROUP and SEQ tokens. PREFIX writes each record type's prefix: its default or the department's own, from "Record type prefixes". Changing the pattern never renumbers records already numbered. Until a pattern is saved, the screen shows the one the department has always used.

Default: unset. Source: RecordsNumberingConfig.Pattern.

<a id="field-recordsnumberingconfig-prefixes"></a>
## Records Numbering Config / Prefixes

Records Numbering Config / Prefixes. The department's own prefix for each built-in record type and for incident reports, written by the PREFIX token in place of the default (RUN, TRN, INC and so on). A type with no prefix of its own uses its default. A new prefix starts its own sequence; numbers already issued keep their prefix, and types given the same prefix share one sequence.

Default: empty. Source: RecordsNumberingConfig.Prefixes.

<a id="field-recordsnumberingconfig-documentpatterns"></a>
## Records Numbering Config / Document Patterns

Records Numbering Config / Document Patterns. The department's own patterns for records request (PRR-), occupancy (OCC-), inspection (INSP-), permit (PRM-), investigation (INV-) and evidence (EV-) numbers, edited under Other record numbers on Records Settings. They follow this setting's numbering year start. A kind with no entry keeps its built-in {KIND}-{year}-{sequence:0000} numbers; a custom pattern's sequences are counted in DocumentNumberSequences and carry on after any number already issued in the same text.

Default: empty. Source: RecordsNumberingConfig.DocumentPatterns.

<a id="field-recordsnumberingconfig-yearstartmonth"></a>
## Records Numbering Config / Year Start Month

Records Numbering Config / Year Start Month. Month the numbering year starts (1 to 12), for a department whose records follow a fiscal year, its own or a county's or state's it reports to. Sequences whose pattern includes {YYYY} or {YY}, and department definitions that reset yearly, start again at 1 on that month and day in the department's local time, and the year tokens write the fiscal year's name. A record's year is the local date it started (an incident report's, its call's). Unset (0) is January, the calendar year. Separate from call numbering's year start. Records request numbers (PRR-year-sequence) and prevention numbers (inspections, permits, occupancies, investigations and evidence, KIND-year-sequence) use the same numbering year.

Default: 0 (January). Source: RecordsNumberingConfig.YearStartMonth.

<a id="field-recordsnumberingconfig-yearstartday"></a>
## Records Numbering Config / Year Start Day

Records Numbering Config / Year Start Day. Day of the month the numbering year starts, from 1 to that month's last day; February 29 is refused so the year starts on the same date every year. Unset (0) is the 1st.

Default: 0 (the 1st). Source: RecordsNumberingConfig.YearStartDay.

<a id="field-recordsnumberingconfig-yearlabel"></a>
## Records Numbering Config / Year Label

Records Numbering Config / Year Label. Which year {YYYY} and {YY} write for a numbering year that does not start on January 1: 0, the year it ends in (the usual fiscal-year convention, so a year starting November 1, 2026 is 2027), or 1, the year it starts in. Ignored for a January 1 start.

Default: 0 (the year it ends in). Source: RecordsNumberingConfig.YearLabel.

<a id="field-recordsnumberingfloor-nextsequence"></a>
## Records Numbering Floor / Next Sequence

Records Numbering Floor / Next Sequence. The lowest sequence number that sequence may issue next.

Default: 1. Source: RecordsNumberingFloor.NextSequence.

<a id="field-recordsnumberingfloor-scopekey"></a>
## Records Numbering Floor / Scope Key

Records Numbering Floor / Scope Key. The sequence a raised next number applies to: the text the pattern renders around the sequence, with # in its place.

Default: unset. Source: RecordsNumberingFloor.ScopeKey.

<a id="field-recordsnumberingfloor-setbyuserid"></a>
## Records Numbering Floor / Set By User ID

Records Numbering Floor / Set By User ID. The administrator who raised the next number.

Default: unset. Source: RecordsNumberingFloor.SetByUserId.

<a id="field-recordsnumberingfloor-seton"></a>
## Records Numbering Floor / Set On

Records Numbering Floor / Set On. When the next number was raised (UTC).

Default: unset. Source: RecordsNumberingFloor.SetOn.

<a id="field-recordsnumberingprefix-definitionkey"></a>
## Records Numbering Prefix / Definition Key

Records Numbering Prefix / Definition Key. The built-in record type the prefix applies to, such as system.run or system.neris-incident.

Default: unset. Source: RecordsNumberingPrefix.DefinitionKey.

<a id="field-recordsnumberingprefix-prefix"></a>
## Records Numbering Prefix / Prefix

Records Numbering Prefix / Prefix. 2 to 6 upper-case letters (A-Z) or digits. A prefix that no longer validates falls back to the type's default.

Default: unset. Source: RecordsNumberingPrefix.Prefix.

<a id="field-recordssearchconfig-indexnarrative"></a>
## Records Search Config / Index Narrative

Records Search Config / Index Narrative. Include unprotected narrative in search. Advanced Data Protection enrollment withdraws narrative indexing; this preference cannot override protection.

Default: true. Source: RecordsSearchConfig.IndexNarrative.

<a id="field-recordssearchconfig-includelegacyhistory"></a>
## Records Search Config / Include Legacy History

Records Search Config / Include Legacy History. Include supported legacy personnel and unit logs in the Records search scope. Source permissions still apply.

Default: true. Source: RecordsSearchConfig.IncludeLegacyHistory.

<a id="field-recordsretentionpolicy-departmentdefaultyears"></a>
## Records Retention Policy / Department Default Years

Records Retention Policy / Department Default Years. Department retention for standard record classes. Zero means permanent; protected classes, prior policy and holds can retain records longer.

Default: system class default. Source: RecordsRetentionPolicy.DepartmentDefaultYears.

<a id="field-recordsretentionpolicy-overrides"></a>
## Records Retention Policy / Overrides

Records Retention Policy / Overrides. Definition-specific prospective retention overrides. Use the Records workflow and its confirmations; Admin Assist never purges records.

Default: empty. Source: RecordsRetentionPolicy.Overrides.

<a id="field-recordsretentionpolicy-lastchangedbyuserid"></a>
## Records Retention Policy / Last Changed By User ID

Records Retention Policy / Last Changed By User ID. Recorded policy-change actor. It is not an editable retention rule and is not exposed as raw identity in this reference.

Default: unset. Source: RecordsRetentionPolicy.LastChangedByUserId.

<a id="field-recordsretentionpolicy-lastchangedon"></a>
## Records Retention Policy / Last Changed On

Records Retention Policy / Last Changed On. Policy effective timestamp used with prior policy versions. It is not the record's retention expiry date.

Default: unset. Source: RecordsRetentionPolicy.LastChangedOn.

<a id="field-recordsretentionpolicy-history"></a>
## Records Retention Policy / History

Records Retention Policy / History. Prior policy versions retained by the owning lifecycle so current changes do not silently rewrite historical retention.

Default: empty. Source: RecordsRetentionPolicy.History.

<a id="field-recordsretentionoverride-definitionkey"></a>
## Records Retention Override / Definition Key

Records Retention Override / Definition Key. Stable record-definition key to which this override applies. Display names do not identify a retention policy.

Default: unset. Source: RecordsRetentionOverride.DefinitionKey.

<a id="field-recordsretentionoverride-retentionyears"></a>
## Records Retention Override / Retention Years

Records Retention Override / Retention Years. Years retained under this definition override; zero means permanent. Holds and prospective policy resolution still apply.

Default: 0. Source: RecordsRetentionOverride.RetentionYears.

<a id="field-recordsretentionoverride-appliesfrom"></a>
## Records Retention Override / Applies From

Records Retention Override / Applies From. Prospective boundary for revisions eligible for the override, evaluated by the Records lifecycle.

Default: unset. Source: RecordsRetentionOverride.AppliesFrom.

<a id="field-recordsretentionpolicyversion-effectiveon"></a>
## Records Retention Policy Version / Effective On

Records Retention Policy Version / Effective On. Effective timestamp of a stored historical retention policy version.

Default: unset. Source: RecordsRetentionPolicyVersion.EffectiveOn.

<a id="field-recordsretentionpolicyversion-policy"></a>
## Records Retention Policy Version / Policy

Records Retention Policy Version / Policy. Historical policy snapshot used to resolve retention for older revisions. Never edit this as an ordinary preference.

Default: unset. Source: RecordsRetentionPolicyVersion.Policy.

<a id="field-recordsdisclosureconfig-statutoryclockdays"></a>
## Records Disclosure Config / Statutory Clock Days

Records Disclosure Config / Statutory Clock Days. Configured disclosure review clock. Verify local obligations with the responsible owner; the default does not establish a legal deadline.

Default: 10. Source: RecordsDisclosureConfig.StatutoryClockDays.

<a id="field-recordsdisclosureconfig-defaultredactionprofile"></a>
## Records Disclosure Config / Default Redaction Profile

Records Disclosure Config / Default Redaction Profile. Default redaction profile in the owning disclosure workflow. Review the actual proposed release before approval.

Default: unset. Source: RecordsDisclosureConfig.DefaultRedactionProfile.

<a id="field-recordsdisclosureconfig-releaseapproveruserid"></a>
## Records Disclosure Config / Release Approver User ID

Records Disclosure Config / Release Approver User ID. Authorized member responsible for disclosure release approval. Selecting an approver does not grant missing permissions or protected-data access.

Default: unset. Source: RecordsDisclosureConfig.ReleaseApproverUserId.

<a id="logs"></a>
## Logs

Logs Activity logs for calls, training and work. Keep incident and activity records with the calls they came from, ready for review and reporting. After each call, the officer completes an incident report from the call and a supervisor reviews it. Choose the log types members complete.

<a id="records"></a>
## Records

Records Incident and activity records. Keep incident and activity records with the calls they came from, ready for review and reporting. After each call, the officer completes an incident report from the call and a supervisor reviews it. Turn on Records if it is available to you and review the record settings.

<a id="incident-reports"></a>
## Incident reports

Incident reports Incident reports linked to calls. Review source completeness before finalization or external submission. After each call, the officer completes an incident report from the call and a supervisor reviews it. Decide who completes and who reviews each report.

<a id="record-definitions"></a>
## Record definitions and templates

Record definitions and templates The forms and fields records use. Fit reporting to approved local procedures while preserving built-in definitions. After each call, the officer completes an incident report from the call and a supervisor reviews it. Start from the built-in forms and add only the fields you need.

<a id="reports"></a>
## Reports

Reports Department reports on calls, personnel and activity. Keep incident and activity records with the calls they came from, ready for review and reporting. After each call, the officer completes an incident report from the call and a supervisor reviews it. Run the reports you use for meetings or compliance.

<a id="new-log"></a>
## New Log

New Log Record a run report, training log or work log. Keep incident and activity records with the calls they came from, ready for review and reporting. After each call, the officer completes an incident report from the call and a supervisor reviews it. Know which reports your department completes and who reviews them. Records may need to be enabled for your department.

<a id="records-dashboard"></a>
## Records Dashboard

Records Dashboard Records that are due, awaiting review or ready to submit. Keep incident and activity records with the calls they came from, ready for review and reporting. After each call, the officer completes an incident report from the call and a supervisor reviews it. Know which reports your department completes and who reviews them. Records may need to be enabled for your department.

<a id="records-settings"></a>
## Records Settings

Records Settings Numbering (including records request and prevention numbers and the numbering year start), retention, search and visibility rules for records. Keep incident and activity records with the calls they came from, ready for review and reporting. After each call, the officer completes an incident report from the call and a supervisor reviews it. Know which reports your department completes and who reviews them. Records may need to be enabled for your department.

<a id="record-occupancies"></a>
## Occupancies and preplans

Occupancies and preplans Buildings and sites with their contacts, hazards and preplans. Keep site knowledge owned and current; recorded hazards do not certify safety. After each call, the officer completes an incident report from the call and a supervisor reviews it. Know which reports your department completes and who reviews them. Records may need to be enabled for your department.

<a id="record-inspections"></a>
## Inspections

Inspections Schedule inspections and track violations and corrections. Track administrative follow-up against configured inspection procedures. After each call, the officer completes an incident report from the call and a supervisor reviews it. Know which reports your department completes and who reviews them. Records may need to be enabled for your department.

<a id="record-hydrants"></a>
## Hydrants and water sources

Hydrants and water sources Hydrants and water sources with their inspection and flow history. Identify records needing verification without claiming current flow or availability. After each call, the officer completes an incident report from the call and a supervisor reviews it. Know which reports your department completes and who reviews them. Records may need to be enabled for your department.

<a id="record-permits"></a>
## Permits

Permits Permits and their approval status. Assign review responsibility; a software state is not a hazardous-work safety clearance. After each call, the officer completes an incident report from the call and a supervisor reviews it. Know which reports your department completes and who reviews them. Records may need to be enabled for your department.

<a id="record-investigations"></a>
## Investigations

Investigations Restricted case records, such as fire investigations. Keep case responsibility and access separate from general department visibility. After each call, the officer completes an incident report from the call and a supervisor reviews it. Know which reports your department completes and who reviews them. Records may need to be enabled for your department.

<a id="record-evidence"></a>
## Evidence capture and provenance

Evidence capture and provenance Collect and track evidence attached to a record. Preserve evidence context and access restrictions without copying it into setup guidance. After each call, the officer completes an incident report from the call and a supervisor reviews it. Know which reports your department completes and who reviews them. Records may need to be enabled for your department.

<a id="record-holds"></a>
## Legal holds

Legal holds Legal holds that stop records from being deleted. Retain affected evidence while a hold remains active. After each call, the officer completes an incident report from the call and a supervisor reviews it. Know which reports your department completes and who reviews them. Records may need to be enabled for your department.

<a id="record-submissions"></a>
## Reporting submissions

Reporting submissions Submit incident reports to state or national reporting systems. Review destination requirements and actual submission outcomes. After each call, the officer completes an incident report from the call and a supervisor reviews it. Know which reports your department completes and who reviews them. Records may need to be enabled for your department.

<a id="record-analytics"></a>
## Records analytics

Records analytics Response, workload and readiness trends from your records. Use measured source data with its scope and completeness limits. After each call, the officer completes an incident report from the call and a supervisor reviews it. Know which reports your department completes and who reviews them. Records may need to be enabled for your department.

<a id="record-saved-reports"></a>
## Saved record reports

Saved record reports Saved record reports you can run again. Make repeatable administrative reviews easier. After each call, the officer completes an incident report from the call and a supervisor reviews it. Know which reports your department completes and who reviews them. Records may need to be enabled for your department.

<a id="record-exports"></a>
## Record exports and templates

Record exports and templates Export records to files using templates. Keep release destinations, protected access and output handling explicit. After each call, the officer completes an incident report from the call and a supervisor reviews it. Know which reports your department completes and who reviews them. Records may need to be enabled for your department.

<a id="record-disclosures"></a>
## Disclosure requests

Disclosure requests Requests to release records, with redaction and approval. Assign review ownership and verify the material approved for release. After each call, the officer completes an incident report from the call and a supervisor reviews it. Know which reports your department completes and who reviews them. Records may need to be enabled for your department.

<a id="module-checklistsdisabled"></a>
## Checklists availability

Checklists availability. Controls availability of checklists. Disabling this module can remove navigation and block module operations; it does not delete its records. Turning it on still requires the applicable subscription, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.ChecklistsDisabled.

<a id="checklist-templates"></a>
## Checklist Templates

Checklist Templates Ready-made checklists for common checks. Recorded checks show what was inspected and what was missed. A daily apparatus check is scheduled for each engine, and the weekly compliance report shows any missed checks. Start from a template or build a checklist for each apparatus.

<a id="checklists"></a>
## Checklists

Checklists Checklists members complete. Recorded checks show what was inspected and what was missed. A daily apparatus check is scheduled for each engine, and the weekly compliance report shows any missed checks. Create the checklists members complete on paper today.

<a id="checklist-schedules"></a>
## Checklist schedules and due work

Checklist schedules and due work Recurring schedules and due checks. Make recorded equipment and procedure checks visible to their owners. A daily apparatus check is scheduled for each engine, and the weekly compliance report shows any missed checks. Schedule each checklist and choose who completes it.

<a id="new-checklist"></a>
## New Checklist

New Checklist Create a checklist. Recorded checks show what was inspected and what was missed. A daily apparatus check is scheduled for each engine, and the weekly compliance report shows any missed checks. Pick the checks you do on paper today and who completes them.

<a id="checklist-reports"></a>
## Checklist compliance reports

Checklist compliance reports Completed, skipped and overdue checks over time. Find missing recorded checks without treating a report as safety certification. A daily apparatus check is scheduled for each engine, and the weekly compliance report shows any missed checks. Pick the checks you do on paper today and who completes them.

<a id="module-inventorydisabled"></a>
## Inventory availability

Inventory availability. Controls availability of inventory. Disabling this module can remove navigation and block module operations; it does not delete its records. Turning it on still requires the applicable subscription, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.InventoryDisabled.

<a id="module-inventorynameoverride"></a>
## Inventory menu name

Inventory menu name. Optional display name for inventory in consumers that support the override. It changes the label, not permissions or functionality. This release has no separate name-override control in Module Settings.

Default: unset. Source: DepartmentModuleSettings.InventoryNameOverride.

<a id="inventory"></a>
## Inventory

Inventory Items, stock levels and storage locations. Know what you have, where it is and what is about to expire. Medical supplies are tracked by lot with expiry alerts, and turnout gear is issued to members. Add the supplies and equipment you track and where they are stored.

<a id="inventory-operations"></a>
## Inventory counts, expiry and alerts

Inventory counts, expiry and alerts Counts, expiry dates and low-stock alerts. Identify inventory data and supplies that need owner follow-up. Medical supplies are tracked by lot with expiry alerts, and turnout gear is issued to members. Set minimum levels and expiry alerts for critical supplies.

<a id="inventory-status"></a>
## Inventory Status

Inventory Status What is on hand, running low or expiring. Know what you have, where it is and what is about to expire. Medical supplies are tracked by lot with expiry alerts, and turnout gear is issued to members. Decide which supplies and equipment are worth tracking and where they are stored.

<a id="inventory-transfer"></a>
## Transfer Inventory

Transfer Inventory Move stock between locations. Know what you have, where it is and what is about to expire. Medical supplies are tracked by lot with expiry alerts, and turnout gear is issued to members. Decide which supplies and equipment are worth tracking and where they are stored.

<a id="inventory-issue"></a>
## Issue Equipment

Issue Equipment Issue equipment to members and record returns. Know what you have, where it is and what is about to expire. Medical supplies are tracked by lot with expiry alerts, and turnout gear is issued to members. Decide which supplies and equipment are worth tracking and where they are stored.

<a id="inventory-purchasing"></a>
## Inventory purchasing

Inventory purchasing Suppliers, purchase orders and deliveries. Connect replenishment to recorded stock needs and authorized receipt. Medical supplies are tracked by lot with expiry alerts, and turnout gear is issued to members. Decide which supplies and equipment are worth tracking and where they are stored.

<a id="deployments"></a>
## Deployment Finance

Deployment Finance Deployment records and Deployment Finance. Keep deployment records and cost documentation together for review and reimbursement. A strike-team deployment is recorded with its personnel, apparatus and dates. Record your next deployment with its personnel and apparatus.

<a id="new-deployment"></a>
## New Deployment

New Deployment Start a deployment record. Keep deployment records and cost documentation together for review and reimbursement. A strike-team deployment is recorded with its personnel, apparatus and dates. Useful when your members deploy to incidents outside your jurisdiction.

<a id="deployment-from-external-order"></a>
## Deployment From External Order

Deployment From External Order Create a deployment from a mutual-aid resource order. Keep deployment records and cost documentation together for review and reimbursement. A strike-team deployment is recorded with its personnel, apparatus and dates. Useful when your members deploy to incidents outside your jurisdiction.

<a id="deployment-partner-acceptance"></a>
## Partner Acceptance Of Deployments

Partner Acceptance Of Deployments When a deployment order is filled for a linked organization on Resgrid, name it as the partner: each fill you accept asks its administrators to accept the assignment with a step-up signature over the link, and the deployment shows who accepted it and when.

<a id="workflows"></a>
## Workflows

Workflows Automations triggered by department events. Automate repeated administrative steps and connect Resgrid to the other systems you use. A workflow posts new calls to a team channel, and a custom field records the fire district on each call. Start with one workflow for a task you repeat, and review its runs.

<a id="user-defined-fields"></a>
## User-defined fields

User-defined fields Custom fields on calls, personnel and other records. Collect the information the department actually needs with appropriate classification. A workflow posts new calls to a team channel, and a custom field records the fire district on each call. Add only the fields you must capture.

<a id="api-mcp"></a>
## API and MCP integrations

API and MCP integrations API and MCP access for other systems. Connect approved systems without placing credentials in setup examples. A workflow posts new calls to a team channel, and a custom field records the fire district on each call. Create credentials for each integration and limit what they can access.

<a id="new-workflow"></a>
## New Workflow

New Workflow Create an automation workflow. Automate repeated administrative steps and connect Resgrid to the other systems you use. A workflow posts new calls to a team channel, and a custom field records the fire district on each call. Have the destination systems and credentials ready, and decide who owns each automation.

<a id="workflow-runs"></a>
## Workflow Runs

Workflow Runs The history of workflow runs, including failures. Automate repeated administrative steps and connect Resgrid to the other systems you use. A workflow posts new calls to a team channel, and a custom field records the fire district on each call. Have the destination systems and credentials ready, and decide who owns each automation.

<a id="import-migration"></a>
## Import & Migration

Import & Migration Bring people, stations, apparatus and settings into Resgrid from spreadsheets or the system you are leaving. Start a workspace, download the Resgrid starter workbook or upload your own export, match columns and values, and fix the issues the checks find. Nothing is saved until you review and approve an import; every import is journaled and can be rolled back for 30 days. Imported people stay dormant (they cannot sign in and receive nothing) until you invite them from Verify & activate, and Admin Assist then verifies the result in Setup Report.

<a id="setting-departmentimportconfig"></a>
## Import & Migration settings

Import & Migration settings How long uploaded files and checked data are kept and how imports are approved. Uploaded files are kept 7 days after a successful import (72 hours with Advanced Data Protection) and 30 days after a failure; checked data is kept 30 days after go-live because rollback needs it. A rollback is possible until go-live plus 30 days for a new department, and for 30 days after the import in a live department. Require a second administrator to have every import approved by two different people. Add only never changes existing records; Review updates changes only the fields approved in the preview.

Default: missing = plan defaults. Source: DepartmentSettingTypes.DepartmentImportConfig.

<a id="import-dormant-members"></a>
## Dormant imported members

Dormant imported members People created by an import are dormant: they cannot sign in, receive no dispatches, messages or reminders, and are left out of every notification sweep until you invite them. Already-expired certifications are imported as expired so nobody is emailed, and reminders for certifications expiring soon wait until the holder is invited. Invite people from Verify & activate; invitations go out in small batches outside night hours, and accepting one lets the person choose a password. People who already have a Resgrid account in another department get an invitation to join instead.

<a id="import-support-access"></a>
## Support access for imports

Support access for imports Resgrid support sees nothing in an import workspace unless an administrator grants access, for a limited time, at one of four levels: Observe (progress, counts and issue codes), Inspect (also sample values to diagnose a problem), Prepare (also fix mappings and values as drafts the department approves) and Operate (staff run the import, with two-person control, only under an agreed managed migration). Every staff action is logged on the workspace timeline, and access can be revoked at any time.

<a id="module-maintenancedisabled"></a>
## Maintenance availability

Maintenance availability. Controls availability of maintenance and work orders. Disabling this module can remove navigation and block module operations; it does not delete its records. Turning it on still requires the applicable subscription, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.MaintenanceDisabled.

<a id="module-maintenancenameoverride"></a>
## Maintenance menu name

Maintenance menu name. Optional display name for maintenance and work orders in consumers that support the override. It changes the label, not permissions or functionality. This release has no separate name-override control in Module Settings.

Default: unset. Source: DepartmentModuleSettings.MaintenanceNameOverride.

<a id="work-orders"></a>
## Work Orders

Work Orders Repair and maintenance work orders. Turn a failed check or reported defect into a tracked repair and a documented return to service. A failed pump test creates a work order, and the engine stays on a safety hold until the repair is approved. Create work orders from failed checks or reported defects.

<a id="preventive-maintenance"></a>
## Preventive maintenance

Preventive maintenance Recurring maintenance schedules. Plan equipment upkeep and track corrective responsibility. A failed pump test creates a work order, and the engine stays on a safety hold until the repair is approved. Schedule routine service for each apparatus.

<a id="maintenance-policy"></a>
## Maintenance policies and approvals

Maintenance policies and approvals Repair approvals and safety holds. Make authority and repair completion requirements explicit. A failed pump test creates a work order, and the engine stays on a safety hold until the repair is approved. Decide who approves repairs and returns equipment to service.

<a id="new-work-order"></a>
## New Work Order

New Work Order Open a work order. Turn a failed check or reported defect into a tracked repair and a documented return to service. A failed pump test creates a work order, and the engine stays on a safety hold until the repair is approved. Requires the Readiness Pro add-on. Checklists remain available without it.

<a id="maintenance-reports"></a>
## Maintenance history and reports

Maintenance history and reports Maintenance history, repair costs and safety holds. Support accountable return-to-service review through the owning workflow. A failed pump test creates a work order, and the engine stays on a safety hold until the repair is approved. Requires the Readiness Pro add-on. Checklists remain available without it.

<a id="addon-readiness"></a>
## Readiness Pro

Readiness Pro The Readiness Pro add-on: work orders, preventive maintenance, repair approvals and safety holds. Give equipment defects an owner and a recorded repair and return-to-service review. A failed apparatus check leads to an explicitly created maintenance work order. Checklists do not require this add-on. Maintenance needs its module, permissions and active entitlement. Historical evidence and hold release follow owning-module rules.

<a id="module-businessoperationsdisabled"></a>
## Business Ops availability

Business Ops availability. Controls availability of Business Ops. Disabling this module can remove navigation and block module operations; it does not delete its records. Turning it on still requires the applicable subscription, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.BusinessOperationsDisabled.

<a id="module-deploymentsdisabled"></a>
## Deployments availability

Deployments availability. Shows or hides Deployments in the web app's navigation, dashboard widgets and search. Hiding it does not delete data or change permissions.

Default: false. Source: DepartmentModuleSettings.DeploymentsDisabled.

<a id="invoices"></a>
## Invoices

Invoices Customer invoices and payments. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Set up billing settings and rate cards, then create invoices.

<a id="contracts"></a>
## Contracts

Contracts Contracts, bids and rate schedules. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Add your service contracts and their rates.

<a id="cal-oes-mars"></a>
## Cal OES MARS

Cal OES MARS Cal OES MARS reimbursement claims. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Enter your annual rates, then work the MARS action queue after deployments.

<a id="workforce"></a>
## Labor & Costing

Labor & Costing Compensation and field cost runs. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Add compensation profiles, then run field costs for deployments.

<a id="new-invoice"></a>
## New Invoice

New Invoice Create an invoice for a customer. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="rate-cards"></a>
## Rate Cards

Rate Cards Rates for units, personnel and fees used on invoices. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="invoice-aging"></a>
## Accounts Receivable Aging

Accounts Receivable Aging Unpaid invoices grouped by how overdue they are. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="billing-settings"></a>
## Billing Settings

Billing Settings The legal name, remit-to address and tax details printed on invoices. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="online-payment-settings"></a>
## Online Payment Settings

Online Payment Settings Connect your Stripe account to take invoice payments online. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="bids"></a>
## Bids

Bids Priced estimates for customers. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="new-bid"></a>
## New Bid

New Bid Create a bid for a customer. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="new-contract"></a>
## New Contract

New Contract Create a service contract for a customer. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="compliance-documents"></a>
## Compliance Documents

Compliance Documents Insurance, licences and bonds, with expiry reminders. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="rate-schedules"></a>
## Rate Schedules

Rate Schedules Contractor rates for crews, vehicles and equipment. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="cal-oes-mars-queue"></a>
## MARS Action Queue

MARS Action Queue MARS claims waiting to be prepared and submitted. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="cal-oes-mars-rates"></a>
## MARS Annual Rates

MARS Annual Rates Annual MARS salary and equipment rates. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="cal-oes-mars-reconciliation"></a>
## MARS Reconciliation

MARS Reconciliation Match MARS payments to the claims you submitted. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="workforce-compensation"></a>
## Compensation Profiles

Compensation Profiles Pay rates and employer costs for staff. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="workforce-annual-facts"></a>
## Annual Pay Facts

Annual Pay Facts Annual earnings and hours used for pay data reporting. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="resource-costs"></a>
## Resource Cost Profiles

Resource Cost Profiles Running costs for units and equipment. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="cost-runs"></a>
## Field Cost Runs

Field Cost Runs Work out the full cost of a bid, call or deployment. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="pay-data-reporting"></a>
## California Pay Data Reporting

California Pay Data Reporting Prepare California pay data reports. Also requires Advanced Data Protection. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="my-demographics"></a>
## My Demographic Response

My Demographic Response Your voluntary answers for California pay data reporting. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Business Ops add-on. Online payments also need your own payment-provider account.

<a id="addon-business"></a>
## Business Ops

Business Ops The Business Ops add-on: invoicing, payments, contracts, bids, MARS reimbursement and labor costing. Connect administrative commercial work to the department activities it supports. Prepare an event standby invoice or review documented mutual-aid costs. Certifications and Deployment Finance do not require this add-on. Pay-data reporting additionally needs ADP Enabled; online payments require provider setup. No reimbursement or savings are guaranteed.

<a id="voice"></a>
## Voice

Voice Push-to-Talk voice channels. Coordinate field teams through app-based voice channels. A search team uses its team channel during a training exercise. Create channels and add the members who use them.

<a id="addon-ptt"></a>
## Push-to-Talk

Push-to-Talk The Push-to-Talk add-on: voice channels in the Resgrid apps. Coordinate field teams through configured voice channels. A SAR coordinator uses a team channel during a training exercise. Review seats, supported clients and channel setup. PTT does not provide phone dispatch alerts or certify radio replacement.

<a id="data-protection"></a>
## Data Protection

Data Protection Protection enrollment, key recovery and access controls. Protects sensitive incident and personnel content, with controlled disclosure and key recovery. A co-response team enrolls so client-related notes are protected in every module that stores them. Review the enrollment checklist and plan key recovery before enrolling.

<a id="protected-workflows"></a>
## Protected workflows

Protected workflows Send protected data to approved outside systems through reviewed workflows. Keep protected-data egress scoped, reviewed and auditable. A co-response team enrolls so client-related notes are protected in every module that stores them. Requires the Advanced Data Protection add-on. Enrollment is a separate step: plan two-factor sign-in and key recovery first, and review the effects on search, exports and integrations.

<a id="addon-adp"></a>
## Advanced Data Protection

Advanced Data Protection The Advanced Data Protection add-on: extra encryption and controlled access for sensitive data. Control protected content disclosure and recovery through the department protection lifecycle. An administrator enrolls the department and verifies its recovery arrangements. Buying does not enroll the department. Review MFA, recovery, migration and integration behavior; this is not a compliance certification.

<a id="addon-ai"></a>
## Enhanced AI

Enhanced AI AI assistance beyond the free Admin Assist allowance. Help explain approved evidence and prepare drafts for human review when released. Ask for an explanation of a configuration finding when the conversational feature is available. Nothing to set up yet; your free Admin Assist allowance works without it.

<a id="ai-intake"></a>
## Enhanced AI call intake

Enhanced AI call intake Enhanced AI reads each message from an intake channel set to Enhanced AI processing and fills in the pending call's type, address, priority and a summary. Dispatchers start from a filled-in call instead of retyping free text. The pending call is saved first, so no message is lost when AI is unavailable. A text saying a car is in the ditch on Route 9 near mile marker 12 becomes a pending vehicle accident call with the location filled in, ready for a dispatcher to check. Needs the Enhanced AI add-on with AI intake turned on for your department. On Intake Channels, edit a channel and set Processing to Enhanced AI. A dispatcher still reviews every pending call, and departments using Advanced Data Protection are not processed by AI.

<a id="ai-voice-intake"></a>
## Enhanced AI voice intake

Enhanced AI voice intake Callers to the department's private inbound dispatch number who are not members answer spoken, guided questions, and Enhanced AI turns their answers into a pending call. Take incident reports by phone at any hour without someone answering, while a dispatcher still decides who to send. A caller reports smoke behind a barn, answers the questions about where it is and what they see, and a pending call with the transcript waits for the dispatcher. Needs the Private Inbound Dispatch Number and Enhanced AI add-ons with AI voice intake turned on for your department. On Private Inbound Dispatch Number, turn on Take incident reports by voice and save. Voice calls have a monthly fair-use limit; over it, callers are asked to text the number instead.

<a id="ai-destination-advice"></a>
## Enhanced AI destination advice

Enhanced AI destination advice Enhanced AI recommends the destination for an open call from its nature, note and call notes and your destinations' capabilities and availability, and suggests a change when the call's details change. Catch a patient who now needs a higher level of care, such as a trauma center, without anyone re-checking every destination by hand. A call note reports a penetrating injury, and the advice on the call suggests the trauma center instead of the nearest hospital, for the dispatcher to apply or dismiss. Needs the Enhanced AI add-on with AI destination advice turned on for your department. Add your destinations with their capabilities and destination rules first. The advice shows on View Call and Update Call, nothing changes until someone applies it, and departments using Advanced Data Protection do not get advice.

<a id="ai-knowledge"></a>
## Knowledge answers

Knowledge answers Members ask a question and get an answer taken only from your department's published protocols and training and the Resgrid Academy, each part linked to its source. Members find what your protocols say in seconds, and the people who write them see the questions the content does not answer. A member asks which protective equipment the hazmat guideline requires and gets the answer with a link to that section of the version in force. Needs the Enhanced AI add-on with knowledge answers turned on for your department. Publish your protocols and start your trainings; members then use Ask on the protocol library, a protocol, a training or a call. Training officers see unanswered questions under Knowledge gaps.

<a id="setting-documentnumberingconfig"></a>
## Document Numbering Config

Document Numbering Config. How work order, invoice, bid and daily time report numbers are written (Department -> Document Numbering): one pattern per document kind and the day their numbering year starts, plus a raised next number per custom sequence. A kind with no pattern keeps its built-in numbers. Changing it never renumbers documents that already have a number.

Default: missing = the built-in numbers. Source: DepartmentSettingTypes.DocumentNumberingConfig.

<a id="field-documentnumberingconfig-yearstartmonth"></a>
## Document Numbering Config / Year Start Month

Document Numbering Config / Year Start Month. Month the numbering year starts (1 to 12) for work orders, invoices, bids and daily time reports, in the department's local time. Work order numbers (their NumberYear) and patterns with {YYYY} or {YY} start again at 1 on that month and day and write the fiscal year's name; {MM} and {DD} always write the calendar date. Unset (0) is January, the calendar year. Call numbers (setting 115) and Records numbers (setting 72) have their own year start.

Default: 0 (January). Source: DocumentNumberingConfig.YearStartMonth.

<a id="field-documentnumberingconfig-yearstartday"></a>
## Document Numbering Config / Year Start Day

Document Numbering Config / Year Start Day. Day of the month the numbering year starts, from 1 to that month's last day; February 29 is refused so the year starts on the same date every year. Unset (0) is the 1st.

Default: 0 (the 1st). Source: DocumentNumberingConfig.YearStartDay.

<a id="field-documentnumberingconfig-yearlabel"></a>
## Document Numbering Config / Year Label

Document Numbering Config / Year Label. Which year {YYYY} and {YY} write for a numbering year that does not start on January 1: 0, the year it ends in (the usual fiscal-year convention, so a year starting November 1, 2026 is 2027), or 1, the year it starts in. Ignored for a January 1 start.

Default: 0 (the year it ends in). Source: DocumentNumberingConfig.YearLabel.

<a id="field-documentnumberingconfig-patterns"></a>
## Document Numbering Config / Patterns

Document Numbering Config / Patterns. The department's own pattern per document kind (work-order, invoice, bid, time-report), in the call number grammar. A kind with no entry, an entry that no longer validates or one equal to the built-in pattern and width keeps the built-in numbers: WO-{YYYY}-{SEQ} at 6 digits for work orders and the plain number for invoices, bids and time reports. The int numbers stay as the unique, ordered ids; the issued text is stored as DisplayNumber (M0268). A custom pattern's sequences are counted in DocumentNumberSequences and seeded from the numbers already issued in the same text, so changing the pattern never re-issues a number. The number as issued is what members see on the document, its PDF and file name, exports, search and invoice lines, and for daily time reports in the Responder, Unit, IC and Dispatch apps (v4 `DisplayNumber`; the apps fall back to the plain number on servers without it).

Default: empty. Source: DocumentNumberingConfig.Patterns.

<a id="field-documentnumberpattern-kind"></a>
## Document Number Pattern / Kind

Document Number Pattern / Kind. The DocumentNumberKinds key the entry numbers: work-order, invoice, bid, time-report (setting 117) or records-request, occupancy, inspection, permit, investigation, evidence (setting 72's DocumentPatterns).

Default: unset. Source: DocumentNumberPattern.Kind.

<a id="field-documentnumberpattern-pattern"></a>
## Document Number Pattern / Pattern

Document Number Pattern / Pattern. Fixed text (letters, digits, - _ . /) around {YYYY} or {YY}, {MM}, {DD} and {SEQ}, {SEQ} exactly once, up to 40 characters. Every number it can write must fit the kind's column: 50 characters, 32 for occupancy, inspection, permit, investigation and evidence numbers.

Default: unset. Source: DocumentNumberPattern.Pattern.

<a id="field-documentnumberpattern-sequencewidth"></a>
## Document Number Pattern / Sequence Width

Document Number Pattern / Sequence Width. Digits the sequence is padded to with leading zeros, 1 to 8. The sequence is counted as a number, so changing the width never restarts it.

Default: the kind's built-in width. Source: DocumentNumberPattern.SequenceWidth.

<a id="setting-departmentterminology"></a>
## Department Terminology

Department Terminology. The department's own names for modules, with the word for one item (Team for Units renamed Teams), and for built-in record types. They are carried through the website's navigation, page titles, buttons, column headings, messages and exports, and shown in the Responder, Unit, IC and Dispatch apps, in every language. Labels only: permissions, links, API values and members' own text do not change. A term with no name uses Resgrid's.

Default: missing = the shipped names. Source: DepartmentSettingTypes.DepartmentTerminology.

<a id="field-departmentterminologyconfig-terms"></a>
## Department Terminology / Terms

Department Terminology / Terms. One entry per renamed module or record type, for example Incident for Run or Shift On Duty for Work. A module entry can also hold the word for one item, such as Team for Units renamed Teams; left empty, it is worked out from the name. A term not listed shows Resgrid's name in the member's language. Names are at most 50 characters.

Default: empty. Source: DepartmentTerminologyConfig.Terms.

<a id="field-departmenttermoverride-key"></a>
## Department Term / Key

Department Term / Key. The term being renamed: a module (module.calls, module.records ...) or a built-in record type (record-type.run, record-type.work ...). Keys never change.

Default: unset. Source: DepartmentTermOverride.Key.

<a id="field-departmenttermoverride-text"></a>
## Department Term / Name

Department Term / Name. The department's word for the term, at most 50 characters. The same word shows for every member in every language; empty uses Resgrid's name.

Default: unset (Resgrid's name). Source: DepartmentTermOverride.Text.

<a id="field-departmenttermoverride-singular"></a>
## Department Term / One Item

Department Term / One Item. For a module, the department's word for one item, such as Team for Units renamed Teams, used on buttons and headings like New Team; at most 50 characters. Empty works it out from the name. Record types have none.

Default: unset (worked out from the name). Source: DepartmentTermOverride.Singular.

<a id="module-incidentbackofficedisabled"></a>
## Incident Back Office availability

Incident Back Office availability. Controls availability of the Incident Back Office desks. Disabling this module removes them from navigation and blocks their operations; it does not delete activations or their records. Turning it on still requires the Incident Back Office add-on, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.IncidentBackOfficeDisabled.

<a id="module-incidentbusinessdisabled"></a>
## Incident finance availability

Incident finance availability. Controls the incident finance desks: procurement, budget, time, cost, claims, recovery, intake and closeout. Disabling it hides those desks and blocks their operations without deleting their records. It only applies while the Incident Back Office is on, and turning it on still requires the Incident Business rollout and permissions.

Default: false. Source: DepartmentModuleSettings.IncidentBusinessDisabled.

<a id="module-incidentcoordinationdisabled"></a>
## Coordination boards availability

Coordination boards availability. Controls coordination boards and the situation desk. Disabling it hides them and blocks their operations without deleting boards or their items. It only applies while the Incident Back Office is on, and turning it on still requires the Incident Coordination rollout and permissions.

Default: false. Source: DepartmentModuleSettings.IncidentCoordinationDisabled.

<a id="setting-incidentbackofficedefaults"></a>
## Incident Back Office defaults

Incident Back Office defaults. Defaults for new activations: how old an incident command or dispatch source may get before its tiles grey out, the currency and measurement system (blank uses the department's country), the length of an operational period, and whether exercises may notify external participants and vendors.

Default: missing = 15-minute staleness, currency and measurement system from the department's country, 12-hour operational periods, exercises never notify external participants or vendors. Source: DepartmentSettingTypes.IncidentBackOfficeDefaults.

<a id="setting-incidentenrollmentpolicy"></a>
## Incident enrollment policy

Incident enrollment policy. How people and units seen on an incident become participants (the enrollment mode), whether people not yet enrolled are counted as a separate band in feeding, sleeping and water forecasts, and how long or old the enrollment queue may get before an alert is raised.

Default: missing = Suggest mode, unregistered forecast band on, alert at 25 waiting or 120 minutes. Source: DepartmentSettingTypes.IncidentEnrollmentPolicy.

<a id="setting-incidentnotificationrouting"></a>
## Incident notification routing

Incident notification routing. Which incident positions receive each Incident Back Office notice. Positions are resolved to their current holders when the notice is sent; a notice with no positions configured uses its built-in default positions.

Default: missing = built-in default positions for each notice (IncidentNotificationRoutingPolicy.Defaults). Source: DepartmentSettingTypes.IncidentNotificationRouting.

<a id="setting-incidentforecastpolicy"></a>
## Incident forecast policy

Incident forecast policy. Coefficients and alert windows for logistics forecasts: meal and water contingency, drinking water per person per day, bed reserve, whether predicted timeouts reduce forecasts, replacement lead time, the arrival, timeout and meal cutoff lead windows, and the facility capacity and water days-of-supply thresholds.

Default: missing = 10% meal and 15% water contingency, 7.5 litres per person per day, 5% bed reserve, timeouts do not reduce forecasts, 3-day replacement lead, 120-minute arrival, 3-day timeout and 180-minute meal cutoff windows, 90% facility and 2-day water thresholds. Source: DepartmentSettingTypes.IncidentForecastPolicy.

<a id="setting-incidentassignmentrulepolicy"></a>
## Incident assignment rules

Incident assignment rules. The work/rest rule pack and assignment limits used to flag people who are reaching their limits: maximum assignment days, travel days, maximum extension, maximum work hours per shift and the rest required per hour worked.

Default: missing = federal-14-plus-travel pack: 14 assignment days, 2 travel days, 7 extension days, 16 work hours per shift, 1 hour of rest per 2 hours worked. Source: DepartmentSettingTypes.IncidentAssignmentRulePolicy.

<a id="setting-incidentvendorpolicy"></a>
## Incident vendor policy

Incident vendor policy. How often vendors must be verified again, how long a capacity hold lasts, and how far ahead expiring compliance documents and capacity holds are flagged.

Default: missing = verify every 365 days, holds expire after 24 hours, compliance flagged 30 days and holds 6 hours ahead. Source: DepartmentSettingTypes.IncidentVendorPolicy.

<a id="setting-incidentselfservicepolicy"></a>
## Incident self-service policy

Incident self-service policy. Self-service links for participants and vendors: how many days a link stays valid after the assignment or engagement ends, whether a one-time code is required before lodging or travel details are shown, and whether vendor links need a verified vendor contact.

Default: missing = links outlive the assignment by 14 days, one-time code required for lodging, verified vendor contact not required. Source: DepartmentSettingTypes.IncidentSelfServicePolicy.

<a id="setting-incidentfinancepolicy"></a>
## Incident finance policy

Incident finance policy. Finance controls: single-person operation (one person may request, approve, commit, receive and accept only with a recorded acknowledgment), extra sender domains for the intake inbox, the budget, agreement ceiling and daily burn thresholds, intake backlog limits, claim and recovery deadline warnings, the split-purchase window and the micro-purchase threshold.

Default: missing = separation of duties enforced, no extra intake domains, 80% budget and 85% agreement ceiling thresholds, no daily burn threshold, intake backlog 50 items or 24 hours, claim deadlines 7 and recovery deadlines 14 days ahead, 3-day split-purchase window, 10,000 micro-purchase threshold. Source: DepartmentSettingTypes.IncidentFinancePolicy.

<a id="setting-incidentretentionpolicy"></a>
## Incident retention policy

Incident retention policy. How long operational snapshots are kept after an incident closes, how many years ledger entries are kept, and how long expired self-service links are kept. Incident records follow Records retention instead.

Default: missing = snapshots 90 days after close, ledger 7 years after close, expired self-service links 30 days. Source: DepartmentSettingTypes.IncidentRetentionPolicy.

<a id="setting-coordinationboarddefaults"></a>
## Coordination board defaults

Coordination board defaults. Defaults for coordination boards: when an item counts as stale, whether road closures and shelter capacity raise alarms, how many days a board share lasts, and how far ahead a due item is flagged.

Default: missing = items stale after 60 minutes, road closure and shelter capacity alarms on, shares last 14 days, due items flagged 60 minutes ahead. Source: DepartmentSettingTypes.CoordinationBoardDefaults.

<a id="incident-activation"></a>
## Incident activation and command

Incident activation and command Open an activation for a large incident, planned event or exercise and follow it from the command desk. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Review the Incident Back Office settings, then open an exercise activation.

<a id="incident-planning"></a>
## Planning, organization and check-in

Planning, organization and check-in Operational periods and plan versions, the incident organization and positions, participants and check-in. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Build your position catalog and team rosters, then check participants in during an exercise.

<a id="incident-logistics"></a>
## Incident logistics

Incident logistics Resource requests, facilities, lodging, feeding, water and support services, with vendors and the support plan. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Add your lodging, feeding, water and transport vendors, then review the forecast policy.

<a id="incident-finance"></a>
## Incident finance

Incident finance Procurement, budget, time, cost and claims for an incident, with separation of duties. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Turn on Incident Business, review the finance policy, then enter the incident budget.

<a id="coordination-boards"></a>
## Coordination boards

Coordination boards Shared boards for road closures, shelters and other items coordinated with partner agencies. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Turn on Incident Coordination, review the board defaults, then create a board.

<a id="incident-open"></a>
## Open an Incident

Open an Incident Start an activation for an incident, planned event or exercise, optionally linked to a call or a parent complex. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-command-desk"></a>
## Command Desk

Command Desk One activation's status, operational picture, data freshness and open work in one place. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-operations"></a>
## Operations Desk

Operations Desk Who is on the incident, their support status and position, and the resources by support status. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-settings"></a>
## Incident Back Office Settings

Incident Back Office Settings Department policies for activations, enrollment, notice routing, forecasts, assignments, vendors, self-service links, finance, retention and boards. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-organization"></a>
## Incident Organization

Incident Organization The organization chart of an activation and who is assigned to each position. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-positions"></a>
## Position Catalog

Position Catalog The incident positions your department fills and the capabilities each one needs. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-teams"></a>
## Incident Teams

Incident Teams Standing team rosters with rotations, slots and qualified candidates. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-participants"></a>
## Participants

Participants The people supporting an activation, with their qualifications, home organizations and self-service links. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-check-in"></a>
## Check-In

Check-In Check people and resources in and out, and track their status, movements and assignment extensions. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-iap"></a>
## Incident Action Plan

Incident Action Plan Assemble the incident action plan for each operational period. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-authority"></a>
## Incident Authority

Incident Authority Delegations of authority by domain, the authority profile copied into each activation and the items left to reconcile. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-demob"></a>
## Demobilization

Demobilization Demobilization plans and checklists for releasing people and resources. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="support-plan"></a>
## Emergency Support Plan

Emergency Support Plan Your department's emergency support plan: support areas and preferred vendors, copied into each activation. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-requests"></a>
## Resource Requests

Resource Requests Mission packages and resource requests from approval through fill, arrival and receipt. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-facilities"></a>
## Incident Facilities

Incident Facilities Base camps, staging areas and other incident facilities, with capacity, agreements and services. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-lodging"></a>
## Lodging

Lodging Lodging properties, room blocks, nightly inventory, bed assignments and rooming lists. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-feeding"></a>
## Feeding

Feeding Meal plans, forecast demand, meal orders and the meals actually served. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-water"></a>
## Water

Water Drinking water plans, stock on hand and deliveries. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-services"></a>
## Support Services

Support Services Support services such as laundry, showers or sanitation, with their requests, activity and assets. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-ground-support"></a>
## Ground Support

Ground Support Transport requests and movements, travel itineraries and fuel. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-communications"></a>
## Incident Communications

Incident Communications The communications plan of an activation and its radio and device assignments. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="vendor-catalog"></a>
## Vendor Catalog

Vendor Catalog Vendors with their locations, capabilities, availability windows and compliance documents. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-vendors"></a>
## Vendor Engagements

Vendor Engagements Vendors engaged on an activation, with their capacity commitments and quotes. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-procurement"></a>
## Incident Procurement

Incident Procurement Vendor agreements, requisitions and approvals, commitments, receipts, vendor invoices and payment packages. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-budget"></a>
## Incident Budget

Incident Budget Budget lines, funding authorizations, spending delegations, funding codes and cost groups. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-time"></a>
## Incident Time

Incident Time Time records and adjustments, equipment use and expenses, with certification and approval. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-cost"></a>
## Incident Cost

Incident Cost Cost transactions, accruals, daily cost projections and cost-share apportionment. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-claims"></a>
## Incident Claims

Incident Claims Claims for injury, damage or loss arising from an incident. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-recovery"></a>
## Disaster Recovery

Disaster Recovery Disaster declarations, recovery projects and their deadlines. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-intake"></a>
## Incident Intake

Incident Intake Documents and receipts sent to the incident's intake inbox, waiting to be filed. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-closeout"></a>
## Incident Closeout

Incident Closeout The closeout checklist that finishes an incident's books. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="incident-situation"></a>
## Situation Desk

Situation Desk Lifeline status, open actions, decisions and mapped situation features. Run the support side of a large incident from one activation: who is on it, where they sleep and eat, what was ordered and what it cost. A wildfire activation checks in arriving strike teams, books their lodging and meals, and tracks every purchase against the incident budget. Requires the Incident Back Office add-on. The finance desks also need Incident Business turned on, and coordination boards need Incident Coordination. Run an exercise activation before the first real incident.

<a id="addon-incidents"></a>
## Incident Back Office

Incident Back Office The Incident Back Office add-on: activation, planning, logistics, finance and coordination boards for large incidents. Keep the support side of a long incident in one place instead of spreadsheets, radio notes and email. Book lodging and meals for arriving crews and track each purchase against the incident budget. Incident finance and coordination boards are turned on separately. Incident records stay in Records and follow its permissions. No reimbursement is guaranteed.

<a id="dedicated-number"></a>
## Department number and intake

Department number and intake Choose and provision your department's number, choose whether texts and voice calls to it create pending calls, and release it when you no longer need it. Give the public, partner agencies and members one local number to text or call, and every report waits in Pending Calls for a dispatcher instead of on someone's personal phone. A rural district prints its new local number on its non-emergency reporting flyers, and a text about a tree down across a road arrives as a pending call that a dispatcher checks and sends. Once the add-on is active, open the department menu, then Private Inbound Dispatch Number. Search by country, area code or digits, provision the number you want, turn on Create pending calls from texts and save. Then send the number a test text and check Pending Calls.

<a id="intake-channels"></a>
## Intake channels

Intake channels Turn texts to the department's number, mail to an intake email address, posts in a linked Slack, Teams, WhatsApp, Telegram, Discord or Google Chat channel, and signed webhook requests into pending calls. Requests that arrive outside the radio and CAD land in one queue for a dispatcher to review; intake never dispatches anyone by itself. A hospital transfer desk posts requests in a shared Teams channel, and each post becomes a pending call with the channel's default call type and priority. Open the department menu, then Intake Channels, and add a channel for each source. Choose Call template or Enhanced AI processing, limit Allowed senders, set a default priority and call type, then send a test message and check Pending Calls.

<a id="intake-spam-screening"></a>
## Intake spam screening

Intake spam screening Keep spam and robocalls out of Pending Calls: dismiss an intake call as spam and block its sender, limit how many calls one phone number can create in an hour, ask voice callers to press 1 before the questions start, and label messages that Enhanced AI reads as not an incident. Dispatchers spend their time on real reports. A blocked sender's texts and calls create no calls and get no reply, and a screened call keeps its label in Pending Calls but is not offered on the dispatcher desk; a dispatcher still decides what happens to it. A marketing number keeps texting the department's number. Past the hourly limit its texts are added to its open call as notes, and a dispatcher dismisses that call as spam and blocks the number for 30 days. On a pending intake call, choose Dismiss as spam and keep Also block the sender ticked; the call is cancelled and nobody is notified. Blocked senders are listed on the Intake Channels page, where Unblock lifts a block early. The hourly limit applies to text, voice and WhatsApp channels without allowed senders, never to email, chat or webhook channels.

<a id="addon-dedicated-number"></a>
## Private Inbound Dispatch Number

Private Inbound Dispatch Number The Private Inbound Dispatch Number add-on: your department's own phone number for taking calls by text and by voice. Members and the public reach your department directly instead of through the shared Resgrid number. The managing member buys the add-on on the Private Inbound Dispatch Number page or its billing page, and a department admin then provisions a local number. Sold only with an active paid entity plan, and bought or cancelled by the managing member. Billed monthly per department, even on an annual plan. When the add-on ends, the number stops taking texts and calls and is released after a grace period unless the add-on is renewed.

<a id="relay"></a>
## Resgrid Relay

Resgrid Relay Install and run Relay, choose its modes and connect it to your department. The Windows desktop app runs several modes side by side with a live dashboard; the headless host runs one mode per process on a server or in Docker. A department installs the desktop app on its station PC, signs it in with a dedicated member account and turns on Auto-start for email dispatch and the radio bridge. Add a member account for the relay to sign in as, then install Relay and connect it with that account's refresh token. The radio bridge, recorder and tone-out require this user token; email dispatch and tone detection can also use client credentials. Run Test connection before starting a mode.

<a id="relay-smtp-dispatch"></a>
## Email dispatch through Relay

Email dispatch through Relay Relay's built-in mail server turns dispatch email from CAD, alarm panels or paging gateways into Resgrid calls and group messages. Mail to the department dispatch code pages the department, a group's dispatch code dispatches that group and a group's message code sends it a message. Attachments go onto the call, and a redelivered message never creates a second call. The county CAD sends each Station 5 run to that group's dispatch code at the county's dispatch domain, and the relay creates the call with the run card PDF attached. Find the department dispatch address on Call Settings and each group's dispatch and message addresses on Groups. Point your dispatch domains' mail at Relay and list those domains in its SMTP settings. The relay's account needs the Create calls permission, and Add Call Data for attachments.

<a id="relay-tone-detect"></a>
## Paging tones to calls

Paging tones to calls On Windows, Relay listens to a scanner or radio through a sound card for two-tone and single-tone pages and creates a Resgrid call with the recorded dispatch audio. Members get the page in the Responder app with the dispatcher's audio attached, even when the radio page is the only dispatch. A volunteer company's scanner hears its two-tone page, and the relay creates a call for that station's group with the dispatch audio attached. On Groups, note the dispatch code of each group you tone out: a group watcher dispatches that group, and a department watcher pages everyone. Enter each watcher's tones in Relay. The relay's account needs Create calls and Add Call Data.

<a id="relay-radio-bridge"></a>
## Radio bridge

Radio bridge On Windows, Relay bridges a cabled two-way radio to a Resgrid push-to-talk channel in both directions. Crews on the radio and members in the Responder, Unit and Dispatch apps hear each other, and an emergency tone on the radio can create a call. A SAR team bridges its VHF base radio to the team channel, so searchers with handhelds and searchers with phones talk to each other. Needs the Push-to-Talk add-on, a voice channel and the relay's refresh token. Key the radio through a CM108 USB or serial PTT interface, set the squelch with Tune and check the wiring and key-up delay with Test transmit.

<a id="relay-lte-donor-radios"></a>
## LTE push-to-talk donor radios

LTE push-to-talk donor radios Joins a cellular push-to-talk (PoC) talk group, such as Rapid Radios or Diga-Talk+, to a Resgrid voice channel through one of its radios cabled to the relay. PoC radio users and Resgrid app users share one conversation, although these PoC services offer no API or gateway for outside audio. A security team cables one Diga-Talk+ mobile to the relay, so guards on PoC radios and supervisors in the Resgrid apps hear each other. Apply the radio's profile, key it with a hard PTT line rather than VOX, turn off its own VOX and beeps, and keep it on external power. The donor radio uses one radio on the PoC account and bridges the talk group it is set to. Needs the Push-to-Talk add-on and a voice channel.

<a id="relay-recorder"></a>
## Channel recorder

Channel recorder Records every transmission on one or all push-to-talk channels as audio files, with a log of who spoke, on which channel, when and for how long. Keeps voice traffic for compliance and after-action review, on local disk or S3-compatible storage. An emergency management agency records every channel during a flood exercise and reviews the transmission log at the after-action meeting. Needs the Push-to-Talk add-on, a voice channel and the relay's refresh token. Each recorded channel occupies a voice seat. Recordings contain operational voice traffic: store, protect and prune them under your records policy.

<a id="relay-dispatch-tone-out"></a>
## Tone-out on push-to-talk

Tone-out on push-to-talk Announces each new Resgrid call on a push-to-talk channel: alert tones, then the call read aloud by Resgrid's text-to-speech. Crews who monitor a channel hear dispatches the way they would on the radio, in Resgrid's own wording with your call priority names. A plant's emergency response team monitors its channel, and each new call is toned out with a hi-lo alert followed by the call name, nature, address and priority. Needs the Push-to-Talk add-on, a voice channel and the relay's refresh token. Calls already open when the relay starts are not announced, and scheduled calls are announced when they are dispatched.

<a id="setting-wellnesssettings"></a>
## Wellness settings

Wellness settings. Who reads members' exposure and critical-incident logs (the wellness officer role), who the designated officers and the peer support team are, the certification each wellness role can hold, and how many years entries are kept. Member wellness is part of the Workforce Pro add-on, not the LMS add-on: without it, existing records are read-only.

Default: no wellness officer role, no linked certifications, no peer support team, the certification switch off, entries kept 30 years. Source: DepartmentSettingTypes.WellnessSettings.

<a id="field-wellnesssettings-wellnessofficerroleid"></a>
## Wellness settings / Wellness officer role

Wellness settings / Wellness officer role. Members of this personnel role read every member's exposure log and keep screening flags and follow-ups. Department administrators do not see the logs unless they hold the role. None leaves each log to its member.

Default: 0 (none: each member alone reads their log). Source: WellnessSettings.WellnessOfficerRoleId.

<a id="field-wellnesssettings-peerteamcertificationtypeid"></a>
## Wellness settings / Peer support team certification

Wellness settings / Peer support team certification. The peer support team's certification type. With a peer support team role, it is shown as each team member's status; with no role, the members holding a current certification of this type are the team and receive the critical-incident anniversary reminders a member chooses to share. With neither, there is no team.

Default: 0 (no peer support certification; with no peer support role either, no team). Source: WellnessSettings.PeerTeamCertificationTypeId.

<a id="field-wellnesssettings-retentionyears"></a>
## Wellness settings / Keep entries for (years)

Wellness settings / Keep entries for (years). Years an exposure entry is kept after it happened, from 30 (the OSHA 29 CFR 1910.1020 minimum) to 100. A legal hold keeps entries longer.

Default: 30. Source: WellnessSettings.RetentionYears.

<a id="field-wellnesssettings-designatedofficerroleid"></a>
## Wellness settings / Designated officer role

Wellness settings / Designated officer role. Members of this personnel role receive bloodborne-pathogen and infectious-disease exposure reports (Ryan White Part G) and run the exposure cases. None means the wellness officer role serves.

Default: 0 (the wellness officer role serves). Source: WellnessSettings.DesignatedOfficerRoleId.

<a id="field-wellnesssettings-bbptrainingcertificationtypeid"></a>
## Wellness settings / Bloodborne-pathogen training certification

Wellness settings / Bloodborne-pathogen training certification. The certification type of the annual bloodborne-pathogen training (OSHA 1910.1030). Each exposure case shows the member's current status of it. None leaves it untracked.

Default: 0 (not tracked). Source: WellnessSettings.BbpTrainingCertificationTypeId.

<a id="field-wellnesssettings-medicalclearancecertificationtypeid"></a>
## Wellness settings / Medical clearance certification

Wellness settings / Medical clearance certification. The certification type an NFPA 1582 medical evaluation is also recorded as (from the Medical Clearance (NFPA 1582) template). Not cleared suspends it. None keeps clearances in Wellness only.

Default: 0 (clearances kept in Wellness only). Source: WellnessSettings.MedicalClearanceCertificationTypeId.

<a id="field-wellnesssettings-injuryretentionyears"></a>
## Wellness settings / Keep injury records for (years)

Wellness settings / Keep injury records for (years). Years an injury record is kept after the end of its year, from 5 (OSHA 29 CFR 1904.33) to 100. A legal hold keeps records longer.

Default: 5. Source: WellnessSettings.InjuryRetentionYears.

<a id="field-wellnesssettings-wellnessofficercertificationtypeid"></a>
## Wellness settings / Wellness officer certification

Wellness settings / Wellness officer certification. A certification type of the department, such as one added from the Wellness Officer template, whose requirements and validity the department sets. Each wellness officer's status (current, expiring, expired, missing) shows on the settings page and the member exposure report. It decides who acts only when the certification switch is on. None shows no status.

Default: 0 (none). Source: WellnessSettings.WellnessOfficerCertificationTypeId.

<a id="field-wellnesssettings-designatedofficercertificationtypeid"></a>
## Wellness settings / Designated officer certification

Wellness settings / Designated officer certification. A certification type of the department, such as one added from the Designated Exposure Control Officer template, shown as each designated officer's status. None: while no designated officer role is named, the wellness officer certification serves.

Default: 0 (the wellness officer certification while no designated officer role is named). Source: WellnessSettings.DesignatedOfficerCertificationTypeId.

<a id="field-wellnesssettings-peerteamroleid"></a>
## Wellness settings / Peer support team role

Wellness settings / Peer support team role. Members of this personnel role are the peer support team and receive the critical-incident anniversary reminders a member chooses to share. None: the members holding a current peer support team certification are the team.

Default: 0 (the holders of the peer support team certification). Source: WellnessSettings.PeerTeamRoleId.

<a id="field-wellnesssettings-requirecurrentcertification"></a>
## Wellness settings / Only members with a current certification act

Wellness settings / Only members with a current certification act. Off (the default): the wellness officer, designated officer and peer support roles alone decide who acts, and each linked certification is shown as status. On: a role member also needs a current certification of the role's linked type; a role with no linked type is not affected. It cannot be turned on while nobody in the wellness officer or designated officer role holds a current certification.

Default: false (the roles alone decide; certifications are shown as status). Source: WellnessSettings.RequireCurrentCertification.

<a id="member-wellness"></a>
## My Exposures

My Exposures Each member's own log of carcinogen, bloodborne pathogen, hazardous substance and critical-incident exposures, with entries suggested from dispatch data after a call is flagged. Exposure records for cancer presumption and workers' compensation, kept 30 years, without anyone but the member and the wellness officers reading them. After a structure fire, the officer flags the call; each member on scene confirms a suggested carcinogen entry and adds the PPE worn and decontamination done. Choose the wellness officer role in Personnel, My exposures, Settings, and optionally a certification type for each wellness role. Members open My exposures from Personnel; officers flag a call from its Wellness follow-up button. Member wellness is part of the Workforce Pro add-on (not the LMS add-on): records are kept for the members with Workforce Pro switched on, and without the add-on existing records are read-only.
<a id="document-sealing"></a>
## Sealed documents and Verify a document

Sealed documents and Verify a document Certificates, transcripts, protocol PDFs and signed documents are sealed with a timestamp so anyone can check they have not changed; officers can check any PDF under Verify a document. Hand out documents that outside agencies and courts can trust, and spot altered copies. An inspector checks that a certificate a member brought from another department was issued by Resgrid and has not been edited. Open Verify a document and upload a PDF. Sealing itself is set up by the operator of the installation; until then documents are marked sealing pending.

<a id="external-signatures"></a>
## External signatures

External signatures Customers, occupants and outside parties sign time reports, bids, contracts, inspections and custody transfers in person on a member's device, or by a Resgrid link with a one-time code. Replace typed names with signatures the other party can verify, sealed with an audit page. A crew boss hands the tablet to the customer's representative to sign the daily shift ticket before leaving the incident. Open the document and use Hand device to signer, or request a signature by link. For links, an admin adds the department's Documenso team token under E-sign settings; links come with the add-on that owns the document.

<a id="ai-authoring-aids"></a>
## Drafting aids

Drafting aids Draft quiz questions, course outlines and change summaries from the department's own content for authors to review. Authors start from a grounded draft instead of a blank page, and nothing is saved until they accept it. A training officer drafts five quiz questions from the new SCBA training and keeps three. Turn on knowledge answers with Enhanced AI; the aids then appear in the training editor and the course builder. Every number in a draft must appear in the content, content marked no AI is never sent, and departments using Advanced Data Protection do not get drafts.

<a id="ai-call-answers"></a>
## Ask about a call

Ask about a call Members ask a question on View Call and get an answer taken only from the protocol versions the call cites and its type, priority, determinant and location type, each part linked to its section. Crews and dispatchers find what the call's protocols say for this call in seconds, and patient or caller details are never sent. On a structure fire at a school, a lieutenant asks where crews stage and gets the answer from the cited section of the school protocol. Turn on knowledge answers with Enhanced AI and attach protocols to calls; Ask about this call then appears on View Call's protocols tab. Only the call's type, priority, determinant and location type are used, never the caller, the address, notes or patient details, and departments using Advanced Data Protection get the matching sections without generated text.

<a id="ai-tutor"></a>
## Tutor and scenario practice

Tutor and scenario practice Learners ask questions about the course they are taking, and members practise on a short scenario written from a protocol, with their own answers scored against that protocol. Members check their understanding between drills, and every answer and score points back to the exact section, without anything being recorded on their transcript. A new EMT practises the cardiac arrest protocol: a short scenario, three questions, and feedback that quotes the steps the protocol lists. Turn on knowledge answers with Enhanced AI; Practice then appears on each protocol. Ask this course in the course player also needs the LMS add-on. Scores and feedback only use numbers written in the protocol, practice never creates a training record, and departments using Advanced Data Protection do not get practice.

<a id="ai-course-drafts"></a>
## Course drafts from protocols and SOPs

Course drafts from protocols and SOPs Draft an online course from a published protocol or an uploaded SOP document: modules, lessons that each cite the sections they teach, and a question bank to review. Training officers start from a complete draft that follows the department's own procedure instead of a blank page, and every lesson shows the source text it rests on. A training officer turns the new mayday SOP into a draft course with four modules and five quiz questions, checks each lesson against its cited section, and submits it for approval. Needs Enhanced AI with knowledge answers and the LMS add-on; open Trainings, Course library, Draft with AI. Drafts are never published on their own, every number must appear in the cited sections, content marked no AI is never sent, and departments using Advanced Data Protection do not get drafts.

<a id="ai-flashcards"></a>
## Course flashcards

Course flashcards Practice cards drafted from a course or the protocol it is built from; authors keep the ones they want and learners practise them in the course player. Members rehearse the steps and criteria they must remember between drills, and a card is hidden and flagged for review as soon as its source gets a new version. After the cardiac arrest protocol moves to a new version, its flashcards disappear from the player until the training officer checks them against the new version. Needs Enhanced AI with knowledge answers and the LMS add-on; open a course and choose Flashcards. Practice is never graded or recorded on the transcript.

<a id="ai-protocol-tools"></a>
## Protocol practice cards, training and translation drafts

Protocol practice cards, training and translation drafts Draft practice cards, a training with a quiz, and translations from a published protocol version for authors to review; members practise the cards on the protocol. Protocol authors get drafts grounded in the department's own protocols with Enhanced AI alone, and every drafted item cites the section it comes from. After the stroke protocol is published, the EMS coordinator drafts ten practice cards, a training with five quiz questions for the acknowledgment, and a Spanish translation to review. Turn on knowledge answers with Enhanced AI; open a published protocol and choose Practice cards or Draft a training, or open a draft translation and choose Draft translation with AI. No LMS add-on is needed. Every card, training part and question cites a section and only uses numbers written there; a translation keeps every dose, number, link and the structure of the source and still needs its approval; drafted trainings are created unassigned; content marked no AI is never sent, and departments using Advanced Data Protection do not get drafts.

<a id="help-search"></a>
## Help center search

Help center search Free keyword search over the Resgrid Academy and the product reference from the Help menu. Members find the lesson or setting they need without leaving Resgrid. A new member searches for status and opens the orientation lesson. Nothing to set up: open Help, then Search help. With Enhanced AI knowledge answers, How do I questions are answered from the Academy lessons only.

<a id="academy-guidance"></a>
## Guided tours and What's new

Guided tours and What's new First-run tasks for each role built on the Academy walkthroughs, and a short lesson on each release. New members and administrators know what to do first, and everyone sees what changed. A new dispatcher ticks off opening the call list, checking units and the map. Nothing to set up: each member sees the tour for their role on the dashboard and can hide it.
<a id="multi-signer-signatures"></a>
## Multi-signer signatures

Multi-signer signatures Several people sign one document in order: members at level 1 or 2, witnesses, certifying officials, evaluators and outside parties, with reminders until everyone has signed. Sign-offs that need more than one person are recorded in the right order, bound to the exact content, and sealed with an audit page when complete. A training officer evaluates a skill at level 2, then the certifying official signs; each is reminded when it is their turn. Members find what is waiting for them under My signatures and on their home page; requests start from the document that needs signing.

<a id="approval-chains"></a>
## Approval chains

Approval chains Purchases, Incident Action Plans, invoices, Cal OES MARS packages and mutual-aid agreements are approved by a chain of people in order, with amount thresholds, parallel steps, delegates while someone is on leave and escalation when a step stalls. Every approval is a level 2 signature bound to the exact content: a change voids it, a rejection needs a reason, and the completed chain seals the document. Purchases over 5,000 need the finance chief after the logistics section chief; if the finance chief does not act within 24 hours, the department admins are asked. A department admin defines a chain per kind under Approvals, Chains; members submit from the item's page or Approvals, and approvers find requests under My approvals and on their home page.

<a id="checklist-witness-signature"></a>
## Checklist witness signature

Checklist witness signature A checklist can require its independent witness to sign at level 2 (a fresh step-up or a passkey) instead of attesting with text alone. Controlled-substance and safety checks get a witness signature bound to the recorded results, included with Checklists at no extra cost. After a drug box check, a second paramedic signs as witness with their passkey before the check counts as complete. Edit the checklist, turn on the independent witness and the witness signature, and publish the new version.

<a id="readiness-signatures"></a>
## Vendor acceptance and issue receipts

Vendor acceptance and issue receipts Vendors accept completed work orders, and members or recipients sign receipts for issued equipment, in person or by a Resgrid link. Replace verbal hand-offs with signed acceptances and receipts that the other party can verify. The pump repair shop's technician signs the completed work order on the station tablet before driving away. Open a completed work order, or an issued item's Receipt, and request the signature. Remote links need Readiness Pro and the department's e-sign settings.
<a id="setting-lmsprogramsettings"></a>
## Training program settings

Training program settings. How training requests are approved and in which currency their costs are entered, and whether Records inspections and pre-plan reviews credit building familiarization.

<a id="field-lmsprogramsettings-requestsupervisorapproval"></a>
## Training program settings / Supervisor approves training requests

Training program settings / Supervisor approves training requests. The admins of the member's group (or the department admins) approve a training request first.

<a id="field-lmsprogramsettings-requesttrainingofficerapproval"></a>
## Training program settings / Training officer approves training requests

Training program settings / Training officer approves training requests. A member who manages training records for the whole department approves the request, after the supervisor when both are on.

<a id="field-lmsprogramsettings-requestcurrency"></a>
## Training program settings / Training currency

Training program settings / Training currency. The three-letter currency code (EUR, CAD, USD …) the department's training request costs, class costs and class fees are entered and totalled in. A class can name its own.

<a id="field-lmsprogramsettings-familiarizationenabled"></a>
## Training program settings / Credit building familiarization

Training program settings / Credit building familiarization. Completed occupancy inspections and pre-plan reviews in Records go on the transcript of the person who did them.

<a id="field-lmsprogramsettings-familiarizationcategoryid"></a>
## Training program settings / Familiarization category

Training program settings / Familiarization category. The training category familiarization credit lands in, for example the ISO pre-fire planning category.

<a id="field-lmsprogramsettings-familiarizationminutes"></a>
## Training program settings / Minutes per visit

Training program settings / Minutes per visit. The minutes credited for a visit that has no start and end time. A visit with its own times is credited its real length.

<a id="field-lmsprogramsettings-familiarizationfrominspections"></a>
## Training program settings / Credit from inspections

Training program settings / Credit from inspections. Completed occupancy inspections credit the inspector.

<a id="field-lmsprogramsettings-familiarizationfrompreplanreviews"></a>
## Training program settings / Credit from pre-plan reviews

Training program settings / Credit from pre-plan reviews. Occupancy and contact pre-plan reviews credit the reviewer.

<a id="lms-training-plan"></a>
## Annual Training Plan

Annual Training Plan A year of planned sessions and hours per category and month for the department, a group or a shift, compared with the hours actually recorded. The written plan auditors, regulators and accreditors ask for, and a shortfall against the annual targets that shows in June, not at the audit. The training officer plans 16 hours of company training each month; by June the forecast shows the department will miss the 192-hour target by 20 hours. Nothing to set up yet: the annual plan is part of the LMS add-on, which is not available yet.

<a id="lms-competency-frameworks"></a>
## Competency Frameworks

Competency Frameworks Job performance requirements, positions and skills, tagged on the courses, trainings and certifications that evidence them, with a gap report per member. Shows for every member which requirements have evidence, how old it is, and which have none. A department imports its regulator's competency framework, tags its courses, and sees a recruit still has no evidence for three requirements. Nothing to set up yet: competency frameworks are part of the LMS add-on, which is not available yet.

<a id="lms-training-requests"></a>
## Training Requests

Training Requests Members ask to attend an outside class with its cost; the supervisor and a training officer approve it, and the class goes on the transcript once taken. One approved, costed record per outside class, and spending totals per member, course and year. A paramedic asks to attend a 16-hour course costing 450 dollars; the captain and the training officer approve it, and the certificate is verified afterwards. Nothing to set up yet: training requests are part of the LMS add-on, which is not available yet.

<a id="lms-report-catalog"></a>
## Training Report Catalog

Training Report Catalog Every training report in one place, saved with its settings in folders and sent on a daily, weekly or monthly schedule as CSV or PDF. The reports an audit needs arrive by themselves, already filtered the same way every time. The ISO training report goes to the chief and the training officer as a PDF on the first of every month. Nothing to set up yet: saved and scheduled reports are part of the LMS add-on, which is not available yet.

<a id="lms-certification-cycles"></a>
## Certification Cycles

Certification Cycles A certification's next cycle starts by itself once its renewal hours are met, and supervisors, admins or named members are alerted when a cycle completes, starts, nears expiry or expires. Recurring credentials keep tracking hours without anyone re-entering them, and the right people hear about them in time. When an EMT reaches the renewal hours, the next two-year cycle starts at the current expiry and the EMS captain gets a note. Nothing to set up yet: certification cycle rules are part of the LMS add-on, which is not available yet.

<a id="lms-certification-requirements"></a>
## Certification Requirement Sets

Certification Requirement Sets A certification is issued or renewed automatically when a member completes every item of its set inside the cycle: LMS courses, a course set, trainings, a supervised practical recorded by an instructor, skill evaluations or continuing-education hours. Voiding a completion behind it takes the certification back. Members and officers see exactly what is left ("3 of 4 done"), nobody re-enters a renewal by hand, and a mistaken completion never leaves a certification standing on nothing. An operator certification needs the yearly driving course, the vehicle checks course and a slow speed course an instructor signs off; when the last one is recorded the certification renews to the next 31 December. Nothing to set up yet: certification requirement sets are part of the LMS add-on, which is not available yet.

<a id="certification-validity-rules"></a>
## Certification Validity Rules

Certification Validity Rules A certification type expires a number of months after issue, at the end of each calendar year, at the end of the organization's own year, or on a fixed date, with an optional grace period and renewal window. Annual and fiscal-year authorizations expire the same day for everyone, renewals land in the right year, and dates follow the organization's time zone. A forklift authorization set to expire on 31 March each year renews to the following 31 March when the refresher is completed in February. Open Certifications, then a certification type, and choose how it expires.

<a id="training-read-and-confirm"></a>
## Read and Confirm Trainings

Read and Confirm Trainings A training without a quiz can ask each member to confirm they read and reviewed it before its due date, and repeat every year: an employee handbook, a code of conduct or a site rule. Opening a document is no longer counted as reading it: the record shows who confirmed and when, ready for an audit. The employee handbook is assigned every January with a 31 January due date; each member ticks "I have read and reviewed this training" and the transcript shows the date. Create a training, attach or write the document, tick "Members confirm they have read and reviewed it", set a due date and repeat every 12 months.

<a id="lms-credential-audiences"></a>
## Messages by Certification Status

Messages by Certification Status Send a message to the members whose certification has expired, expires within a number of days, or who never held it. Reminders reach exactly the people who need them, without building a list by hand. The training officer messages everyone whose CPR card expires within 30 days with the date of the next class. Nothing to set up yet: certification-status audiences are part of the LMS add-on, which is not available yet.

<a id="lms-protocol-learning"></a>
## Protocol Quizzes and Micro-Learning

Protocol Quizzes and Micro-Learning Make a protocol's acknowledgment wait for a quiz drawn from a question bank, and send members one scenario question a week from chosen protocols. Members show they understood a protocol, not only that they opened it, and keep it fresh between reviews. A new mayday procedure needs five questions passed at 80% before the acknowledgment counts, and every Monday each firefighter answers one scenario from it. Nothing to set up yet: protocol quizzes and micro-learning are part of the LMS add-on, which is not available yet.
<a id="lms-class-sessions"></a>
## Class Sessions

Class Sessions Instructor-led classes on the calendar with capacity, waitlist, enrolment rules, rotating-QR and GPS sign-in, and an instructor sign-off that writes every attendee's training record. One roster per class that stands as the record: who taught, who attended, what was covered and for how long, kept for the category's retention. A bloodborne pathogens refresher for 20 seats fills up; three members wait on the waitlist, and when one withdraws the next is enrolled and notified. Nothing to set up yet: class sessions are part of the LMS add-on, which is not available yet.

<a id="lms-lesson-plans"></a>
## Lesson Plans

Lesson Plans Reusable plans for instructor-led sessions: objectives, competencies covered, materials and props, instructor-to-student ratio, safety plan and pre-brief checklist, versioned and approved before use. Live-fire, driving and SCBA sessions are taught from an approved plan, and the roster shows which version was used. The training officer copies the live-fire starter plan, adds the department's burn building and ratios, and a chief approves it before the first evolution. Nothing to set up yet: lesson plans are part of the LMS add-on, which is not available yet.

<a id="lms-training-resources"></a>
## Facility and Prop Booking

Facility and Prop Booking The organization's training grounds, classrooms, props, vehicles and equipment, booked for class times so nothing is double-booked. Two classes never plan on the same burn building or engine for the same afternoon. The ladder evolution books the training tower and Engine 3 for Saturday morning; a second class asking for the tower then is refused. Nothing to set up yet: facility booking is part of the LMS add-on, which is not available yet.
<a id="setting-lmsskillsettings"></a>
## Skill notice settings

Skill notice settings. How skill evaluation and task book notices are delivered, immediately or as a daily or weekly digest, and whether the evaluated members and the department admins receive them.

<a id="field-lmsskillsettings-digestmode"></a>
## Skill notice settings / Skill notice delivery

Skill notice settings / Skill notice delivery. Immediately, a daily or weekly digest (one message per person with every notice due), or off.

<a id="field-lmsskillsettings-digesthour"></a>
## Skill notice settings / Skill digest time

Skill notice settings / Skill digest time. The department-local hour daily and weekly digests are sent.

<a id="field-lmsskillsettings-digestweekday"></a>
## Skill notice settings / Skill weekly digest day

Skill notice settings / Skill weekly digest day. The weekday weekly digests are sent.

<a id="field-lmsskillsettings-notifymembers"></a>
## Skill notice settings / Notify evaluated members

Skill notice settings / Notify evaluated members. The evaluated members and trainees receive notices about their own evaluations and task books.

<a id="field-lmsskillsettings-notifyofficers"></a>
## Skill notice settings / Notify department admins

Skill notice settings / Notify department admins. The active department admins receive a notice about every evaluation and task book.

<a id="lms-skill-sheets"></a>
## Skill Sheets

Skill Sheets Job-performance skill sheets with ordered steps, critical criteria that fail the evaluation, rating scales, timers and evidence, versioned, with a library of Resgrid starters. Every evaluator grades the same skill the same way, and the department can show exactly what was checked. The SCBA donning sheet fails anyone who takes longer than 60 seconds or misses the facepiece seal check. Open Trainings, choose Skills, then Skill sheets, and start from a template.

<a id="lms-skill-evaluations"></a>
## Skill Evaluations

Skill Evaluations Evaluator mode: grade one member or a whole crew on a skill sheet, with Pass All, annotations and evidence, then sign at L2; passed evaluations land on the transcript and the record is sealed. Skills checked on the drill ground become signed, sealed proof that requirements and certifications can count. A captain grades Engine 2's crew on the hoseline advance on one form, splits out the member who struggled, and signs from a tablet. Publish a skill sheet, set who may evaluate on it, then choose Start an evaluation under Trainings, Skills.

<a id="lms-task-books"></a>
## Position Task Books

Position Task Books Task books on the NWCG model: initiated with a position code and date, tasks signed by qualified evaluators across dates and incidents, recommended by a final evaluator and certified by the certifying official; certification activates the trainee's certification. Trainee positions move to qualified with a full, signed trail instead of a paper book in a locker. An FFT2 trainee's task book is initiated in March, two incident tasks are signed on wildfires that summer, and the certifying official certifies it in September. Open Trainings, choose Skills, then Definitions, start from a task book starter, set who signs and certifies, then initiate books under Task books.

<a id="setting-lmscereportingsettings"></a>
## CE reporting settings

CE reporting settings. The CAPCE and CE Broker provider numbers, the certification types that hold members' license, NREMT and National EMS ID numbers, and the column lists of the CE / CPD training-hours file and the CAPCE, state CE and state roster files.

<a id="field-lmscereportingsettings-capceprovidernumber"></a>
## CE reporting settings / CAPCE provider number

CE reporting settings / CAPCE provider number. The department's CAPCE provider number when it is a CAPCE-accredited provider. Without it no CAPCE completion file is built.

<a id="field-lmscereportingsettings-capceprovidername"></a>
## CE reporting settings / CAPCE provider name

CE reporting settings / CAPCE provider name. The provider name as the department enters it on courses and records; completions naming it count as the department's own CE.

<a id="field-lmscereportingsettings-licensecertificationtypeid"></a>
## CE reporting settings / State license certification type

CE reporting settings / State license certification type. The certification type whose number, area, type and expiry the files report as the member's state license.

<a id="field-lmscereportingsettings-nremtcertificationtypeid"></a>
## CE reporting settings / NREMT certification type

CE reporting settings / NREMT certification type. The certification type whose number and expiry the files report as the NREMT number and next re-registration date.

<a id="field-lmscereportingsettings-nemsidcertificationtypeid"></a>
## CE reporting settings / National EMS ID certification type

CE reporting settings / National EMS ID certification type. The certification type whose number the files report as the member's National EMS ID (NEMSID).

<a id="field-lmscereportingsettings-cebrokerprovidernumber"></a>
## CE reporting settings / CE Broker provider number

CE reporting settings / CE Broker provider number. The CE Broker provider tracking number (Florida) written in column A of the CE Broker file without its 50- prefix.

<a id="field-lmscereportingsettings-columnmaps"></a>
## CE reporting settings / CE file column lists

CE reporting settings / CE file column lists. The department's own column lists, one per file type; a file type without one uses its default columns.

<a id="field-lmscecolumnmap-exporterkey"></a>
## CE reporting settings / CE file type

CE reporting settings / CE file type. Which file a column list belongs to: training-hours, capce, fl-ems, va-ems, nj-ems, fl-fire, state-ce or state-roster.

<a id="field-lmscecolumnmap-columns"></a>
## CE reporting settings / CE file columns

CE reporting settings / CE file columns. The columns of the file, in order.

<a id="field-lmscecolumn-header"></a>
## CE reporting settings / CE column heading

CE reporting settings / CE column heading. The heading written in the file's header row; files without a header row leave it out.

<a id="field-lmscecolumn-source"></a>
## CE reporting settings / CE column value

CE reporting settings / CE column value. Which value the column carries: a member, record, certification or provider field, or fixed text.

<a id="field-lmscecolumn-argument"></a>
## CE reporting settings / CE column format

CE reporting settings / CE column format. A date or number format for the value, or the text of a fixed-text column.

<a id="lms-data-export"></a>
## Training data export

Training data export Department owners and admins export everything the department put into Training: courses with their versions, lessons and media, question banks with answer keys, and every learning record, attempt and certificate as CSV and xAPI statements, with a SCORM 1.2 package per course where the lessons allow. A department can always take its training content and evidence with it, also after the LMS add-on lapses. Before moving to a regional academy's system, the training officer exports everything, loads the xAPI statements into the new record store and keeps the CSV records for the audit. Open Trainings, choose Programs, then Data export, pick Everything or Records only and start the export; download the zip when it is ready.

<a id="lms-ce-reporting"></a>
## CE reporting files

CE reporting files Builds a CE / CPD training-hours file with the columns your regulator or professional body asks for, and for US departments the CAPCE completion file, state CE files (Florida's CE Broker layout and a configurable CSV for other states) and a state-ready roster for systems such as Acadis, with a preview and per-department columns. Completions reach your regulator, professional body, CAPCE or state office without retyping a roster, and nothing is submitted before someone checks the file. Each month the EMS coordinator previews last month's CAPCE file, fixes two members missing a state license number and uploads the CSV to CAPCE. Open Trainings, choose Programs, then CE reporting, enter the provider numbers and the certification types that hold license numbers, then build a file for a date range.
<a id="lms-starter-courses"></a>
## Starter courses

Starter courses Courses written by Resgrid (getting started with Resgrid, incident safety basics, a hazardous materials awareness outline, driver basics) to copy into the department as a draft. A department gets a structured first course in minutes and adapts it to its own procedures; the copy keeps its attribution, and partner slots show where accredited content will plug in. A volunteer department copies Getting started with Resgrid, adds its radio channels and station names, and assigns it to every new member after approval. Needs the LMS add-on and Manage Courses; open Trainings, Course library, Starter courses. Starters carry no standard's text and no accreditation.
<a id="setting-lmsstandardssettings"></a>
## xAPI (LRS) settings

xAPI (LRS) settings. Whether LMS course and lesson results are sent as xAPI statements to the department's Learning Record Store, with which key and secret, and whether members are identified by e-mail address.

<a id="field-lmsstandardssettings-xapienabled"></a>
## xAPI (LRS) settings / Send xAPI statements

xAPI (LRS) settings / Send xAPI statements. Course and lesson results (attempted, completed, passed, failed) are queued and sent to the LRS by the standards worker, with retries.

<a id="field-lmsstandardssettings-lrsendpoint"></a>
## xAPI (LRS) settings / LRS endpoint

xAPI (LRS) settings / LRS endpoint. The https address of the Learning Record Store's xAPI endpoint; statements are posted to its statements resource.

<a id="field-lmsstandardssettings-lrskey"></a>
## xAPI (LRS) settings / LRS key

xAPI (LRS) settings / LRS key. The basic-auth key (user name) the LRS issued.

<a id="field-lmsstandardssettings-lrssecretencrypted"></a>
## xAPI (LRS) settings / LRS secret

xAPI (LRS) settings / LRS secret. The basic-auth secret, stored encrypted for the department and never shown again.

<a id="field-lmsstandardssettings-shareemailinactor"></a>
## xAPI (LRS) settings / Identify members by e-mail

xAPI (LRS) settings / Identify members by e-mail. Off: each member is a pseudonymous department account. On: the member's e-mail address is sent as the statement's actor.

<a id="lms-content-standards"></a>
## SCORM, cmi5, xAPI and LTI

SCORM, cmi5, xAPI and LTI Import SCORM 1.2/2004 and cmi5 packages played in a sandboxed player, launch third-party CE courses over LTI 1.3 with score passback, and send results to an external LRS as xAPI statements. Partner and purchased e-learning runs inside Resgrid and lands on the transcript, and results can flow to the department's own LRS. A CAPCE provider's cardiac course launches from a course lesson over LTI; the score comes back, the lesson completes and the credit lands on the member's transcript. Open Trainings, choose Standards, upload a package or register an LTI tool, then add a SCORM, cmi5 or LTI lesson to a course.

<a id="setting-stepuppolicy"></a>
## Step-Up Verification Policy

Step-Up Verification Policy. Where members must confirm it is them again (signatures, read-and-sign, approvals, protected data), whether it is optional, and whether a password or an identity provider sign-in may be used besides two-factor authentication. Check the members without two-factor authentication the page lists before requiring it.

Default: missing = today's behaviour: signatures and approvals require two-factor step-up, read-and-sign follows the protocol library switch, protected data follows the ADP exemptions. Source: DepartmentSettingTypes.StepUpPolicy.

<a id="field-departmentstepuppolicy-areas"></a>
## Step-Up Verification Policy / Areas

Step-Up Verification Policy / Areas. One entry per area that differs from the default. An area with no entry requires two-factor step-up where it did before.

Default: empty (every area at its default). Source: DepartmentStepUpPolicy.Areas.

<a id="field-stepupareasetting-area"></a>
## Step-Up Area / Area

Step-Up Area / Area. Which actions the entry is for: 1 signatures, 2 read-and-sign acknowledgments, 3 approval decisions, 4 protected data.

Default: unset. Source: StepUpAreaSetting.Area.

<a id="field-stepupareasetting-optional"></a>
## Step-Up Area / Optional

Step-Up Area / Optional. Step-up is not required for signatures or approval decisions: they are signed with the signed-in session unless the member has verified recently. Read-and-sign uses the protocol library switch and protected data the ADP exemptions instead.

Default: false (required). Source: StepUpAreaSetting.Optional.

<a id="field-stepupareasetting-alternativemethods"></a>
## Step-Up Area / Alternative Methods

Step-Up Area / Alternative Methods. Kinds of verification accepted besides two-factor authentication: 2 a fresh sign-in with the identity provider, 4 the account password entered again. Neither is a second factor; for protected data only the managing member can change this, and a change ends the grants already issued.

Default: 0 (two-factor authentication only). Source: StepUpAreaSetting.AlternativeMethods.

<a id="workforce-pro-seats"></a>
### Workforce Pro seats

Switch Workforce Pro on for the members whose wellness and workplace exposure records your organization keeps, within the seats your 25-member packs buy. Officers and managers acting on others need their permission, not a seat. Switching a member off never deletes a record: their records become read-only. If the add-on ends, existing records stay readable and exportable.
