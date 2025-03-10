using Autofac;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries;
using Resgrid.Repositories.DataRepository.Servers.PostgreSql;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Repositories.DataRepository
{
	public class DataModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<StandardIsolation>().As<IISolationLevel>().InstancePerLifetimeScope();

			if (Config.DataConfig.DatabaseType == Config.DatabaseTypes.Postgres)
			{
				builder.RegisterType<PostgreSqlConfiguration>().As<SqlConfiguration>().InstancePerLifetimeScope();
				builder.RegisterType<PostgreSqlConnectionProvider>().As<IConnectionProvider>().InstancePerLifetimeScope();
			}
			else
			{
				builder.RegisterType<SqlServerConfiguration>().As<SqlConfiguration>().InstancePerLifetimeScope();
				builder.RegisterType<SqlServerConnectionProvider>().As<IConnectionProvider>().InstancePerLifetimeScope();
			}

			builder.RegisterType<QueryList>().As<IQueryList>().InstancePerLifetimeScope();
			builder.RegisterType<UnitOfWork>().As<IUnitOfWork>().InstancePerLifetimeScope();
			builder.RegisterType<QueryFactory>().As<IQueryFactory>().InstancePerLifetimeScope();

			// Custom Repositories
			builder.RegisterType<UserStatesRepository>().As<IUserStatesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentsRepository>().As<IDepartmentsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentSettingsRepository>().As<IDepartmentSettingsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<UnitStatesRepository>().As<IUnitStatesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentMembersRepository>().As<IDepartmentMembersRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PaymentRepository>().As<IPaymentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<UserProfilesRepository>().As<IUserProfilesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallsRepository>().As<ICallsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ActionLogsRepository>().As<IActionLogsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<IdentityRepository>().As<IIdentityRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ScheduledTaskLogsRepository>().As<IScheduledTaskLogsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CalendarItemsRepository>().As<ICalendarItemsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ScheduledTasksRepository>().As<IScheduledTasksRepository>().InstancePerLifetimeScope();

			// Dapper Repositories
			builder.RegisterType<IdentityRepository>().As<IIdentityRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ResourceOrdersRepository>().As<IResourceOrdersRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentNotificationRepository>().As<IDepartmentNotificationRepository>().InstancePerLifetimeScope();
			builder.RegisterType<TrainingRepository>().As<ITrainingRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentCallEmailsRepository>().As<IDepartmentCallEmailsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<MessageRepository>().As<IMessageRepository>().InstancePerLifetimeScope();
			builder.RegisterType<UnitLocationRepository>().As<IUnitLocationRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PushUriRepository>().As<IPushUriRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PlansRepository>().As<IPlansRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallTypesRepository>().As<ICallTypesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentGroupsRepository>().As<IDepartmentGroupsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<AddressRepository>().As<IAddressRepository>().InstancePerLifetimeScope();
			builder.RegisterType<HealthRepository>().As<IHealthRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentGroupMembersRepository>().As<IDepartmentGroupMembersRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentCallPruningRepository>().As<IDepartmentCallPruningRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ShiftsRepository>().As<IShiftsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ClaimsRepository>().As<IClaimsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<IdentityRoleRepository>().As<IIdentityRoleRepository>().InstancePerLifetimeScope();
			builder.RegisterType<IdentityUserRepository>().As<IIdentityUserRepository>().InstancePerLifetimeScope();
			builder.RegisterType<AffiliateRepository>().As<IAffiliateRepository>().InstancePerLifetimeScope();
			builder.RegisterType<AuditLogsRepository>().As<IAuditLogsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<NotesRepository>().As<INotesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<FileRepository>().As<IFileRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PushLogsRepository>().As<IPushLogsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<JobsRepository>().As<IJobsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DocumentRepository>().As<IDocumentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<InvitesRepository>().As<IInvitesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<InventoryRepository>().As<IInventoryRepository>().InstancePerLifetimeScope();
			builder.RegisterType<InventoryTypesRepository>().As<IInventoryTypesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PoisRepository>().As<IPoisRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PoiTypesRepository>().As<IPoiTypesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<QueueItemsRepository>().As<IQueueItemsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallQuickTemplateRepository>().As<ICallQuickTemplateRepository>().InstancePerLifetimeScope();
			builder.RegisterType<InboundMessageEventRepository>().As<IInboundMessageEventRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PersonnelCertificationRepository>().As<IPersonnelCertificationRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentCertificationTypeRepository>().As<IDepartmentCertificationTypeRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PermissionsRepository>().As<IPermissionsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentLinksRepository>().As<IDepartmentLinksRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PaymentProviderEventsRepository>().As<IPaymentProviderEventsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LogEntriesRepository>().As<ILogEntriesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ProcessLogRepository>().As<IProcessLogRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DispatchProtocolRepository>().As<IDispatchProtocolRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DispatchProtocolAttachmentRepository>().As<IDispatchProtocolAttachmentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PersonnelRolesRepository>().As<IPersonnelRolesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PersonnelRoleUsersRepository>().As<IPersonnelRoleUsersRepository>().InstancePerLifetimeScope();
			builder.RegisterType<MessageRecipientRepository>().As<IMessageRecipientRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ResourceOrderSettingsRepository>().As<IResourceOrderSettingsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ResourceOrderFillRepository>().As<IResourceOrderFillRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ResourceOrderItemRepository>().As<IResourceOrderItemRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ResourceOrderFillUnitRepository>().As<IResourceOrderFillUnitRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CommandDefinitionRepository>().As<ICommandDefinitionRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DistributionListRepository>().As<IDistributionListRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DistributionListMemberRepository>().As<IDistributionListMemberRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CustomStateRepository>().As<ICustomStateRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CustomStateDetailRepository>().As<ICustomStateDetailRepository>().InstancePerLifetimeScope();
			builder.RegisterType<TrainingUserRepository>().As<ITrainingUserRepository>().InstancePerLifetimeScope();
			builder.RegisterType<TrainingAttachmentRepository>().As<ITrainingAttachmentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CalendarItemTypeRepository>().As<ICalendarItemTypeRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CalendarItemAttendeeRepository>().As<ICalendarItemAttendeeRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LogsRepository>().As<ILogsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LogUsersRepository>().As<ILogUsersRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallLogsRepository>().As<ICallLogsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LogAttachmentRepository>().As<ILogAttachmentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<UnitsRepository>().As<IUnitsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<UnitLogsRepository>().As<IUnitLogsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<UnitRolesRepository>().As<IUnitRolesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<UnitTypesRepository>().As<IUnitTypesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<UnitStateRoleRepository>().As<IUnitStateRoleRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ShiftPersonRepository>().As<IShiftPersonRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ShiftDaysRepository>().As<IShiftDaysRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ShiftGroupsRepository>().As<IShiftGroupsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ShiftSignupRepository>().As<IShiftSignupRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ShiftSignupTradeRepository>().As<IShiftSignupTradeRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ShiftSignupTradeUserRepository>().As<IShiftSignupTradeUserRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ShiftSignupTradeUserShiftsRepository>().As<IShiftSignupTradeUserShiftsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ShiftStaffingRepository>().As<IShiftStaffingRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ShiftStaffingPersonRepository>().As<IShiftStaffingPersonRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ShiftGroupAssignmentsRepository>().As<IShiftGroupAssignmentsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallDispatchesRepository>().As<ICallDispatchesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallAttachmentRepository>().As<ICallAttachmentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallDispatchGroupRepository>().As<ICallDispatchGroupRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallDispatchUnitRepository>().As<ICallDispatchUnitRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallDispatchRoleRepository>().As<ICallDispatchRoleRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallNotesRepository>().As<ICallNotesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentCallPriorityRepository>().As<IDepartmentCallPriorityRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallProtocolsRepository>().As<ICallProtocolsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<TrainingQuestionRepository>().As<ITrainingQuestionRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LogUnitsRepository>().As<ILogUnitsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ShiftGroupRolesRepository>().As<IShiftGroupRolesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DispatchProtocolQuestionsRepository>().As<IDispatchProtocolQuestionsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DispatchProtocolTriggersRepository>().As<IDispatchProtocolTriggersRepository>().InstancePerLifetimeScope();
			builder.RegisterType<UnitActiveRolesRepository>().As<IUnitActiveRolesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DispatchProtocolQuestionAnswersRepository>().As<IDispatchProtocolQuestionAnswersRepository>().InstancePerLifetimeScope();
			builder.RegisterType<FormsRepository>().As<IFormsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<FormAutomationsRepository>().As<IFormAutomationsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PlanAddonsRepository>().As<IPlanAddonsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PaymentAddonsRepository>().As<IPaymentAddonsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentVoiceRepository>().As<IDepartmentVoiceRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentVoiceChannelRepository>().As<IDepartmentVoiceChannelRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentVoiceUserRepository>().As<IDepartmentVoiceUserRepository>().InstancePerLifetimeScope();
			builder.RegisterType<OidcRepository>().As<IOidcRepository>().InstancePerLifetimeScope();
			builder.RegisterType<SystemAuditsRepository>().As<ISystemAuditsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<AutofillsRepository>().As<IAutofillsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<WorkshiftsRepository>().As<IWorkshiftsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<WorkshiftEntitysRepository>().As<IWorkshiftEntitysRepository>().InstancePerLifetimeScope();
			builder.RegisterType<WorkshiftFillsRepository>().As<IWorkshiftFillsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<WorkshiftDaysRepository>().As<IWorkshiftDaysRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallReferencesRepository>().As<ICallReferencesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DeleteRepository>().As<IDeleteRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentAudioRepository>().As<IDepartmentAudioRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DocumentCategoriesRepository>().As<IDocumentCategoriesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<NoteCategoriesRepository>().As<INoteCategoriesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallContactsRepository>().As<ICallContactsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ContactAssociationsRepository>().As<IContactAssociationsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ContactNoteTypesRepository>().As<IContactNoteTypesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ContactNotesRepository>().As<IContactNotesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ContactsRepository>().As<IContactsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ContactCategoryRepository>().As<IContactCategoryRepository>().InstancePerLifetimeScope();
		}
	}
}
