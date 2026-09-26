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

Personnel roles Roles such as Firefighter, EMT or Officer. Describe responsibility consistently without assuming that a role proves qualification. A volunteer department creates Station 1 and Station 2 groups, invites 24 members and assigns Engineer and Firefighter roles. Create the roles you dispatch or report on, and assign them to members.

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
## Require2 FAFor Admins

Require2 FAFor Admins. Administrator MFA requirement mode. Verify actual factors and recovery for each administrator before enforcement changes.

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

Create calls. Allows starting a call. The department-level action uses each member’s actual administrator state and personnel roles; later dispatch validation remains authoritative.

Default: No permission row: everyone; no group lock. Source: PermissionTypes.CreateCall.

<a id="permission-createnote"></a>
## Create notes

Create notes. Allows creating a department note. The action does not grant access to protected note contents or another department.

Default: No permission row: everyone; no group lock. Source: PermissionTypes.CreateNote.

<a id="permission-viewpersonalinfo"></a>
## View personal information

View personal information. Controls the personal-information permission gate. Protected fields still require their own current authorization and data protection grant.

Default: No permission row: everyone; no group lock. Source: PermissionTypes.ViewPersonalInfo.

<a id="permission-viewgroupusers"></a>
## View personnel

View personnel. Controls visibility of personnel targets. A group lock can narrow target scope; administrators of a target group or its ancestors use the owning visibility policy.

Default: No permission row: everyone; no group lock. Source: PermissionTypes.ViewGroupUsers.

<a id="permission-viewgroupunits"></a>
## View units

View units. Controls visibility of unit targets and their assigned stations. Ungrouped viewers and unassigned units do not share a group.

Default: No permission row: everyone; no group lock. Source: PermissionTypes.ViewGroupUnits.

<a id="permission-canseepersonnellocations"></a>
## View personnel locations

View personnel locations. Controls the location visibility gate for each viewer and person. The preview counts access pairs, not current map markers, tracking consent or GPS availability.

Default: No permission row: everyone; no group lock. Source: PermissionTypes.CanSeePersonnelLocations.

<a id="permission-canseeunitlocations"></a>
## View unit locations

View unit locations. Controls the location visibility gate for each viewer and unit. A group lock refers to the unit’s station and the owning group hierarchy, not any station a viewer can otherwise see.

Default: No permission row: everyone; no group lock. Source: PermissionTypes.CanSeeUnitLocations.

<a id="permission-addpersonnel"></a>
## Add Personnel

Add Personnel. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.AddPersonnel.

<a id="permission-removepersonnel"></a>
## Remove Personnel

Remove Personnel. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.RemovePersonnel.

<a id="permission-createtraining"></a>
## Create Training

Create Training. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.CreateTraining.

<a id="permission-createdocument"></a>
## Create Document

Create Document. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.CreateDocument.

<a id="permission-createcalendarentry"></a>
## Create Calendar Entry

Create Calendar Entry. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.CreateCalendarEntry.

<a id="permission-createlog"></a>
## Create Log

Create Log. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.CreateLog.

<a id="permission-createshift"></a>
## Create Shift

Create Shift. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.CreateShift.

<a id="permission-adjustinventory"></a>
## Adjust Inventory

Adjust Inventory. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.AdjustInventory.

<a id="permission-createmessage"></a>
## Create Message

Create Message. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.CreateMessage.

<a id="permission-deletecall"></a>
## Delete Call

Delete Call. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.DeleteCall.

<a id="permission-closecall"></a>
## Close Call

Close Call. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.CloseCall.

<a id="permission-addcalldata"></a>
## Add Call Data

Add Call Data. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.AddCallData.

<a id="permission-contactedit"></a>
## Contact Edit

Contact Edit. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ContactEdit.

<a id="permission-contactview"></a>
## Contact View

Contact View. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ContactView.

<a id="permission-contactdelete"></a>
## Contact Delete

Contact Delete. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ContactDelete.

