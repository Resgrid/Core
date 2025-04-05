// From https://github.com/grandchamp/Identity.Dapper

namespace Resgrid.Repositories.DataRepository.Configs
{
	public abstract class SqlConfiguration
	{
		protected SqlConfiguration() { }

		public string SchemaName { get; set; }
		public string ParameterNotation { get; set; }
		public string TableColumnStartNotation { get; set; }
		public string TableColumnEndNotation { get; set; }
		public string InsertGetReturnIdCommand { get; set; }
		public string QueryPrefix { get; set; }

		#region Common
		public string InsertQuery { get; set; }
		public string DeleteQuery { get; set; }
		public string DeleteMultipleQuery { get; set; }
		public string UpdateQuery { get; set; }
		public string SelectByIdQuery { get; set; }
		public string SelectAllQuery { get; set; }
		public string SelectByDepartmentIdQuery { get; set; }
		public string SelectByUserIdQuery { get; set; }
		#endregion Common

		#region Action Logs
		public string ActionLogsTable { get; set; }

		public string SelectLastActionLogsForDepartmentQuery { get; set; }
		public string SelectActionLogsByUserIdQuery { get; set; }
		public string SelectALogsByUserInDateRangQuery { get; set; }
		public string SelectALogsByDateRangeQuery { get; set; }
		public string SelectALogsByDidQuery { get; set; }
		public string SelectLastActionLogForUserQuery { get; set; }
		public string SelectActionLogsByCallIdTypeQuery { get; set; }
		public string SelectPreviousActionLogsByUserQuery { get; set; }
		public string SelectLastActionLogByUserIdQuery { get; set; }
		public string SelectActionLogsByCallIdQuery {get;set;}
		#endregion Action Logs

		#region Department Members
		public string DepartmentMembersTable { get; set; }

		public string SelectMembersUnlimitedQuery { get; set; }
		public string SelectMembersWithinLimitsQuery { get; set; }
		public string SelectMembersByDidUserIdQuery { get; set; }
		public string SelectMembersByUserIdQuery { get; set; }
		public string SelectMembersUnlimitedInclDelQuery { get; set; }

		#endregion Department Members

		#region Department Settings
		public string DepartmentSettingsTable { get; set; }
		public string SelectDepartmentSettingByDepartmentIdTypeQuery { get; set; }
		public string SelectDepartmentSettingByTypeUserIdQuery { get; set; }
		public string SelectDepartmentSettingBySettingAndTypeQuery { get; set; }
		public string SelectAllDepartmentManagerInfoQuery { get; set; }
		public string SelectDepartmentManagerInfoByEmailQuery { get; set; }
		public string SelectMessagesByUserQuery { get;set; }
		#endregion Department Settings

		#region Invites
		public string InvitesTable { get; set; }
		public string SelectInviteByCodeQuery { get; set; }
		public string SelectInviteByEmailQuery { get; set; }
		#endregion Invites

		#region Inventory
		public string InventoryTable { get; set; }
		public string InventoryTypesTable { get; set; }
		public string SelectInventoryByTypeIdQuery { get; set; }
		public string SelectInventoryByDIdQuery { get; set; }
		public string SelectInventoryByInventoryIdQuery { get; set; }
		public string DeleteInventoryByGroupIdQuery { get; set; }
		#endregion Inventory

		#region Queues
		public string QueueItemsTable { get; set; }

		public string SelectQueueItemByTypeQuery { get; set; }
		#endregion Queues

		#region Certifications
		public string CertificationsTable { get; set; }

		public string SelectCertsByUserQuery { get; set; }
		#endregion Certifications

		#region Permissions
		public string PermissionsTable { get; set; }

		public string SelectPermissionByDepartmentTypeQuery { get; set; }
		#endregion Permissions

		#region Department Links
		public string DepartmentLinksTable { get; set; }

		public string SelectAllLinksForDepartmentQuery { get; set; }
		public string SelectAllLinkForDepartmentIdQuery { get; set; }
		#endregion Department Links

