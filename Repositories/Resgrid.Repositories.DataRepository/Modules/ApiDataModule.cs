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
	public class ApiDataModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<ReadinessProBillingRepository>().As<IReadinessProBillingRepository>().InstancePerLifetimeScope();
			builder.RegisterType<BusinessOperationsBillingRepository>().As<IBusinessOperationsBillingRepository>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentBackOfficeBillingRepository>().As<IIncidentBackOfficeBillingRepository>().InstancePerLifetimeScope();
			builder.RegisterType<AiBillingRepository>().As<IAiBillingRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DedicatedNumberBillingRepository>().As<IDedicatedNumberBillingRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentPhoneNumbersRepository>().As<IDepartmentPhoneNumbersRepository>().InstancePerLifetimeScope();
			builder.RegisterType<AdpBillingRepository>().As<IAdpBillingRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CustomerBillingProfileRepository>().As<ICustomerBillingProfileRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RateCardRepository>().As<IRateCardRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RateCardItemRepository>().As<IRateCardItemRepository>().InstancePerLifetimeScope();
			builder.RegisterType<InvoiceRepository>().As<IInvoiceRepository>().InstancePerLifetimeScope();
			builder.RegisterType<InvoiceLineItemRepository>().As<IInvoiceLineItemRepository>().InstancePerLifetimeScope();
			builder.RegisterType<InvoicePaymentRepository>().As<IInvoicePaymentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<InvoiceNumberSequenceRepository>().As<IInvoiceNumberSequenceRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentPaymentConnectionRepository>().As<IDepartmentPaymentConnectionRepository>().InstancePerLifetimeScope();
			builder.RegisterType<InvoicePaymentRequestRepository>().As<IInvoicePaymentRequestRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PaymentProviderEventRepository>().As<IPaymentProviderEventRepository>().InstancePerLifetimeScope();
			// Workforce & Business Operations plan Phase D (M0213-M0214): certification requirements, settings, credits, unit records.
			builder.RegisterType<PersonnelRoleCertificationRequirementRepository>().As<IPersonnelRoleCertificationRequirementRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentCertificationSettingsRepository>().As<IDepartmentCertificationSettingsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PersonnelCertificationCreditsRepository>().As<IPersonnelCertificationCreditsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<UnitCertificationRepository>().As<IUnitCertificationRepository>().InstancePerLifetimeScope();
			// Workforce & Business Operations plan Phase C (M0218): the free deployment core (deployments, roster, time reports, expenses, attachments).
			builder.RegisterType<DeploymentRepository>().As<IDeploymentRepository>().InstancePerLifetimeScope();
			// Incident Back Office (large-incident-logistics-finance-plan.md §11.2; M0290-M0297): one open-generic ledger repository
			// for every table; the invariant-bearing tables (bed-nights, vendor capacity, time immutability) are enforced in their services.
			builder.RegisterGeneric(typeof(IncidentLogistics.IncidentLedgerRepository<>)).As(typeof(IIncidentLedgerRepository<>)).InstancePerLifetimeScope();
			builder.RegisterType<IncidentLogistics.IncidentLookupRepository>().As<IIncidentLookupRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DeploymentUnitRepository>().As<IDeploymentUnitRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DeploymentPersonnelRepository>().As<IDeploymentPersonnelRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DeploymentEquipmentRepository>().As<IDeploymentEquipmentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DeploymentTimeReportRepository>().As<IDeploymentTimeReportRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DeploymentTimeEntryRepository>().As<IDeploymentTimeEntryRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DeploymentExpenseRepository>().As<IDeploymentExpenseRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DeploymentAttachmentRepository>().As<IDeploymentAttachmentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<TimeReportNumberSequenceRepository>().As<ITimeReportNumberSequenceRepository>().InstancePerLifetimeScope();
			// Workforce & Business Operations plan, Phase C-M2: contractor path (M0215–M0217).
			builder.RegisterType<RateScheduleRepository>().As<IRateScheduleRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RateScheduleEntryRepository>().As<IRateScheduleEntryRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RateScheduleEntryBandRepository>().As<IRateScheduleEntryBandRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RatePremiumRepository>().As<IRatePremiumRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ServiceContractRepository>().As<IServiceContractRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ServiceContractDocumentRequirementRepository>().As<IServiceContractDocumentRequirementRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentComplianceDocumentRepository>().As<IDepartmentComplianceDocumentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<BidRepository>().As<IBidRepository>().InstancePerLifetimeScope();
			builder.RegisterType<BidLineItemRepository>().As<IBidLineItemRepository>().InstancePerLifetimeScope();
			builder.RegisterType<BidNumberSequenceRepository>().As<IBidNumberSequenceRepository>().InstancePerLifetimeScope();
			// Workforce & Business Operations plan, Phase C-M3: Cal OES MARS shadow tables (registry M0219).
			builder.RegisterType<CalOesMarsAgencyProfileRepository>().As<ICalOesMarsAgencyProfileRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CalOesMarsResourceProfileRepository>().As<ICalOesMarsResourceProfileRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CalOesMarsRateProfileRepository>().As<ICalOesMarsRateProfileRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CalOesMarsRateLineRepository>().As<ICalOesMarsRateLineRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CalOesMarsAdministrativeRateInputRepository>().As<ICalOesMarsAdministrativeRateInputRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CalOesMarsAgreementSnapshotRepository>().As<ICalOesMarsAgreementSnapshotRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CalOesMarsWorkItemRepository>().As<ICalOesMarsWorkItemRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CalOesMarsReimbursementLineRepository>().As<ICalOesMarsReimbursementLineRepository>().InstancePerLifetimeScope();
			// Workforce & Business Operations plan, Phase E: protected workforce pay data, costing and CRD reporting (registry M0220–M0223).
			builder.RegisterType<WorkforceEmployerProfileRepository>().As<IWorkforceEmployerProfileRepository>().InstancePerLifetimeScope();
			builder.RegisterType<WorkforceAffiliatedEntityRepository>().As<IWorkforceAffiliatedEntityRepository>().InstancePerLifetimeScope();
			builder.RegisterType<WorkforceEstablishmentRepository>().As<IWorkforceEstablishmentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<WorkforceLaborContractorRepository>().As<IWorkforceLaborContractorRepository>().InstancePerLifetimeScope();
			builder.RegisterType<WorkforceWorkerRepository>().As<IWorkforceWorkerRepository>().InstancePerLifetimeScope();
			builder.RegisterType<WorkforceEmploymentRepository>().As<IWorkforceEmploymentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<WorkforceJobAssignmentRepository>().As<IWorkforceJobAssignmentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<EmployeeCompensationProfileRepository>().As<IEmployeeCompensationProfileRepository>().InstancePerLifetimeScope();
			builder.RegisterType<EmployeePayComponentRepository>().As<IEmployeePayComponentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<EmployeeCostComponentRepository>().As<IEmployeeCostComponentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<WorkforceWorkEntryRepository>().As<IWorkforceWorkEntryRepository>().InstancePerLifetimeScope();
			builder.RegisterType<WorkforceAnnualPayFactRepository>().As<IWorkforceAnnualPayFactRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ResourceCostProfileRepository>().As<IResourceCostProfileRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ResourceCostComponentRepository>().As<IResourceCostComponentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ResourceUsageEntryRepository>().As<IResourceUsageEntryRepository>().InstancePerLifetimeScope();
			builder.RegisterType<FieldCostRunRepository>().As<IFieldCostRunRepository>().InstancePerLifetimeScope();
			builder.RegisterType<FieldCostLineRepository>().As<IFieldCostLineRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PayDataReportingDemographicRepository>().As<IPayDataReportingDemographicRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PayDataReportRunRepository>().As<IPayDataReportRunRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PayDataReportEmployeeSnapshotRepository>().As<IPayDataReportEmployeeSnapshotRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PayDataReportRowRepository>().As<IPayDataReportRowRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PayDataExportArtifactRepository>().As<IPayDataExportArtifactRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentBillingIdentityRepository>().As<IDepartmentBillingIdentityRepository>().InstancePerLifetimeScope();
			builder.RegisterType<WorkOrderRepository>().As<IWorkOrderRepository>().As<IWorkOrderMaintenanceRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ChecklistRepository>().As<IChecklistRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ChecklistReminderRepository>().As<IChecklistReminderRepository>().InstancePerLifetimeScope();
			builder.RegisterType<StandardIsolation>().As<IISolationLevel>().InstancePerLifetimeScope();

			builder.RegisterType<QueryList>().As<IQueryList>().InstancePerLifetimeScope();

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
			builder.RegisterType<ReportingRepository>().As<IReportingRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ReportingRollupRepository>().As<IReportingRollupRepository>().InstancePerLifetimeScope();
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
			builder.RegisterType<InventoryStore>().As<IInventoryStore>().InstancePerLifetimeScope();
			builder.RegisterType<InventoryTypesRepository>().As<IInventoryTypesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PoisRepository>().As<IPoisRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PoiCapabilitiesRepository>().As<IPoiCapabilitiesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallPersonnelReleasesRepository>().As<ICallPersonnelReleasesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallBroadcastOutboxRepository>().As<ICallBroadcastOutboxRepository>().InstancePerLifetimeScope();
			builder.RegisterType<PoiAvailabilitiesRepository>().As<IPoiAvailabilitiesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DestinationRulesRepository>().As<IDestinationRulesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallEmailUpdatesRepository>().As<ICallEmailUpdatesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<AiDestinationAdvicesRepository>().As<IAiDestinationAdvicesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<AiDestinationReviewsRepository>().As<IAiDestinationReviewsRepository>().InstancePerLifetimeScope();
			// Intake channels, messages and voice intake sessions (M0323).
			builder.RegisterType<IntakeChannelsRepository>().As<IIntakeChannelsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<IntakeMessagesRepository>().As<IIntakeMessagesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<IntakeBlockedSendersRepository>().As<IIntakeBlockedSendersRepository>().InstancePerLifetimeScope();
			builder.RegisterType<VoiceIntakeSessionsRepository>().As<IVoiceIntakeSessionsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<IntakeConversationsRepository>().As<IIntakeConversationsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<IntakeConversationMessagesRepository>().As<IIntakeConversationMessagesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RecurringCallsRepository>().As<IRecurringCallsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RecurringCallOccurrencesRepository>().As<IRecurringCallOccurrencesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RecurringCallAlertsRepository>().As<IRecurringCallAlertsRepository>().InstancePerLifetimeScope();
			// Dispatcher desk (M0324)
			builder.RegisterType<ShiftDispatchSettingsRepository>().As<IShiftDispatchSettingsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ShiftDispatchersRepository>().As<IShiftDispatchersRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DispatcherBreaksRepository>().As<IDispatcherBreaksRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DispatcherPresencesRepository>().As<IDispatcherPresencesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DispatcherQueueItemsRepository>().As<IDispatcherQueueItemsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DispatcherQueueEventsRepository>().As<IDispatcherQueueEventsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DispatcherConversationMessagesRepository>().As<IDispatcherConversationMessagesRepository>().InstancePerLifetimeScope();
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
			builder.RegisterType<CommandDefinitionRoleRepository>().As<ICommandDefinitionRoleRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CommandDefinitionRoleUnitTypeRepository>().As<ICommandDefinitionRoleUnitTypeRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CommandDefinitionRolePersonnelRoleRepository>().As<ICommandDefinitionRolePersonnelRoleRepository>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentCommandRepository>().As<IIncidentCommandRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CommandStructureNodeRepository>().As<ICommandStructureNodeRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ResourceAssignmentRepository>().As<IResourceAssignmentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<TacticalObjectiveRepository>().As<ITacticalObjectiveRepository>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentTimerRepository>().As<IIncidentTimerRepository>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentMapAnnotationRepository>().As<IIncidentMapAnnotationRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CommandLogEntryRepository>().As<ICommandLogEntryRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CommandTransferRepository>().As<ICommandTransferRepository>().InstancePerLifetimeScope();
			builder.RegisterType<VoiceTransmissionLogRepository>().As<IVoiceTransmissionLogRepository>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentAdHocUnitRepository>().As<IIncidentAdHocUnitRepository>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentAdHocPersonnelRepository>().As<IIncidentAdHocPersonnelRepository>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentRoleAssignmentRepository>().As<IIncidentRoleAssignmentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ChatChannelRepository>().As<IChatChannelRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ChatChannelAccessRuleRepository>().As<IChatChannelAccessRuleRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ChatChannelMemberRepository>().As<IChatChannelMemberRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ChatMessageRepository>().As<IChatMessageRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ChatMessageEditRepository>().As<IChatMessageEditRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ChatAttachmentRepository>().As<IChatAttachmentRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ChatMessageReactionRepository>().As<IChatMessageReactionRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ChatMessageMentionRepository>().As<IChatMessageMentionRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ChatMessageAckRepository>().As<IChatMessageAckRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ChatMessageFlagRepository>().As<IChatMessageFlagRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ChatModerationActionRepository>().As<IChatModerationActionRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ChatDepartmentSettingRepository>().As<IChatDepartmentSettingRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ChatExportRepository>().As<IChatExportRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ModerationRequestRepository>().As<IModerationRequestRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ModerationReportRepository>().As<IModerationReportRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ModerationActionRepository>().As<IModerationActionRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DistributionListRepository>().As<IDistributionListRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DistributionListMemberRepository>().As<IDistributionListMemberRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CustomStateRepository>().As<ICustomStateRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CustomStateDetailRepository>().As<ICustomStateDetailRepository>().InstancePerLifetimeScope();
			builder.RegisterType<TrainingUserRepository>().As<ITrainingUserRepository>().InstancePerLifetimeScope();
			builder.RegisterType<TrainingQuizAttemptsRepository>().As<ITrainingQuizAttemptsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<TrainingAudiencesRepository>().As<ITrainingAudiencesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<TrainingCompletionHistoriesRepository>().As<ITrainingCompletionHistoriesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<TrainingQuestionFlagsRepository>().As<ITrainingQuestionFlagsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ContentAudienceChangesRepository>().As<IContentAudienceChangesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<MemberLeavePeriodsRepository>().As<IMemberLeavePeriodsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<MemberPinnedItemsRepository>().As<IMemberPinnedItemsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LearningRecordsRepository>().As<ILearningRecordsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<KnowledgeIndexRepository>().As<IKnowledgeIndexRepository>().InstancePerLifetimeScope();
			builder.RegisterType<KnowledgeGapsRepository>().As<IKnowledgeGapsRepository>().InstancePerLifetimeScope();
			// Academy A2/A3 (M0397): member guidance states and major lesson revisions awaiting worker 88
			builder.RegisterType<AcademyGuidanceStatesRepository>().As<IAcademyGuidanceStatesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<AcademyLessonUpdatesRepository>().As<IAcademyLessonUpdatesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LearningRecordFilesRepository>().As<ILearningRecordFilesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<TrainingCategoriesRepository>().As<ITrainingCategoriesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CategoryCreditMappingsRepository>().As<ICategoryCreditMappingsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LearningRecordBackfillsRepository>().As<ILearningRecordBackfillsRepository>().InstancePerLifetimeScope();
			// Member wellness WL0 (M0376): exposure and critical-incident log, post-call flags, screening flags.
			builder.RegisterType<RmsResponderExposuresRepository>().As<IRmsResponderExposuresRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RmsCallExposureFlagsRepository>().As<IRmsCallExposureFlagsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RmsResponderScreeningFlagsRepository>().As<IRmsResponderScreeningFlagsRepository>().InstancePerLifetimeScope();
			// Member wellness WL1 (M0394-M0396): BBP exposure cases, injuries and duty periods, medical clearances.
			builder.RegisterType<RmsExposureIncidentsRepository>().As<IRmsExposureIncidentsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RmsMemberInjuriesRepository>().As<IRmsMemberInjuriesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RmsInjuryDutyPeriodsRepository>().As<IRmsInjuryDutyPeriodsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RmsMedicalClearancesRepository>().As<IRmsMedicalClearancesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCertificateTemplatesRepository>().As<ILmsCertificateTemplatesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsMemberSeatsRepository>().As<ILmsMemberSeatsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsAddonStatesRepository>().As<ILmsAddonStatesRepository>().InstancePerLifetimeScope();
			// Training T3 part 2 (round 4C, M0387-M0390): annual plan, competency frameworks, training requests, report catalog, certification cycles
			// Training T4 part 2 (round 5C, M0407-M0410): skill sheets, evaluations, task books, the skill notification digest
			builder.RegisterType<LmsSkillSheetsRepository>().As<ILmsSkillSheetsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsSkillSheetVersionsRepository>().As<ILmsSkillSheetVersionsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsSkillEvaluationsRepository>().As<ILmsSkillEvaluationsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsSkillEvaluationMembersRepository>().As<ILmsSkillEvaluationMembersRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsSkillEvaluationFilesRepository>().As<ILmsSkillEvaluationFilesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsTaskBookDefinitionsRepository>().As<ILmsTaskBookDefinitionsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsTaskBooksRepository>().As<ILmsTaskBooksRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsSkillNotificationsRepository>().As<ILmsSkillNotificationsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsTrainingPlansRepository>().As<ILmsTrainingPlansRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsTrainingPlanItemsRepository>().As<ILmsTrainingPlanItemsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCompetencyFrameworksRepository>().As<ILmsCompetencyFrameworksRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCompetencyItemsRepository>().As<ILmsCompetencyItemsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCompetencyAlignmentsRepository>().As<ILmsCompetencyAlignmentsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsTrainingRequestsRepository>().As<ILmsTrainingRequestsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsTrainingRequestDecisionsRepository>().As<ILmsTrainingRequestDecisionsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsReportFoldersRepository>().As<ILmsReportFoldersRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsReportDefinitionsRepository>().As<ILmsReportDefinitionsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsReportRunsRepository>().As<ILmsReportRunsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCertificationCycleRulesRepository>().As<ILmsCertificationCycleRulesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCertificationAlertsRepository>().As<ILmsCertificationAlertsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsProgramStatesRepository>().As<ILmsProgramStatesRepository>().InstancePerLifetimeScope();
			// Training T4 part 1 (round 5B, M0403-M0405): class sessions, rosters, instructors, costs, resources and bookings, lesson plans
			builder.RegisterType<LmsClassSessionsRepository>().As<ILmsClassSessionsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsClassEnrollmentsRepository>().As<ILmsClassEnrollmentsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsClassInstructorsRepository>().As<ILmsClassInstructorsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsClassCostLinesRepository>().As<ILmsClassCostLinesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsTrainingResourcesRepository>().As<ILmsTrainingResourcesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsClassResourceBookingsRepository>().As<ILmsClassResourceBookingsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsLessonPlansRepository>().As<ILmsLessonPlansRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsLessonPlanVersionsRepository>().As<ILmsLessonPlanVersionsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsFamiliarizationSourceRepository>().As<ILmsFamiliarizationSourceRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsAddonLapsesRepository>().As<ILmsAddonLapsesRepository>().InstancePerLifetimeScope();
			// Training T2 LMS courses and assessments, A1 Academy (M0359/M0360)
			builder.RegisterType<LmsCoursesRepository>().As<ILmsCoursesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCourseVersionsRepository>().As<ILmsCourseVersionsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCourseVersionApprovalsRepository>().As<ILmsCourseVersionApprovalsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCourseAudiencesRepository>().As<ILmsCourseAudiencesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCourseOwnersRepository>().As<ILmsCourseOwnersRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCourseModulesRepository>().As<ILmsCourseModulesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsLessonsRepository>().As<ILmsLessonsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsQuestionBanksRepository>().As<ILmsQuestionBanksRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsQuestionsRepository>().As<ILmsQuestionsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsAssessmentAttemptsRepository>().As<ILmsAssessmentAttemptsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsProtocolLearningsRepository>().As<ILmsProtocolLearningsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsProtocolQuizAttemptsRepository>().As<ILmsProtocolQuizAttemptsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsMicroLearningRepository>().As<ILmsMicroLearningRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsEnrollmentsRepository>().As<ILmsEnrollmentsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsLessonProgressRepository>().As<ILmsLessonProgressRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsLessonFilesRepository>().As<ILmsLessonFilesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsSurveyResponsesRepository>().As<ILmsSurveyResponsesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCrewViewingsRepository>().As<ILmsCrewViewingsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCrewViewingAttendeesRepository>().As<ILmsCrewViewingAttendeesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsPathAssignmentsRepository>().As<ILmsPathAssignmentsRepository>().InstancePerLifetimeScope();
			// Training T3 (M0383-M0385): learning requirements, enforcement, worker 93's clock and learning paths
			builder.RegisterType<LmsRequirementsRepository>().As<ILmsRequirementsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsRequirementAudiencesRepository>().As<ILmsRequirementAudiencesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsRequirementMembersRepository>().As<ILmsRequirementMembersRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsRequirementEnforcementsRepository>().As<ILmsRequirementEnforcementsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsRequirementDepartmentStatesRepository>().As<ILmsRequirementDepartmentStatesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsPathsRepository>().As<ILmsPathsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsPathStepsRepository>().As<ILmsPathStepsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsRequirementFactsRepository>().As<ILmsRequirementFactsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsDepartmentStatesRepository>().As<ILmsDepartmentStatesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LmsMediaAssetsRepository>().As<ILmsMediaAssetsRepository>().InstancePerLifetimeScope();
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
			builder.RegisterType<CallVideoFeedRepository>().As<ICallVideoFeedRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CommunicationTestRepository>().As<ICommunicationTestRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CommunicationTestRunRepository>().As<ICommunicationTestRunRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CommunicationTestResultRepository>().As<ICommunicationTestResultRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CommunicationTestTargetRepository>().As<ICommunicationTestTargetRepository>().InstancePerLifetimeScope();
			builder.RegisterType<WeatherAlertSourceRepository>().As<IWeatherAlertSourceRepository>().InstancePerLifetimeScope();
			builder.RegisterType<WeatherAlertRepository>().As<IWeatherAlertRepository>().InstancePerLifetimeScope();
			builder.RegisterType<WeatherAlertZoneRepository>().As<IWeatherAlertZoneRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentCallPriorityRepository>().As<IDepartmentCallPriorityRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallProtocolsRepository>().As<ICallProtocolsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<TrainingQuestionRepository>().As<ITrainingQuestionRepository>().InstancePerLifetimeScope();
			builder.RegisterType<LogUnitsRepository>().As<ILogUnitsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ShiftGroupRolesRepository>().As<IShiftGroupRolesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DispatchProtocolQuestionsRepository>().As<IDispatchProtocolQuestionsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DispatchProtocolTriggersRepository>().As<IDispatchProtocolTriggersRepository>().InstancePerLifetimeScope();
			builder.RegisterType<UnitActiveRolesRepository>().As<IUnitActiveRolesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DispatchProtocolQuestionAnswersRepository>().As<IDispatchProtocolQuestionAnswersRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DispatchProtocolSnapshotsRepository>().As<IDispatchProtocolSnapshotsRepository>().InstancePerLifetimeScope();
			// Protocols P1 library and S0 signatures (M0342, M0343)
			builder.RegisterType<SignatureRecordsRepository>().As<ISignatureRecordsRepository>().InstancePerLifetimeScope();
			// e-Signature S1/S2 (M0371-M0374)
			builder.RegisterType<SealedDocumentsRepository>().As<ISealedDocumentsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<SealedDocumentInputsRepository>().As<ISealedDocumentInputsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<SignatureRequestsRepository>().As<ISignatureRequestsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<SignatureRequestSignersRepository>().As<ISignatureRequestSignersRepository>().InstancePerLifetimeScope();
			// e-Signature S4 approval chains (M0413) and mutual-aid agreements (M0414)
			builder.RegisterType<ApprovalChainsRepository>().As<IApprovalChainsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ApprovalChainStepsRepository>().As<IApprovalChainStepsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ApprovalRequestsRepository>().As<IApprovalRequestsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ApprovalRequestStepsRepository>().As<IApprovalRequestStepsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ApprovalDelegatesRepository>().As<IApprovalDelegatesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<MutualAidAgreementsRepository>().As<IMutualAidAgreementsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<SignatureCapturesRepository>().As<ISignatureCapturesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ESignProviderAccountsRepository>().As<IESignProviderAccountsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ESignWebhookEventsRepository>().As<IESignWebhookEventsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolCategoriesRepository>().As<IProtocolCategoriesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolVersionsRepository>().As<IProtocolVersionsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolVersionApprovalsRepository>().As<IProtocolVersionApprovalsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolAudiencesRepository>().As<IProtocolAudiencesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolCommentsRepository>().As<IProtocolCommentsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolLibraryRepository>().As<IProtocolLibraryRepository>().InstancePerLifetimeScope();
			// Protocols P1 wave 2 (M0353) and P2 (M0354): primary files, file text, acknowledgments
			builder.RegisterType<ProtocolVersionFilesRepository>().As<IProtocolVersionFilesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolVersionTranslationsRepository>().As<IProtocolVersionTranslationsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolAiSuggestionsRepository>().As<IProtocolAiSuggestionsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolFileTextsRepository>().As<IProtocolFileTextsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolAcknowledgmentsRepository>().As<IProtocolAcknowledgmentsRepository>().InstancePerLifetimeScope();
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

			// Indoor Maps Repositories
			builder.RegisterType<IndoorMapsRepository>().As<IIndoorMapsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<IndoorMapFloorsRepository>().As<IIndoorMapFloorsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<IndoorMapZonesRepository>().As<IIndoorMapZonesRepository>().InstancePerLifetimeScope();

			// Custom Maps Repositories
			builder.RegisterType<CustomMapTilesRepository>().As<ICustomMapTilesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CustomMapImportsRepository>().As<ICustomMapImportsRepository>().InstancePerLifetimeScope();

			// Route Planning Repositories
			builder.RegisterType<RoutePlansRepository>().As<IRoutePlansRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RouteStopsRepository>().As<IRouteStopsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RouteSchedulesRepository>().As<IRouteSchedulesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RouteInstancesRepository>().As<IRouteInstancesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RouteInstanceStopsRepository>().As<IRouteInstanceStopsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RouteDeviationsRepository>().As<IRouteDeviationsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<UnitStatusAlertAcknowledgementsRepository>().As<IUnitStatusAlertAcknowledgementsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallLocationKeysRepository>().As<ICallLocationKeysRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DispatchRenotificationsRepository>().As<IDispatchRenotificationsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallNumberSequencesRepository>().As<ICallNumberSequencesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DocumentNumberSequencesRepository>().As<IDocumentNumberSequencesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentApiKeysRepository>().As<IDepartmentApiKeysRepository>().InstancePerLifetimeScope();
			builder.RegisterType<SystemOperationRequestsRepository>().As<ISystemOperationRequestsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<InboundEmailReceiptsRepository>().As<IInboundEmailReceiptsRepository>().InstancePerLifetimeScope();
			// Department Import (Import & Migration, M0273-M0276, M0281)
			builder.RegisterType<Onboarding.ImportWorkspacesRepository>().As<IImportWorkspacesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportInvitesRepository>().As<IImportInvitesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportSourcesRepository>().As<IImportSourcesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportArtifactsRepository>().As<IImportArtifactsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportDatasetsRepository>().As<IImportDatasetsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportColumnMapsRepository>().As<IImportColumnMapsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportValueMapsRepository>().As<IImportValueMapsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportStagedRowsRepository>().As<IImportStagedRowsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportIssuesRepository>().As<IImportIssuesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportExternalIdsRepository>().As<IImportExternalIdsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportRunsRepository>().As<IImportRunsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportChangeJournalRepository>().As<IImportChangeJournalRepository>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportReconciliationsRepository>().As<IImportReconciliationsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportSupportGrantsRepository>().As<IImportSupportGrantsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportSupportRequestsRepository>().As<IImportSupportRequestsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportMessagesRepository>().As<IImportMessagesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportSuggestionsRepository>().As<IImportSuggestionsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportConfigurationChangesRepository>().As<IImportConfigurationChangesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportEventsRepository>().As<IImportEventsRepository>().InstancePerLifetimeScope();

			// CheckIn Repositories
			builder.RegisterType<CheckInTimerConfigRepository>().As<ICheckInTimerConfigRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CheckInTimerOverrideRepository>().As<ICheckInTimerOverrideRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CheckInRecordRepository>().As<ICheckInRecordRepository>().InstancePerLifetimeScope();

			// Run Card Repositories
			builder.RegisterType<RunCardsRepository>().As<IRunCardsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RunCardTriggersRepository>().As<IRunCardTriggersRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RunCardAlarmLevelsRepository>().As<IRunCardAlarmLevelsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RunCardUnitRequirementsRepository>().As<IRunCardUnitRequirementsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RunCardRoleRequirementsRepository>().As<IRunCardRoleRequirementsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RunCardAvailabilitySelectionsRepository>().As<IRunCardAvailabilitySelectionsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<StationCoverageRequirementsRepository>().As<IStationCoverageRequirementsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RunCardActivationsRepository>().As<IRunCardActivationsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RunCardDecisionNodesRepository>().As<IRunCardDecisionNodesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ResponsePackagesRepository>().As<IResponsePackagesRepository>().InstancePerLifetimeScope();
			builder.RegisterType<ResponsePackageRowsRepository>().As<IResponsePackageRowsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RunCardTargetsRepository>().As<IRunCardTargetsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<RunCardDecisionsRepository>().As<IRunCardDecisionsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<CallOutstandingRequirementsRepository>().As<ICallOutstandingRequirementsRepository>().InstancePerLifetimeScope();

			// Calendar Check-In Repositories
			builder.RegisterType<CalendarItemCheckInRepository>().As<ICalendarItemCheckInRepository>().InstancePerLifetimeScope();

			// SSO Repositories
			builder.RegisterType<DepartmentSsoConfigRepository>().As<IDepartmentSsoConfigRepository>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentSecurityPolicyRepository>().As<IDepartmentSecurityPolicyRepository>().InstancePerLifetimeScope();
			builder.RegisterType<UserSessionsRepository>().As<IUserSessionsRepository>().InstancePerLifetimeScope();
			builder.RegisterType<UserExternalIdentityLinksRepository>().As<IUserExternalIdentityLinksRepository>().InstancePerLifetimeScope();

			// UTF-8 / PostgreSQL-migration data maintenance
			builder.RegisterType<Utf8MaintenanceRepository>().As<IUtf8MaintenanceRepository>().InstancePerLifetimeScope();
		}
	}
}