<a id="permission-createworkflow"></a>
## Create Workflow

Create Workflow. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.CreateWorkflow.

<a id="permission-manageworkflowcredentials"></a>
## Manage Workflow Credentials

Manage Workflow Credentials. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManageWorkflowCredentials.

<a id="permission-viewworkflowruns"></a>
## View Workflow Runs

View Workflow Runs. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ViewWorkflowRuns.

<a id="permission-viewudffields"></a>
## View custom field Fields

View custom field Fields. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ViewUdfFields.

<a id="permission-manageroutes"></a>
## Manage Routes

Manage Routes. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManageRoutes.

<a id="permission-deletelog"></a>
## Delete Log

Delete Log. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.DeleteLog.

<a id="permission-usecalendarsync"></a>
## Use Calendar Sync

Use Calendar Sync. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.UseCalendarSync.

<a id="permission-dispatchapplogin"></a>
## Dispatch App Login

Dispatch App Login. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.DispatchAppLogin.

<a id="permission-commandapplogin"></a>
## Command App Login

Command App Login. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.CommandAppLogin.

<a id="permission-managedepartmentdataprotection"></a>
## Manage Department Data Protection

Manage Department Data Protection. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManageDepartmentDataProtection.

<a id="permission-viewprotectedcalldata"></a>
## View Protected Call Data

View Protected Call Data. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ViewProtectedCallData.

<a id="permission-editprotectedcalldata"></a>
## Edit Protected Call Data

Edit Protected Call Data. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.EditProtectedCallData.

<a id="permission-viewprotectedpersonneldata"></a>
## View Protected Personnel Data

View Protected Personnel Data. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ViewProtectedPersonnelData.

<a id="permission-viewprotectedcontactdata"></a>
## View Protected Contact Data

View Protected Contact Data. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ViewProtectedContactData.

<a id="permission-viewprotectedoperationaldata"></a>
## View Protected Operational Data

View Protected Operational Data. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ViewProtectedOperationalData.

<a id="permission-exportprotecteddata"></a>
## Export Protected Data

Export Protected Data. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ExportProtectedData.

<a id="permission-configureprotecteddataegress"></a>
## Configure Protected Data Egress

Configure Protected Data Egress. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ConfigureProtectedDataEgress.

<a id="permission-breakglassprotecteddata"></a>
## Break Glass Protected Data

Break Glass Protected Data. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.BreakGlassProtectedData.

<a id="permission-createrecord"></a>
## Create Record

Create Record. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.CreateRecord.

<a id="permission-deleterecord"></a>
## Delete Record

Delete Record. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.DeleteRecord.

<a id="permission-reviewrecords"></a>
## Review Records

Review Records. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ReviewRecords.

<a id="permission-approverecords"></a>
## Approve Records

Approve Records. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ApproveRecords.

<a id="permission-finalizerecords"></a>
## Finalize Records

Finalize Records. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.FinalizeRecords.

<a id="permission-amendrecords"></a>
## Amend Records

Amend Records. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.AmendRecords.

<a id="permission-submitrecords"></a>
## Submit Records

Submit Records. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.SubmitRecords.

<a id="permission-exportrecords"></a>
## Export Records

Export Records. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ExportRecords.

<a id="permission-sharerecordsexternally"></a>
## Share Records Externally

Share Records Externally. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ShareRecordsExternally.

<a id="permission-viewrestrictedrecords"></a>
## View Restricted Records

View Restricted Records. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ViewRestrictedRecords.

<a id="permission-viewlegacyrecords"></a>
## View Legacy Records

View Legacy Records. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ViewLegacyRecords.

<a id="permission-viewgrouprecords"></a>
## View Group Records

View Group Records. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ViewGroupRecords.

<a id="permission-managerecorddefinitions"></a>
## Manage Record Definitions

Manage Record Definitions. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManageRecordDefinitions.

<a id="permission-publishrecorddefinitions"></a>
## Publish Record Definitions

Publish Record Definitions. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.PublishRecordDefinitions.