		#region Departments
		public string DepartmentsTable { get; set; }
		public string DepartmentCallPruningTable { get; set; }
		public string SelectDepartmentByLinkCodeQuery { get; set; }
		public string SelectDepartmentByIdQuery { get; set; }
		public string SelectCallPruningByDidQuery { get; set; }
		public string SelectDepartmentReportByDidQuery { get; set; }
		public string SelectDepartmentByNameQuery { get; set; }
		public string SelectDepartmentByUsernameQuery { get;set; }
		public string SelectDepartmentByUserIdQuery { get; set; }
		public string SelectValidDepartmentByUsernameQuery { get; set; }
		public string SelectDepartmentStatsByUserDidQuery { get; set; }
		#endregion Departments

		#region Personnel Roles
		public string PersonnelRolesTable { get; set; }
		public string PersonnelRoleUsersTable { get; set; }
		public string SelectRoleByDidAndNameQuery { get; set; }
		public string SelectRolesByDidAndUserQuery { get; set; }
		public string SelectRoleUsersByRoleQuery { get; set; }
		public string SelectRoleUsersByUserQuery { get; set; }
		public string SelectRoleUsersByDidQuery { get; set; }
		public string SelectRolesByDidQuery { get; set; }
		public string SelectRolesByRoleIdQuery { get; set; }
		#endregion Personnel Roles

		#region Messages
		public string MessagesTable { get; set; }
		public string MessageRecipientsTable { get; set; }
		public string SelectInboxMessagesByUserQuery { get; set; }
		public string SelectSentMessagesByUserQuery { get; set; }
		public string SelectUnreadMessageCountQuery { get; set; }
		public string SelectMessageRecpByMessageUsQuery { get; set; }
		public string SelectMessageRecpsByUserQuery { get; set; }
		public string SelectMessageByIdQuery { get; set; }
		public string UpdateRecievedMessagesAsDeletedQuery { get; set; }
		public string UpdateRecievedMessagesAsReadQuery { get; set; }
		#endregion Messages

		#region Resource Orders
		public string ResourceOrdersTable { get; set; }
		public string ResourceOrderFillsTable { get;set; }
		public string ResourceOrderItemsTable {get;set;}
		public string ResourceOrderSettingsTable {get;set;}
		public string ResourceOrderFillUnitsTable { get; set; }
		public string SelectAllOpenOrdersQuery { get; set; }
		public string UpdateOrderFillStatusQuery {get;set;}
		public string SelectAllOpenNonDVisibleOrdersQuery { get; set; }
		public string SelectAllItemsByOrderIdQuery { get; set; }
		public string SelectItemsByResourceOrderIdQuery { get; set; }
		public string SelectOrderFillUnitsByFillIdQuery { get; set; }
		#endregion Resource Orders

		#region Distribution Lists
		public string DistributionListsTable { get; set; }
		public string DistributionListMembersTable { get; set; }
		public string SelectDListByEmailQuery { get; set; }
		public string SelectAllEnabledDListsQuery { get; set; }
		public string SelectDListMembersByListIdQuery { get; set; }
		public string SelectDListMembersByUserQuery { get; set; }
		public string SelectDListByIdQuery { get; set; }
		public string SelectDListsByDIdQuery { get; set; }
		#endregion Distribution Lists

		#region Custom States
		public string CustomStatesTable { get; set; }
		public string CustomStateDetailsTable { get; set; }
		public string SelectStatesByDidUserQuery { get; set; }
		public string SelectStatesByIdQuery { get; set; }
		#endregion Custom States

		#region Training
		public string TrainingsTable { get; set; }
		public string TrainingAttachmentsTable { get; set; }
		public string TrainingUsersTable { get; set; }
		public string TrainingQuestionsTable { get; set; }
		public string TrainingQuestionAnswersTable { get; set; }
		public string SelectTrainingUserByTandUIdQuery { get; set; }
		public string SelectTrainingsByDIdQuery { get; set; }
		public string SelectTrainingQuestionsByTrainIdQuery { get; set; }
		public string SelectTrainingByIdQuery { get; set; }
		public string SelectTrainingAttachmentsBytIdQuery { get; set; }
		#endregion Training