<a id="permission-managerecordreports"></a>
## Manage Record Reports

Manage Record Reports. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManageRecordReports.

<a id="permission-managerecorddisclosures"></a>
## Manage Record Disclosures

Manage Record Disclosures. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManageRecordDisclosures.

<a id="permission-managerecordlegalhold"></a>
## Manage Record Legal Hold

Manage Record Legal Hold. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManageRecordLegalHold.

<a id="permission-reassignrecorddrafts"></a>
## Reassign Record Drafts

Reassign Record Drafts. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ReassignRecordDrafts.

<a id="permission-recordspreventionadmin"></a>
## Records Prevention Admin

Records Prevention Admin. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.RecordsPreventionAdmin.

<a id="permission-managechecklists"></a>
## Manage Checklists

Manage Checklists. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManageChecklists.

<a id="permission-viewchecklistresults"></a>
## View Checklist Results

View Checklist Results. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ViewChecklistResults.

<a id="permission-manageworkorders"></a>
## Manage Work Orders

Manage Work Orders. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManageWorkOrders.

<a id="permission-viewallworkorders"></a>
## View All Work Orders

View All Work Orders. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ViewAllWorkOrders.

<a id="permission-transferinventory"></a>
## Transfer Inventory

Transfer Inventory. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.TransferInventory.

<a id="permission-issueinventory"></a>
## Issue Inventory

Issue Inventory. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.IssueInventory.

<a id="permission-managecontrolledsubstances"></a>
## Manage Controlled Substances

Manage Controlled Substances. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManageControlledSubstances.

<a id="permission-manageinvoicing"></a>
## Manage Invoicing

Manage Invoicing. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManageInvoicing.

<a id="permission-viewinvoicing"></a>
## View Invoicing

View Invoicing. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ViewInvoicing.

<a id="permission-managecertifications"></a>
## Manage Certifications

Manage Certifications. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManageCertifications.

<a id="permission-viewcertifications"></a>
## View Certifications

View Certifications. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ViewCertifications.

<a id="permission-managecertificationsetup"></a>
## Manage Certification Setup

Manage Certification Setup. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManageCertificationSetup.

<a id="permission-managebids"></a>
## Manage Bids

Manage Bids. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManageBids.

<a id="permission-managecontracts"></a>
## Manage Contracts

Manage Contracts. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManageContracts.

<a id="permission-managedeployments"></a>
## Manage Deployments

Manage Deployments. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManageDeployments.

<a id="permission-approvetimereports"></a>
## Approve Time Reports

Approve Time Reports. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ApproveTimeReports.

<a id="permission-managemutualaidreimbursement"></a>
## Manage Mutual Aid Reimbursement

Manage Mutual Aid Reimbursement. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManageMutualAidReimbursement.

<a id="permission-viewinternalcosts"></a>
## View Internal Costs

View Internal Costs. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ViewInternalCosts.

<a id="permission-manageworkforcecompensation"></a>
## Manage Workforce Compensation

Manage Workforce Compensation. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManageWorkforceCompensation.

<a id="permission-viewworkforcecompensation"></a>
## View Workforce Compensation

View Workforce Compensation. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ViewWorkforceCompensation.

<a id="permission-managepaydatareporting"></a>
## Manage Pay Data Reporting

Manage Pay Data Reporting. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ManagePayDataReporting.

<a id="permission-exportpaydatareporting"></a>
## Export Pay Data Reporting

Export Pay Data Reporting. Review this permission on the Security screen. Its default when no policy is saved, group scope, dependent permissions and behavior across web, API and mobile are awaiting a complete consumer review. No live impact preview is available for this permission yet.

Default: Awaiting consumer review; do not infer access from an absent row. Source: PermissionTypes.ExportPayDataReporting.

<a id="table-department-name"></a>
## Department / Name

Department / Name. Department display name. Review member-facing labels and documents that snapshot the name; changing it does not rename historical artifacts.

Default: required. Source: Department.Name.

<a id="table-department-departmenttype"></a>
## Department / Department Type

Department / Department Type. Legacy organization-type label. The operating profile provides multiple sectors; neither establishes clinical scope or response qualifications.

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

Unit Call Dispatch Status To Set. Unit status applied during call dispatch. Minus one or an invalid built-in unit status leaves the status unchanged.

Default: -1. Source: DepartmentSettingTypes.UnitCallDispatchStatusToSet.

<a id="setting-unitcallreleasestatustoset"></a>
## Unit Call Release Status To Set

Unit Call Release Status To Set. Unit status applied at call release. Review unit-type overrides before assuming one department-wide effect.

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

<a id="setting-newcallfieldpolicy"></a>
## New Call Field Policy

New Call Field Policy. Controls optional new-call fields across supported clients. Hidden fields cannot be required; core dispatch fields remain mandatory.

Default: visible, not required. Source: DepartmentSettingTypes.NewCallFieldPolicy.

<a id="setting-groupdispatchscopeconfig"></a>
## Group Dispatch Scope Config

Group Dispatch Scope Config. Group-subtree dispatch visibility with explicit role exceptions. Preview each actor and target scope before changing it.

Default: disabled, no department-wide role exceptions. Source: DepartmentSettingTypes.GroupDispatchScopeConfig.

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

Unit Type Call Status Override / Dispatch Status. Status applied to this unit type on dispatch; minus one leaves it unchanged.

Default: -1. Source: UnitTypeCallStatusOverride.DispatchStatus.

<a id="field-unittypecallstatusoverride-releasestatus"></a>
## Unit Type Call Status Override / Release Status

Unit Type Call Status Override / Release Status. Status applied to this unit type on release; minus one leaves it unchanged.

Default: -1. Source: UnitTypeCallStatusOverride.ReleaseStatus.

<a id="field-newcallfieldpolicy-rules"></a>
## New Call Field Policy / Rules

New Call Field Policy / Rules. Per-field visibility and requiredness for optional call-creation fields. Unconfigured fields remain visible and optional.

Default: empty. Source: NewCallFieldPolicy.Rules.

<a id="field-newcallfieldrule-key"></a>
## New Call Field Rule / Key

New Call Field Rule / Key. One supported built-in call-field key. Name, nature, priority and type cannot be removed through this policy.

Default: unset. Source: NewCallFieldRule.Key.

<a id="field-newcallfieldrule-visible"></a>
## New Call Field Rule / Visible

New Call Field Rule / Visible. Whether the optional call field is shown on supported creation surfaces. Hidden fields cannot be required.

Default: true. Source: NewCallFieldRule.Visible.

<a id="field-newcallfieldrule-required"></a>
## New Call Field Rule / Required

New Call Field Rule / Required. Whether a visible optional field must be supplied. Review imports and each supported client before making a field mandatory.

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

Dispatch Recommendation Config / Max Location Age Seconds. Exclude older unit positions from closest-unit candidates; zero removes the age limit. IncludeStaleLocations changes this behavior.

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

Dispatch Recommendation Config / Use Routed ETA. Re-rank shortlisted candidates using provider travel estimates. The operational path can make external provider calls; Admin Assist does not.

Default: false. Source: DispatchRecommendationConfig.UseRoutedEta.

<a id="field-dispatchrecommendationconfig-etashortlistsize"></a>
## Dispatch Recommendation Config / ETA Shortlist Size

Dispatch Recommendation Config / ETA Shortlist Size. Number of straight-line candidates per requirement sent for routed ETA when enabled. The owning validator caps provider work.

Default: 5. Source: DispatchRecommendationConfig.EtaShortlistSize.

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

Call types and priorities The call types and priorities used to classify calls. Use consistent reporting and run-card matching terms. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Add the call types your dispatchers use and set their priorities.

<a id="custom-statuses"></a>
## Personnel and unit statuses