		#region Calendar
		public string CalendarItemsTable { get; set; }
		public string CalendarItemAttendeesTable { get; set; }
		public string CalendarItemTypesTable { get; set; }
		public string SelectCalendarItemByRecurrenceIdQuery { get; set; }
		public string DeleteCalendarItemQuery { get; set; }
		public string SelectCalendarItemAttendeeByUserQuery { get; set; }
		public string SelectCalendarItemsByDateQuery { get; set; }
		public string SelectCalendarItemByIdQuery { get; set; }
		public string SelectCalendarItemByDIdQuery { get; set; }
		#endregion Calendar

		#region User Profile
		public string UserProfilesTable { get; set; }
		public string SelectProfileByUserIdQuery { get; set; }
		public string SelectProfileByMobileQuery { get; set; }
		public string SelectProfileByHomeQuery { get; set; }
		public string SelectProfilesByIdsQuery { get; set; }
		public string SelectAllProfilesByDIdQuery { get; set; }
		public string SelectAllNonDeletedProfilesByDIdQuery { get; set; }
		#endregion User Profile

		#region Logs
		public string LogsTable { get; set; }
		public string LogUsersTable { get; set; }
		public string LogUnitsTable { get; set; }
		public string CallLogsTable { get; set; }
		public string LogAttachmentsTable { get; set; }
		public string SelectLogsByUserIdQuery { get; set; }
		public string SelectCallLogsByUserIdQuery { get; set; }
		public string SelectLogsByCallIdQuery { get; set; }
		public string SelectLogUsersByUserIdQuery { get; set; }
		public string SelectLogsByGroupIdQuery { get; set; }
		public string SelectLogAttachmentByLogIdQuery { get; set; }
		public string SelectCallLogsByCallIdQuery { get;set; }
		public string SelectLogUsersByLogIdQuery { get; set; }
		public string SelectLogUnitsByLogIdQuery { get; set; }
		public string SelectLogYearsByDeptQuery { get; set; }
		public string SelecAllLogsByDidYearQuery { get; set; }
		#endregion Logs

		#region Units
		public string UnitsTable { get; set; }
		public string UnitLogsTable { get; set; }
		public string UnitRolesTable { get; set; }
		public string UnitTypesTable { get; set; }
		public string UnitStatesTable { get; set; }
		public string UnitStateRolesTable { get; set; }
		public string UnitLocationsTable { get; set; }
		public string UnitActiveRolesTable { get; set; }
		public string SelectUnitStatesByUnitIdQuery { get; set; }
		public string SelectLastUnitStateByUnitIdQuery { get; set; }
		public string SelectLastUnitStateByUnitIdTimeQuery { get; set; }
		public string SelectUnitByDIdNameQuery { get; set; }
		public string SelectUnitTypeByDIdNameQuery { get; set; }
		public string SelectUnitLogsByUnitIdQuery { get; set; }
		public string SelectUnitRolesByUnitIdQuery { get; set; }
		public string SelectUnitsByGroupIdQuery { get; set; }
		public string SelectCurrentRolesByUnitIdQuery { get; set; }
		public string SelectLatestUnitLocationByUnitId { get; set; }
		public string SelectLatestUnitLocationByUnitIdTimeQuery { get; set; }
		public string SelectUnitStatesByCallIdQuery { get; set; }
		public string SelectUnitByDIdTypeQuery { get; set; }
		public string SelectLastUnitStatesByDidQuery { get; set; }
		public string SelectUnitStateByUnitStateIdQuery { get; set; }
		public string SelectUnitActiveRolesByUnitIdQuery { get; set; }
		public string DeleteUnitActiveRolesByUnitIdQuery { get; set; }
		public string SelectActiveRolesForUnitsByDidQuery { get; set; }
		public string SelectUnitsByDIdQuery { get; set; }
		#endregion Units