Personnel and unit statuses The statuses members and units report, such as Responding or On Scene. Use terminology familiar to the department while preserving automation meaning. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Review the built-in statuses and add your own where your terms differ.

<a id="dispatch-settings"></a>
## Dispatch Settings

Dispatch Settings How calls page personnel: by group or by shift, and related automatic status changes. Consistent call types and statuses make dispatching, response tracking and reporting reliable from the first call. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Choose whether dispatch follows groups or shifts, and review the automatic status options.

<a id="email-intake"></a>
## Email and CAD call intake

Email and CAD call intake Create calls automatically from CAD, paging or email dispatches. Review parsing, routing and observed integration failures before activation. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Choose your CAD or paging format and connect the mailbox that receives dispatches.

<a id="run-cards"></a>
## Run cards and recommendations

Run cards and recommendations Recommended resources for each call type or area. Review candidate coverage before enabling operational use. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Build run cards for your most common calls, then turn on recommendations.

<a id="calls"></a>
## Calls

Calls Active calls and their dispatch status. Consistent call types and statuses make dispatching, response tracking and reporting reliable from the first call. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Collect your call types, the statuses members report while responding, and the details of any CAD or paging system that should create calls.

<a id="new-call"></a>
## New Call

New Call Create and dispatch a call. Consistent call types and statuses make dispatching, response tracking and reporting reliable from the first call. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Collect your call types, the statuses members report while responding, and the details of any CAD or paging system that should create calls.

<a id="archived-calls"></a>
## Archived Calls

Archived Calls Closed and past calls. Consistent call types and statuses make dispatching, response tracking and reporting reliable from the first call. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Collect your call types, the statuses members report while responding, and the details of any CAD or paging system that should create calls.

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

Call Settings The mailbox and format used to import calls from CAD or paging email. Consistent call types and statuses make dispatching, response tracking and reporting reliable from the first call. A department adds Structure Fire and Medical call types, sets Responding and On Scene statuses and connects its county CAD email feed. Collect your call types, the statuses members report while responding, and the details of any CAD or paging system that should create calls.

<a id="setting-unitssortorder"></a>
## Units Sort Order

Units Sort Order. Sort order for unit lists; does not change unit eligibility or dispatch recommendation ranking.

Default: owning view default. Source: DepartmentSettingTypes.UnitsSortOrder.

<a id="units"></a>
## Units

Units Your apparatus, vehicles and response teams. Units let dispatchers send the right resources and let everyone see which apparatus is available. Engine 1 and Rescue 1 are added with their unit types and assigned to Station 1. Add each unit with its type and home station group.

<a id="unit-types"></a>
## Unit types

Unit types Categories such as Engine, Ladder or Ambulance. Group comparable resources without assuming staffing or qualification. Engine 1 and Rescue 1 are added with their unit types and assigned to Station 1. Create unit types before adding units so each unit can be classified.

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

Enable Modern Notifications. Use supported modern notification behavior. Verify each consumer and channel; provider acceptance is not delivery.

Default: false. Source: DepartmentSettingTypes.EnableModernNotifications.

<a id="module-messagingdisabled"></a>
## Messaging availability

Messaging availability. Controls availability of messages and announcements. Disabling this module can remove navigation and block module operations; it does not delete its records. Turning it on still requires the applicable subscription, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.MessagingDisabled.

<a id="module-messagingnameoverride"></a>
## Messaging menu name

Messaging menu name. Optional display name for messages and announcements in consumers that support the override. It changes the label, not permissions or functionality. This release has no separate name-override control in Module Settings.

Default: unset. Source: DepartmentModuleSettings.MessagingNameOverride.

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

Dashboard The department home page: current status, staffing and activity. Most members use Resgrid through the Responder app. Getting everyone installed and signed in is what lets dispatches reach them. Members install the Responder app, a tablet in Engine 1 runs the Unit app and a station screen shows the Big Board. Decide which devices each role will use and share the app download links with your members.

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

Points of Interest Points of interest such as stations, hospitals and hydrants. A current map shows where responders and apparatus are and puts important locations in front of dispatchers. A department adds its stations and hospitals as points of interest and lets personnel locations expire after 60 minutes so old positions disappear. Add the locations dispatchers need to see.

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

Routes Planned routes, such as patrols, and who is assigned to them. Prepare recurring route information with current source ownership. A department adds its stations and hospitals as points of interest and lets personnel locations expire after 60 minutes so old positions disappear. Confirm members allow location sharing in the Responder app, and decide how long a position should stay on the map.

<a id="setting-allowsignupsformultipleshiftgroups"></a>
## Allow Signups For Multiple Shift Groups

Allow Signups For Multiple Shift Groups. Allow a member to sign up for multiple shift groups. Review overlaps and local coverage rules separately.

Default: false. Source: DepartmentSettingTypes.AllowSignupsForMultipleShiftGroups.

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

<a id="module-trainingdisabled"></a>
## Training availability

Training availability. Controls availability of training. Disabling this module can remove navigation and block module operations; it does not delete its records. Turning it on still requires the applicable subscription, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.TrainingDisabled.

<a id="module-trainingnameoverride"></a>
## Training menu name

Training menu name. Optional display name for training in consumers that support the override. It changes the label, not permissions or functionality. This release has no separate name-override control in Module Settings.

Default: unset. Source: DepartmentModuleSettings.TrainingNameOverride.

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

<a id="contacts"></a>
## Contacts

Contacts Shared contacts such as hospitals, agencies and vendors. Members find the current procedure, preplan or phone number when they need it instead of searching email. A department uploads its standard operating procedures, adds hospital and mutual-aid contacts and creates a medical dispatch protocol. Add the outside contacts members call most.

<a id="documents"></a>
## Documents

Documents Shared documents such as procedures and preplans. Members find the current procedure, preplan or phone number when they need it instead of searching email. A department uploads its standard operating procedures, adds hospital and mutual-aid contacts and creates a medical dispatch protocol. Upload your most-used documents and organize them by category.

<a id="protocols"></a>
## Protocols

Protocols Dispatch protocols with scripted questions and instructions. Members find the current procedure, preplan or phone number when they need it instead of searching email. A department uploads its standard operating procedures, adds hospital and mutual-aid contacts and creates a medical dispatch protocol. Create protocols for the call types that need scripted questions.

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

Records Numbering Config. Number assignment, year reset and sequence formatting for definitions that do not override the department configuration.

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

<a id="module-logsdisabled"></a>
## Logs availability

Logs availability. Controls availability of legacy logs. Disabling this module can remove navigation and block module operations; it does not delete its records. Turning it on still requires the applicable subscription, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.LogsDisabled.

<a id="module-logsnameoverride"></a>
## Logs menu name

Logs menu name. Optional display name for legacy logs in consumers that support the override. It changes the label, not permissions or functionality. This release has no separate name-override control in Module Settings.

Default: unset. Source: DepartmentModuleSettings.LogsNameOverride.

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

Records Numbering Config / Reset Yearly. Restart record sequences each calendar year in the department time zone.

Default: true. Source: RecordsNumberingConfig.ResetYearly.

<a id="field-recordsnumberingconfig-sequencewidth"></a>
## Records Numbering Config / Sequence Width

Records Numbering Config / Sequence Width. Zero-padded sequence width for definitions using department defaults. The Records editor validates the supported width.

Default: 4. Source: RecordsNumberingConfig.SequenceWidth.

<a id="field-recordsnumberingconfig-includeyear"></a>
## Records Numbering Config / Include Year

Records Numbering Config / Include Year. Include the year between the record prefix and sequence number.

Default: true. Source: RecordsNumberingConfig.IncludeYear.

<a id="field-recordsnumberingconfig-pergroupsequence"></a>
## Records Numbering Config / Per Group Sequence

Records Numbering Config / Per Group Sequence. Use separate station/group sequences. This changes numbering, not record visibility.