		#region Shifts
		public string ShiftsTable { get; set; }
		public string ShiftPersonsTable { get; set; }
		public string ShiftDaysTable { get; set; }
		public string ShiftGroupsTable { get; set; }
		public string ShiftSignupsTable { get; set; }
		public string ShiftSignupTradesTable { get; set; }
		public string ShiftSignupTradeUsersTable { get; set; }
		public string ShiftSignupTradeUserShiftsTable { get; set; }
		public string ShiftStaffingsTable { get; set; }
		public string ShiftGroupRolesTable { get; set; }
		public string ShiftStaffingPersonsTable { get; set; }
		public string ShiftGroupAssignmentsTable { get; set; }
		public string SelectShiftStaffingByDayQuery { get; set; }
		public string SelectShiftGroupByGroupQuery { get; set; }
		public string SelectShiftAssignmentByGroupQuery { get; set; }
		public string SelectShiftSignupTradeUsersByTradeIdQuery { get; set; }
		public string SelectShiftSignupByShiftIdQuery { get; set; }
		public string SelectShiftSignupTradeBySourceIdQuery { get; set; }
		public string SelectShiftSignupTradeByTargetIdQuery { get; set; }
		public string SelectShiftDaysByShiftIdQuery { get; set; }
		public string SelectShiftAndDaysByShiftIdQuery { get; set; }
		public string SelectShiftAndDaysQuery { get; set; }
		public string SelectShiftAndDaysJSONQuery { get; set; }
		public string SelectShiftSignupByUserIdQuery { get; set; }
		public string SelectShiftSignupTradeByUserIdQuery { get; set; }
		public string SelectOpenShiftSignupTradesByUserIdQuery { get; set; }
		public string SelectShiftAndDaysByDIdQuery { get; set; }
		public string SelectShiftByShiftIdQuery { get; set; }
		public string SelectShiftPersonByShiftIdQuery { get; set; }
		public string SelectShiftGroupByShiftIdQuery { get; set; }
		public string SelectShiftDayByIdQuery { get; set; }
		public string SelectShiftSignupByShiftIdDateQuery { get; set; }
		public string SelectShiftGroupRolesByGroupIdQuery { get; set; }
		public string SelectShiftTradeAndSourceByUserIdQuery { get; set; }
		public string SelectShiftByShiftIdJSONQuery { get; set; }
		public string SelectShiftsByDidJSONQuery { get; set; }
		public string SelectShiftSignupsByGroupIdAndDateQuery { get; set; }
		#endregion Shifts

		#region Calls
		public string CallsTable { get; set; }
		public string CallDispatchesTable { get; set; }
		public string CallDispatchGroupsTable { get; set; }
		public string CallDispatchUnitsTable { get; set; }
		public string CallDispatchRolesTable { get; set; }
		public string CallTypesTable { get; set; }
		public string CallNotesTable { get; set; }
		public string CallAttachmentsTable { get; set; }
		public string DepartmentCallPrioritiesTable { get; set; }
		public string CallProtocolsTable { get; set; }
		public string CallContactsTable { get; set; }
		public string SelectAllCallsByDidDateQuery { get; set; }
		public string SelectCallsCountByDidDateQuery { get; set; }
		public string SelectAllClosedCallsByDidDateQuery { get; set; }
		public string SelectAllCallDispatchesByGroupIdQuery { get; set; }
		public string SelectCallAttachmentByCallIdTypeQuery { get; set; }
		public string SelectAllOpenCallsByDidDateQuery { get; set; }
		public string SelectAllCallsByDidLoggedOnQuery { get; set; }
		public string UpdateUserDispatchesAsSentQuery { get; set; }
		public string SelectCallProtocolsByCallIdQuery { get; set; }
		public string SelectAllCallDispatchesByCallIdQuery { get; set; }
		public string SelectCallAttachmentByCallIdQuery { get; set; }
		public string SelectCallNotesByCallIdQuery { get; set; }
		public string SelectAllCallGroupDispsByCallIdQuery { get; set; }
		public string SelectAllCallUnitDispsByCallIdQuery { get; set; }
		public string SelectAllCallRoleDispsByCallIdQuery { get; set; }
		public string SelectCallYearsByDeptQuery { get; set; }
		public string SelectAllClosedCallsByDidYearQuery { get; set; }
		public string SelectNonDispatchedScheduledCallsByDateQuery { get; set; }
		public string SelectNonDispatchedScheduledCallsByDidQuery { get; set; }
		public string SelectCallsByContactQuery { get; set; }
		#endregion Calls

		#region Dispatch Protocols
		public string DispatchProtocolsTable { get; set; }
		public string DispatchProtocolTriggersTable { get; set; }
		public string DispatchProtocolAttachmentsTable { get; set; }
		public string DispatchProtocolQuestionsTable { get; set; }
		public string DispatchProtocolQuestionAnswersTable { get; set; }
		public string SelectProtocolByIdQuery { get; set; }
		public string SelectProtocolsByDIdQuery { get; set; }
		public string SelectProtocolQuestionsByProIdQuery { get; set; }
		public string SelectProtocolAttachmentsByProIdQuery { get; set; }
		public string SelectProtocolTriggersByProIdQuery { get; set; }
		#endregion Dispatch Protocols

		#region Department Groups
		public string DepartmentGroupsTable { get; set; }
		public string DepartmentGroupMembersTable { get; set; }
		public string SelectGroupMembersByGroupIdQuery { get; set; }
		public string SelectGroupMembersByUserDidQuery { get; set; }
		public string SelectAllGroupsByDidQuery { get; set; }
		public string SelectAllGroupsByParentIdQuery { get; set; }
		public string SelectGroupByDispatchCodeQuery { get; set; }
		public string SelectGroupByMessageCodeQuery { get; set; }
		public string SelectGroupByGroupIdQuery { get; set; }
		public string DeleteGroupMembersByGroupIdDidQuery { get; set; }
		public string SelectGroupAdminsByDidQuery { get; set; }
		#endregion Department Groups

		#region Payments
		public string PaymentsTable { get; set; }
		public string SelectGetDepartmentPlanCountsQuery { get; set; }
		public string SelectPaymentByTransactionIdQuery { get; set; }
		public string SelectPaymentsByDIdQuery { get; set; }
		public string SelectPaymentByIdQuery { get; set; }
		#endregion Payments

		#region User States
		public string UserStatesTable { get; set; }
		public string SelectLatestUserStatesByDidQuery { get; set; }
		public string SelectUserStatesByUserIdQuery { get; set; }
		public string SelectLastUserStatesByUserIdQuery { get; set; }
		public string SelectPreviousUserStatesByUserIdQuery { get; set; }
		public string SelectUserStatesByDIdDateRangeQuery { get; set; }
		#endregion User States

		#region Plans
		public string PlansTable { get; set; }
		public string PlanLimitsTable { get; set; }
		public string SelectPlanByPlanIdQuery { get; set; }
		#endregion Plans

		#region Mapping
		public string PoisTableName { get; set; }
		public string POITypesTableName { get; set; }
		public string SelectPoiTypesByDIdQuery { get; set; }
		public string SelectPoiTypeByIdQuery { get; set; }
		#endregion Mapping

		#region Notes
		public string NotesTableName { get; set; }
		public string SelectNotesByDIdQuery { get; set; }
		#endregion Notes

		#region Forms
		public string FormsTable { get; set; }
		public string FormAutomationsTable { get; set; }
		public string SelectFormByIdQuery { get; set; }
		public string SelectFormsByDIdQuery { get; set; }
		public string SelectFormAutomationsByFormIdQuery { get; set; }
		public string SelectNonDeletedFormsByDIdQuery { get; set; }
		public string UpdateFormsToEnableQuery { get; set; }
		public string UpdateFormsToDisableQuery { get; set; }
		#endregion Forms