Default: false. Source: RecordsNumberingConfig.PerGroupSequence.

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

Records Settings Numbering, retention, search and visibility rules for records. Keep incident and activity records with the calls they came from, ready for review and reporting. After each call, the officer completes an incident report from the call and a supervisor reviews it. Know which reports your department completes and who reviews them. Records may need to be enabled for your department.

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
## Import and migration planning

Import and migration planning Plan moving your data from another system into Resgrid. Avoid duplicate resources and verify resulting configuration in Setup Report. A workflow posts new calls to a team channel, and a custom field records the fire district on each call. Have the destination systems and credentials ready, and decide who owns each automation.

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
## Business Operations availability

Business Operations availability. Controls availability of business operations. Disabling this module can remove navigation and block module operations; it does not delete its records. Turning it on still requires the applicable subscription, rollout and permissions.

Default: false. Source: DepartmentModuleSettings.BusinessOperationsDisabled.

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
## Workforce

Workforce Workforce compensation and field cost runs. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Add compensation profiles, then run field costs for deployments.

<a id="new-invoice"></a>
## New Invoice

New Invoice Create an invoice for a customer. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="rate-cards"></a>
## Rate Cards

Rate Cards Rates for units, personnel and fees used on invoices. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="invoice-aging"></a>
## Accounts Receivable Aging

Accounts Receivable Aging Unpaid invoices grouped by how overdue they are. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="billing-settings"></a>
## Billing Settings

Billing Settings The legal name, remit-to address and tax details printed on invoices. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="online-payment-settings"></a>
## Online Payment Settings

Online Payment Settings Connect your Stripe account to take invoice payments online. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="bids"></a>
## Bids

Bids Priced estimates for customers. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="new-bid"></a>
## New Bid

New Bid Create a bid for a customer. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="new-contract"></a>
## New Contract

New Contract Create a service contract for a customer. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="compliance-documents"></a>
## Compliance Documents

Compliance Documents Insurance, licences and bonds, with expiry reminders. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="rate-schedules"></a>
## Rate Schedules

Rate Schedules Contractor rates for crews, vehicles and equipment. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="cal-oes-mars-queue"></a>
## MARS Action Queue

MARS Action Queue MARS claims waiting to be prepared and submitted. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="cal-oes-mars-rates"></a>
## MARS Annual Rates

MARS Annual Rates Annual MARS salary and equipment rates. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="cal-oes-mars-reconciliation"></a>
## MARS Reconciliation

MARS Reconciliation Match MARS payments to the claims you submitted. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="workforce-compensation"></a>
## Compensation Profiles

Compensation Profiles Pay rates and employer costs for staff. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="workforce-annual-facts"></a>
## Annual Pay Facts

Annual Pay Facts Annual earnings and hours used for pay data reporting. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="resource-costs"></a>
## Resource Cost Profiles

Resource Cost Profiles Running costs for units and equipment. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="cost-runs"></a>
## Field Cost Runs

Field Cost Runs Work out the full cost of a bid, call or deployment. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="pay-data-reporting"></a>
## California Pay Data Reporting

California Pay Data Reporting Prepare California pay data reports. Also requires Advanced Data Protection. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="my-demographics"></a>
## My Demographic Response

My Demographic Response Your voluntary answers for California pay data reporting. Bill for standby and contract services and prepare reimbursement claims from records you already keep. An event standby is invoiced from its deployment record, and a mutual-aid claim is prepared for MARS reimbursement. Requires the Workforce and Business Operations add-on. Online payments also need your own payment-provider account.

<a id="addon-business"></a>
## Business Operations

Business Operations The Workforce and Business Operations add-on: invoicing, payments, contracts, bids, MARS reimbursement and workforce costing. Connect administrative commercial work to the department activities it supports. Prepare an event standby invoice or review documented mutual-aid costs. Certifications and Deployment Finance do not require this add-on. Pay-data reporting additionally needs ADP Enabled; online payments require provider setup. No reimbursement or savings are guaranteed.

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