		#region Voice
		public string DepartmentVoiceTableName { get; set; }
		public string DepartmentVoiceChannelsTableName { get; set; }
		public string DepartmentVoiceUsersTableName { get; set; }
		public string SelectVoiceByDIdQuery { get; set; }
		public string SelectVoiceChannelsByVoiceIdQuery { get; set; }
		public string SelectVoiceUserByUserIdQuery { get; set; }
		public string SelectVoiceChannelsByDIdQuery { get; set; }
		#endregion Voice

		#region Unit States
		public string SelectUnitStatesByUnitInDateRangeQuery { get; set; }
		#endregion Unit States

		#region Workshifts
		public string WorkshiftsTable { get; set; }
		public string WorkshiftDaysTable { get; set; }
		public string WorkshiftEntitiesTable { get; set; }
		public string WorkshiftFillsTable { get; set; }
		public string SelectAllWorkshiftsAndDaysByDidQuery { get; set; }
		public string SelectWorkshiftByIdQuery { get; set; }
		public string SelectWorkshiftEntitiesByWorkshiftIdQuery { get; set; }
		public string SelectWorkshiftFillsByWorkshiftIdQuery { get; set; }
		#endregion Workshifts

		#region CallReferences
		public string CallReferencesTable { get; set; }
		public string SelectAllCallReferencesBySourceCallIdQuery { get; set; }
		public string SelectAllCallReferencesByTargetCallIdQuery { get; set; }
		#endregion CallReferences

		#region Scheduled Tasks
		public string ScheduledTasksTable { get; set; }
		public string SelectAllUpcomingOrRecurringReportTasksQuery { get; set; }
		#endregion Scheduled Tasks

		#region Contacts
		public string ContactsTableName { get; set; }
		public string ContactAssociationsTableName { get; set; }
		public string ContactCategoriesTableName { get; set; }
		public string ContactNotesTableName { get; set; }
		public string ContactNoteTypesTableName { get; set; }
		public string CallContactTableName { get; set; }
		public string SelectContactsByCategoryIdQuery { get; set; }
		public string SelectContactNotesByContactIdQuery { get; set; }
		public string SelectAllCallContactsByCallIdQuery { get; set; }
		#endregion Contacts

		// Identity

		#region Table Names
		public string RoleTable { get; set; }
		public string UserTable { get; set; }
		public string UserClaimTable { get; set; }
		public string UserLoginTable { get; set; }
		public string UserRoleTable { get; set; }
		public string RoleClaimTable { get; set; }
		#endregion

		#region Role Queries
		public string InsertRoleQuery { get; set; }
		public string DeleteRoleQuery { get; set; }
		public string UpdateRoleQuery { get; set; }
		public string SelectRoleByNameQuery { get; set; }
		public string SelectRoleByIdQuery { get; set; }
		public string SelectClaimByRoleQuery { get; set; }
		public string InsertRoleClaimQuery { get; set; }
		public string DeleteRoleClaimQuery { get; set; }
		#endregion

		#region User Queries
		public string InsertUserQuery { get; set; }
		public string DeleteUserQuery { get; set; }
		public string UpdateUserQuery { get; set; }
		public string SelectUserByUserNameQuery { get; set; }
		public string SelectUserByEmailQuery { get; set; }
		public string SelectUserByIdQuery { get; set; }
		public string InsertUserClaimQuery { get; set; }
		public string InsertUserLoginQuery { get; set; }
		public string InsertUserRoleQuery { get; set; }
		public string GetUserLoginByLoginProviderAndProviderKeyQuery { get; set; }
		public string GetClaimsByUserIdQuery { get; set; }
		public string GetUserLoginInfoByIdQuery { get; set; }
		public string GetUsersByClaimQuery { get; set; }
		public string GetUsersInRoleQuery { get; set; }
		public string GetRolesByUserIdQuery { get; set; }
		public string IsInRoleQuery { get; set; }
		public string RemoveClaimsQuery { get; set; }
		public string RemoveUserFromRoleQuery { get; set; }
		public string RemoveLoginForUserQuery { get; set; }
		public string UpdateClaimForUserQuery { get; set; }
		#endregion
	}
}
