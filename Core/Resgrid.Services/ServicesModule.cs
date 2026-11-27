using System;
using Autofac;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services.CallEmailTemplates;
using RestSharp;
using RestSharp.Serializers.NewtonsoftJson;
using Resgrid.Config;

namespace Resgrid.Services
{
	public class ServicesModule : Module
	{
		private const string TtsRestClientRegistrationName = "tts-rest-client";

		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterInstance(TimeProvider.System).As<TimeProvider>().SingleInstance().PreserveExistingDefaults();
			builder.RegisterType<Resgrid.AdminAssist.ConfigurationCatalog>().As<Resgrid.Model.AdminAssist.IAdminAssistCatalog>().SingleInstance();
			builder.RegisterType<AdminAssist.AdminAssistAccessService>().As<Resgrid.Model.AdminAssist.IAdminAssistAccessService>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.ConfigurationSnapshotProvider>().As<Resgrid.Model.AdminAssist.IConfigurationSnapshotProvider>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.SettingsEvidenceSource>().As<Resgrid.Model.AdminAssist.IAdminAssistEvidenceSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.DepartmentImportEvidenceSource>().As<Resgrid.Model.AdminAssist.IAdminAssistEvidenceSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.AdministrativeReferenceEvidenceSource>().As<Resgrid.Model.AdminAssist.IAdminAssistEvidenceSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.OperatingProfileEvidenceSource>().As<Resgrid.Model.AdminAssist.IAdminAssistEvidenceSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.OrganizationEvidenceSource>().As<Resgrid.Model.AdminAssist.IAdminAssistEvidenceSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.OrganizationFindingSubjects>().As<Resgrid.Model.AdminAssist.IAdminAssistFindingSubjectSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.AdminIdentityEvidenceSource>().As<Resgrid.Model.AdminAssist.IAdminAssistEvidenceSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.DispatchEvidenceSource>().As<Resgrid.Model.AdminAssist.IAdminAssistEvidenceSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.StatusConfigurationEvidenceSource>().As<Resgrid.Model.AdminAssist.IAdminAssistEvidenceSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.StatusConfigurationFindingSubjects>().As<Resgrid.Model.AdminAssist.IAdminAssistFindingSubjectSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.ImportEvidenceSource>().As<Resgrid.Model.AdminAssist.IAdminAssistEvidenceSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.CommunicationEvidenceSource>().As<Resgrid.Model.AdminAssist.IAdminAssistEvidenceSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.CapacityEvidenceSource>().As<Resgrid.Model.AdminAssist.IAdminAssistEvidenceSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.CapabilityEvidenceSource>().As<Resgrid.Model.AdminAssist.IAdminAssistEvidenceSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.QualificationEvidenceSource>().As<Resgrid.Model.AdminAssist.IAdminAssistEvidenceSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.ReadinessEvidenceSource>().As<Resgrid.Model.AdminAssist.IAdminAssistEvidenceSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.StaffingEvidenceSource>().As<Resgrid.Model.AdminAssist.IAdminAssistEvidenceSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.SetupInventoryEvidenceSource>().As<Resgrid.Model.AdminAssist.IAdminAssistEvidenceSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.VoiceEvidenceSource>().As<Resgrid.Model.AdminAssist.IAdminAssistEvidenceSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.AdminAssistAskService>().As<Resgrid.Model.AdminAssist.IAdminAssistAskService>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.AdminAssistPlanService>().As<Resgrid.Model.AdminAssist.IAdminAssistPlans>().As<Resgrid.Model.AdminAssist.IAdminAssistPlanQueries>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.AdminAssistPlanProtection>().As<Resgrid.Model.AdminAssist.IAdminAssistPlanProtection>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.AdminAssistDiagnosticService>().As<Resgrid.Model.AdminAssist.IAdminAssistDiagnostics>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.AdminAssistDiagnosticSource>().As<Resgrid.Model.AdminAssist.IAdminAssistDiagnosticSource>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.AdminAssistDiagnosticProtection>().As<Resgrid.Model.AdminAssist.IAdminAssistDiagnosticProtection>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.AdminAssistAskQueries>().As<Resgrid.Model.AdminAssist.IAdminAssistAskQueries>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.AdminAssistConversationProtection>().As<Resgrid.Model.AdminAssist.IAdminAssistConversationProtection>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.AiAccessService>().As<Resgrid.Model.AdminAssist.IAiAccessService>().InstancePerLifetimeScope();
			builder.Register(_ => {
				var endpoint = Resgrid.Llm.OperatorEndpointPolicy.ValidateUri(AiConfig.Endpoint, AiConfig.AllowPrivateEndpoint);
				return new Resgrid.Llm.OpenAiToolClient(Resgrid.Llm.OperatorEndpointPolicy.CreateClient(endpoint, AiConfig.AllowPrivateEndpoint), endpoint, AiConfig.ApiKey);
			}).As<Resgrid.Llm.ILlmClient>().InstancePerLifetimeScope();
			builder.Register(_ => {
				var target = Resgrid.Ai.AiDecisionSettings.Resolve(AiConfig.DecisionProvider, AiConfig.DecisionEndpoint, AiConfig.AllowPrivateEndpoint, AiConfig.DecisionApiKey,
					AiConfig.DecisionModel, AiConfig.DecisionModelRevision, AiConfig.DecisionRuntimeDigest, AiConfig.DecisionGatewayId) ?? throw new Resgrid.Llm.LlmUnavailableException();
				// A remote (Workers AI) endpoint never gets private-network access, whatever the self-hosted setting says.
				var allowPrivate = AiConfig.AllowPrivateEndpoint && !target.Remote;
				return new Resgrid.Llm.SystemOneDecisionClient(Resgrid.Llm.OperatorEndpointPolicy.CreateClient(target.Endpoint, allowPrivate), target.Endpoint, AiConfig.DecisionApiKey, target.Remote,
					target.GatewayId);
			}).As<Resgrid.Llm.IDecisionClient>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.AdminAssistService>().As<Resgrid.Model.AdminAssist.IAdminAssistService>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.ConfigurationImpactService>().As<Resgrid.Model.AdminAssist.IConfigurationImpactService>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.SecurityImpactService>().As<Resgrid.Model.AdminAssist.ISecurityImpactService>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.NotificationImpactService>().As<Resgrid.Model.AdminAssist.INotificationImpactService>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.RetentionImpactService>().As<Resgrid.Model.AdminAssist.IRetentionImpactService>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.TextImportImpactService>().As<Resgrid.Model.AdminAssist.ITextImportImpactService>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.ModuleImpactService>().As<Resgrid.Model.AdminAssist.IModuleImpactService>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.PermissionImpactService>().As<Resgrid.Model.AdminAssist.IPermissionImpactService>().As<Resgrid.Model.AdminAssist.IAdminAssistPermissionEvaluator>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.DispatchImpactService>().As<Resgrid.Model.AdminAssist.IDispatchImpactService>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.MappingImpactProvider>().As<Resgrid.Model.AdminAssist.IOperationalImpactProvider>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.StatusAutomationImpactProvider>().As<Resgrid.Model.AdminAssist.IOperationalImpactProvider>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.ConfigurationChangeJournal>().As<Resgrid.Model.AdminAssist.IConfigurationChangeJournal>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.AdminAssistWorklistService>().As<Resgrid.Model.AdminAssist.IAdminAssistWorklistService>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.AdminAssistMaintenanceService>().As<Resgrid.Model.AdminAssist.IAdminAssistMaintenanceService>().InstancePerLifetimeScope();
			builder.RegisterType<AdminAssist.AdminAssistTraceWriter>().As<Resgrid.Model.AdminAssist.IAdminAssistTraceWriter>().InstancePerLifetimeScope();
			builder.RegisterType<InventoryAuthorizationService>().As<IInventoryAuthorizationService>().InstancePerLifetimeScope();
			builder.RegisterType<InventoryModernizationService>().AsSelf().As<IInventoryCatalogService>().As<IInventoryStockService>().As<IInventoryWorkOrderAdapter>().As<IInventoryTransferService>()
				.As<IInventoryIssuanceService>().As<IInventoryMigrationService>().As<IInventoryPurchasingService>().As<IInventoryOperationsService>().As<IInventoryAlertService>().As<IInventoryImportService>().As<Resgrid.Model.Checklists.IChecklistAssetSource>().As<Resgrid.Model.Checklists.IChecklistHistoricalAssetSource>().InstancePerLifetimeScope();
			builder.RegisterType<InventoryAlertNotifications>().AsSelf().InstancePerLifetimeScope();
			builder.RegisterType<InventoryScheduledReportService>().As<IInventoryScheduledReportService>().InstancePerLifetimeScope();
			builder.RegisterType<ReadinessAccessService>().As<IReadinessAccessService>().InstancePerLifetimeScope();
			builder.RegisterType<ChecklistTemplateService>().As<IChecklistTemplateService>().InstancePerLifetimeScope();
			builder.RegisterType<WorkOrderNotificationService>().As<IWorkOrderNotificationService>().InstancePerLifetimeScope();
			builder.Register(_ => new RestClient(new RestClientOptions(SystemBehaviorConfig.BillingApiBaseUrl) { Timeout = TimeSpan.FromSeconds(10) },
				configureSerialization: serializer => serializer.UseNewtonsoftJson())).Named<RestClient>("readiness-billing-client").SingleInstance();
			builder.RegisterType<ReadinessProBillingService>().As<IReadinessProBillingService>()
				.WithParameter((parameter, _) => parameter.ParameterType == typeof(Func<RestClient>), (_, context) =>
				{
					var scope = context.Resolve<ILifetimeScope>();
					return (Func<RestClient>)(() => scope.ResolveNamed<RestClient>("readiness-billing-client"));
				}).InstancePerLifetimeScope();
			// Workforce & Business Operations plan Phase B (decision 42): add-on billing proxy and entitlement gate.
			builder.Register(_ => new RestClient(new RestClientOptions(SystemBehaviorConfig.BillingApiBaseUrl) { Timeout = TimeSpan.FromSeconds(10) },
				configureSerialization: serializer => serializer.UseNewtonsoftJson())).Named<RestClient>("business-operations-billing-client").SingleInstance();
			builder.RegisterType<BusinessOperationsBillingService>().As<IBusinessOperationsBillingService>()
				.WithParameter((parameter, _) => parameter.ParameterType == typeof(Func<RestClient>), (_, context) =>
				{
					var scope = context.Resolve<ILifetimeScope>();
					return (Func<RestClient>)(() => scope.ResolveNamed<RestClient>("business-operations-billing-client"));
				}).InstancePerLifetimeScope();
			builder.RegisterType<BusinessOperationsAccessService>().As<IBusinessOperationsAccessService>().InstancePerLifetimeScope();
			// Incident Back Office (large-incident-logistics-finance-plan.md rev 4.1): add-on gate, ledger services, rules and lifecycles.
			IncidentLogistics.IncidentBackOfficeRegistrations.Register(builder);
			// Enhanced AI add-on (enhanced-ai-addon-plan.md): billing proxy and paid entitlement gate, cloned from Business Operations.
			builder.Register(_ => new RestClient(new RestClientOptions(SystemBehaviorConfig.BillingApiBaseUrl) { Timeout = TimeSpan.FromSeconds(10) },
				configureSerialization: serializer => serializer.UseNewtonsoftJson())).Named<RestClient>("ai-billing-client").SingleInstance();
			builder.RegisterType<AiBillingService>().As<IAiBillingService>()
				.WithParameter((parameter, _) => parameter.ParameterType == typeof(Func<RestClient>), (_, context) =>
				{
					var scope = context.Resolve<ILifetimeScope>();
					return (Func<RestClient>)(() => scope.ResolveNamed<RestClient>("ai-billing-client"));
				}).InstancePerLifetimeScope();
			builder.RegisterType<EnhancedAiAccessService>().As<IEnhancedAiAccessService>().InstancePerLifetimeScope();
			// Dedicated Number add-on (call-intake-and-ai-destinations-plan.md D1): billing proxy, entitlement and the department's number.
			builder.Register(_ => new RestClient(new RestClientOptions(SystemBehaviorConfig.BillingApiBaseUrl) { Timeout = TimeSpan.FromSeconds(10) },
				configureSerialization: serializer => serializer.UseNewtonsoftJson())).Named<RestClient>("dedicated-number-billing-client").SingleInstance();
			builder.RegisterType<DedicatedNumberBillingService>().As<IDedicatedNumberBillingService>()
				.WithParameter((parameter, _) => parameter.ParameterType == typeof(Func<RestClient>), (_, context) =>
				{
					var scope = context.Resolve<ILifetimeScope>();
					return (Func<RestClient>)(() => scope.ResolveNamed<RestClient>("dedicated-number-billing-client"));
				}).InstancePerLifetimeScope();
			builder.RegisterType<DedicatedNumberAccessService>().As<IDedicatedNumberAccessService>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentPhoneNumbersService>().As<IDepartmentPhoneNumbersService>().InstancePerLifetimeScope();
			// Advanced Data Protection yearly checkout (AdpAddonConfig): billing proxy, cloned from Enhanced AI.
			builder.Register(_ => new RestClient(new RestClientOptions(SystemBehaviorConfig.BillingApiBaseUrl) { Timeout = TimeSpan.FromSeconds(10) },
				configureSerialization: serializer => serializer.UseNewtonsoftJson())).Named<RestClient>("adp-billing-client").SingleInstance();
			builder.RegisterType<AdpBillingService>().As<IAdpBillingService>()
				.WithParameter((parameter, _) => parameter.ParameterType == typeof(Func<RestClient>), (_, context) =>
				{
					var scope = context.Resolve<ILifetimeScope>();
					return (Func<RestClient>)(() => scope.ResolveNamed<RestClient>("adp-billing-client"));
				}).InstancePerLifetimeScope();
			// Enhanced AI base-type suggestions for department list names (decision model, DepartmentConfiguration requests only).
			builder.RegisterType<BaseTypeAiSuggestionService>().As<IBaseTypeAiSuggestionService>().InstancePerLifetimeScope();
			// AI dispatch, Enrich mode (enhanced-ai-addon-plan.md §4): worker-side enrichment of calls created from AI-format dispatch email.
			builder.RegisterType<AiDispatch.AiDispatchEnrichmentService>().As<Resgrid.Model.AiDispatch.IAiDispatchEnrichmentService>().InstancePerLifetimeScope();
			builder.RegisterType<AiDispatch.AiDispatchAdminService>().As<Resgrid.Model.AiDispatch.IAiDispatchAdminService>().InstancePerLifetimeScope();
			// Enhanced AI destination advice (call-intake-and-ai-destinations-plan.md D4): advisor, entitlement memory, call save triggers.
			builder.RegisterType<AiDestination.AiDestinationAvailabilityCache>().AsSelf().SingleInstance();
			builder.RegisterType<AiDestination.AiDestinationAdvisorService>().As<IAiDestinationAdvisorService>().InstancePerLifetimeScope();
			builder.RegisterType<AiDestination.AiDestinationReviewEventListener>().AsSelf().SingleInstance().AutoActivate();
			builder.RegisterType<Invoicing.InvoicingService>().As<IInvoicingService>().InstancePerLifetimeScope();
			builder.RegisterType<Invoicing.DeploymentService>().As<IDeploymentService>().InstancePerLifetimeScope();
			builder.RegisterType<Invoicing.TimeTrackingService>().As<ITimeTrackingService>().InstancePerLifetimeScope();
			// Workforce & Business Operations plan, Phase C-M2: the contractor path.
			builder.RegisterType<Invoicing.RateScheduleService>().As<IRateScheduleService>().InstancePerLifetimeScope();
			builder.RegisterType<Invoicing.ServiceContractService>().As<IServiceContractService>().InstancePerLifetimeScope();
			builder.RegisterType<Invoicing.BidsService>().As<IBidsService>().InstancePerLifetimeScope();
			builder.RegisterType<Invoicing.ContractorBillingEngine>().As<IContractorBillingEngine>().InstancePerLifetimeScope();
			// Workforce & Business Operations plan, Phase C-M3: Cal OES MARS cost recovery (manual gateway only).
			builder.RegisterType<CostRecovery.CalOesMarsReimbursementCalculator>().As<ICalOesMarsReimbursementCalculator>().SingleInstance();
			builder.RegisterType<CostRecovery.ManualCalOesMarsGateway>().As<ICalOesMarsExternalGateway>().SingleInstance();
			builder.RegisterType<CostRecovery.CalOesMarsService>().As<ICalOesMarsService>().InstancePerLifetimeScope();
			// Workforce & Business Operations plan, Phase E: protected workforce pay data, field costing and California pay data reporting.
			builder.RegisterType<Workforce.WorkforceService>().As<IWorkforceService>().InstancePerLifetimeScope();
			builder.RegisterType<Workforce.CompensationCostService>().As<ICompensationCostService>().InstancePerLifetimeScope();
			builder.RegisterType<Workforce.FieldCostingService>().As<IFieldCostingService>().InstancePerLifetimeScope();
			builder.RegisterType<Workforce.PayDataDemographicsService>().As<IPayDataDemographicsService>().InstancePerLifetimeScope();
			builder.RegisterType<Workforce.CaPayDataReportingService>().As<ICaPayDataReportingService>().InstancePerLifetimeScope();
			builder.RegisterType<WorkOrdersService>().As<IWorkOrdersService>().As<IWorkOrderMaintenanceService>().As<IWorkOrderReportingService>().As<IWorkOrderOperationsService>().InstancePerLifetimeScope();
			builder.RegisterType<WorkOrderAuthorizationService>().As<IWorkOrderAuthorizationService>().InstancePerLifetimeScope();
			builder.RegisterType<ChecklistsService>().As<IChecklistsService>().InstancePerLifetimeScope();
			builder.RegisterType<ChecklistScheduledReportService>().As<IChecklistScheduledReportService>().InstancePerLifetimeScope();
			builder.RegisterType<ChecklistAssignmentService>().As<Resgrid.Model.Checklists.IChecklistAssignmentService>().InstancePerLifetimeScope();
			builder.RegisterType<UnavailableChecklistAssetSource>().As<Resgrid.Model.Checklists.IChecklistAssetSource>().PreserveExistingDefaults().InstancePerLifetimeScope();
			builder.RegisterType<ChecklistReminderService>().As<IChecklistReminderService>().InstancePerLifetimeScope();
			builder.RegisterType<ReadinessHistoryProtectionService>().As<IReadinessHistoryProtectionService>().InstancePerLifetimeScope();
			builder.RegisterType<ChecklistAuthorizationService>().As<IChecklistAuthorizationService>().InstancePerLifetimeScope();
			builder.RegisterType<EncryptionService>().As<IEncryptionService>().InstancePerLifetimeScope();
			builder.RegisterType<WorkflowService>().As<IWorkflowService>().InstancePerLifetimeScope();
			// Protected Workflows (ADP push model): the administrative lifecycle and the unattended runtime WorkflowService
			// hands every step of an approved release to. The runtime needs IProtectedDataBrokerClient
			// (ProtectedDataBrokerClientModule), which every host that loads this module already registers.
			builder.RegisterType<ProtectedWorkflowService>().As<IProtectedWorkflowService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtectedWorkflowRuntime>().As<IProtectedWorkflowRuntime>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentCommandService>().As<IIncidentCommandService>().InstancePerLifetimeScope();
			builder.RegisterType<ChatChannelService>().As<IChatChannelService>().InstancePerLifetimeScope();
			builder.RegisterType<DispatchAccessService>().As<IDispatchAccessService>().InstancePerLifetimeScope();
			builder.RegisterType<CommandAccessService>().As<ICommandAccessService>().InstancePerLifetimeScope();
			builder.RegisterType<ChatPermissionService>().As<IChatPermissionService>().InstancePerLifetimeScope();
			builder.RegisterType<ChatMessageService>().As<IChatMessageService>().InstancePerLifetimeScope();
			builder.RegisterType<ChatPresenceService>().As<IChatPresenceService>().InstancePerLifetimeScope();
			builder.RegisterType<ChatModerationService>().As<IChatModerationService>().InstancePerLifetimeScope();
			builder.RegisterType<ModerationService>().As<IModerationService>().InstancePerLifetimeScope();
			builder.RegisterType<ChatNotificationService>().As<IChatNotificationService>().InstancePerLifetimeScope();
			builder.RegisterType<ChatProvisioningEventService>().As<IChatProvisioningEventService>().SingleInstance().AutoActivate();
			builder.RegisterType<DispatcherDeskCallEventService>().AsSelf().SingleInstance().AutoActivate();
			builder.RegisterType<DispatcherEmailCallService>().As<IDispatcherEmailCallService>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentCommandNotificationService>().As<IIncidentCommandNotificationService>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentVoiceService>().As<IIncidentVoiceService>().InstancePerLifetimeScope();
			builder.RegisterType<MutualAidService>().As<IMutualAidService>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentResourcesService>().As<IIncidentResourcesService>().InstancePerLifetimeScope();
			builder.RegisterType<SyncService>().As<ISyncService>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentReportingService>().As<IIncidentReportingService>().InstancePerLifetimeScope();
			builder.RegisterType<WorkflowTemplateContextBuilder>().As<Resgrid.Model.Providers.IWorkflowTemplateContextBuilder>().InstancePerLifetimeScope();
			builder.RegisterType<LogService>().As<ILogService>().InstancePerLifetimeScope();
			builder.RegisterType<QueueService>().As<IQueueService>().InstancePerLifetimeScope();
			builder.RegisterType<DeleteService>().As<IDeleteService>().InstancePerLifetimeScope();
			builder.RegisterType<CommunicationService>().As<ICommunicationService>().InstancePerLifetimeScope();
			// Default no-op chat outbound channel so CommunicationService always resolves it; the real
			// ChatbotOutboundService in ChatbotProviderModule overrides this (PreserveExistingDefaults
			// keeps the real one winning regardless of module load order).
			builder.RegisterType<NullChatbotOutboundService>().As<IChatbotOutboundService>().InstancePerLifetimeScope().PreserveExistingDefaults();
			builder.RegisterType<SmsService>().As<ISmsService>().InstancePerLifetimeScope();
			builder.RegisterType<PushLogsService>().As<IPushLogsService>().InstancePerLifetimeScope();
			// Per scope: its push-log, profile, settings and units services are per scope (the decorator below follows).
			builder.RegisterType<PushService>().As<IPushService>().InstancePerLifetimeScope();
			// ADP outbound net (plan 7.5): a push title or subtitle carrying an envelope would land
			// on a lock screen and in the OS notification log.
			builder.RegisterDecorator<ProtectedPushServiceDecorator, IPushService>();
			builder.RegisterType<MessageService>().As<IMessageService>().InstancePerLifetimeScope();
			builder.RegisterType<TextResponsePromptService>().As<ITextResponsePromptService>().InstancePerLifetimeScope();
			builder.RegisterType<AddressService>().As<IAddressService>().InstancePerLifetimeScope();
			builder.RegisterType<UserStateService>().As<IUserStateService>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentsService>().As<IDepartmentsService>().InstancePerLifetimeScope();
			builder.RegisterType<UsersService>().As<IUsersService>().InstancePerLifetimeScope();
			builder.RegisterType<ActionLogsService>().As<IActionLogsService>().InstancePerLifetimeScope();
			builder.RegisterType<PersonnelStatusBoardService>().As<IPersonnelStatusBoardService>().InstancePerLifetimeScope();
			builder.RegisterType<EmailService>().As<IEmailService>().InstancePerLifetimeScope();
			builder.RegisterType<EmailMarketingService>().As<IEmailMarketingService>().InstancePerLifetimeScope();
			builder.RegisterType<CallsService>().As<ICallsService>().InstancePerLifetimeScope();
			builder.RegisterType<CallNumberingService>().As<ICallNumberingService>().InstancePerLifetimeScope();
			builder.RegisterType<DocumentNumberingService>().As<IDocumentNumberingService>().InstancePerLifetimeScope();
			builder.RegisterType<PlatformReportingService>().As<IPlatformReportingService>().InstancePerLifetimeScope();
			builder.RegisterType<ReportingRollupProcessor>().As<IReportingRollupProcessor>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentGroupsService>().As<IDepartmentGroupsService>().InstancePerLifetimeScope();
			builder.RegisterType<AuthorizationService>().As<IAuthorizationService>().InstancePerLifetimeScope();
			builder.RegisterType<UserProfileService>().As<IUserProfileService>().InstancePerLifetimeScope();
			builder.RegisterType<InvitesService>().As<IInvitesService>().InstancePerLifetimeScope();
			builder.RegisterType<WorkLogsService>().As<IWorkLogsService>().InstancePerLifetimeScope();
			builder.RegisterType<SubscriptionsService>().As<ISubscriptionsService>().InstancePerLifetimeScope();
			builder.RegisterType<LimitsService>().As<ILimitsService>().InstancePerLifetimeScope();
			builder.RegisterType<CallEmailFactory>().As<ICallEmailFactory>().InstancePerLifetimeScope();
			builder.RegisterType<JobsService>().As<IJobsService>().InstancePerLifetimeScope();
			builder.RegisterType<UnitsService>().As<IUnitsService>().InstancePerLifetimeScope();
			builder.RegisterType<UnitTrackingIdentifierService>().As<IUnitTrackingIdentifierService>().SingleInstance();
			builder.RegisterType<UnitTrackingEventIdService>().As<IUnitTrackingEventIdService>().SingleInstance();
			builder.RegisterType<UnitTrackingAuthenticationService>().As<IUnitTrackingAuthenticationService>().InstancePerLifetimeScope();
			builder.RegisterType<UnitTrackingService>().As<IUnitTrackingService>().InstancePerLifetimeScope();
			builder.RegisterType<UnitTrackingIngressService>().As<IUnitTrackingIngressService>().InstancePerLifetimeScope();
			builder.RegisterType<UnitLocationSourceResolver>().As<IUnitLocationSourceResolver>().InstancePerLifetimeScope();
			builder.RegisterType<UnitTrackingCatalogService>().As<IUnitTrackingCatalogService>().SingleInstance();
			builder.RegisterType<UnitTrackingStatusService>().As<IUnitTrackingStatusService>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentSettingsService>().As<IDepartmentSettingsService>().InstancePerLifetimeScope();
			builder.RegisterType<FeatureToggleService>().As<IFeatureToggleService>().InstancePerLifetimeScope();
			builder.RegisterType<ChecklistAccessMutationObserver>().As<IFeatureFlagMutationObserver>().InstancePerLifetimeScope();
			builder.RegisterType<CallDispatchStatusService>().As<ICallDispatchStatusService>().InstancePerLifetimeScope();
			builder.RegisterType<DispatchRenotificationService>().As<IDispatchRenotificationService>().InstancePerLifetimeScope();
			builder.RegisterType<PendingCallsService>().As<IPendingCallsService>().InstancePerLifetimeScope();
			builder.RegisterType<CallClosureService>().As<ICallClosureService>().InstancePerLifetimeScope();
			builder.RegisterType<CallAutoCloseService>().As<ICallAutoCloseService>().InstancePerLifetimeScope();
			builder.RegisterType<CallResourceReleaseService>().As<ICallResourceReleaseService>().InstancePerLifetimeScope();
			builder.RegisterType<CallBroadcastOutboxService>().As<ICallBroadcastOutboxService>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentApiKeysService>().As<IDepartmentApiKeysService>().InstancePerLifetimeScope();
			builder.RegisterType<SystemOperationsService>().As<ISystemOperationsService>().InstancePerLifetimeScope();
			builder.RegisterType<InboundEmailReceiptsService>().As<IInboundEmailReceiptsService>().InstancePerLifetimeScope();
			builder.RegisterType<CallStatusAttributionService>().As<ICallStatusAttributionService>().InstancePerLifetimeScope();
			builder.RegisterType<CallGroupAssignmentService>().As<ICallGroupAssignmentService>().InstancePerLifetimeScope();
			builder.RegisterType<PersonnelRolesService>().As<IPersonnelRolesService>().InstancePerLifetimeScope();
			builder.RegisterType<ScheduledTasksService>().As<IScheduledTasksService>().InstancePerLifetimeScope();
			builder.RegisterType<DistributionListsService>().As<IDistributionListsService>().InstancePerLifetimeScope();
			builder.RegisterType<DocumentsService>().As<IDocumentsService>().InstancePerLifetimeScope();
			//builder.RegisterType<PaymentProviderService>().As<IPaymentProviderService>().InstancePerLifetimeScope();
			builder.RegisterType<CalendarService>().As<ICalendarService>().InstancePerLifetimeScope();
			builder.RegisterType<CalendarExportService>().As<ICalendarExportService>().InstancePerLifetimeScope();
			builder.RegisterType<NotesService>().As<INotesService>().InstancePerLifetimeScope();
			builder.RegisterType<CertificationService>().As<ICertificationService>().InstancePerLifetimeScope();
			builder.RegisterType<AffiliateService>().As<IAffiliateService>().InstancePerLifetimeScope();
			builder.RegisterType<NumbersService>().As<INumbersService>().InstancePerLifetimeScope();
			builder.RegisterType<TextCommandService>().As<ITextCommandService>().InstancePerLifetimeScope();
			builder.RegisterType<NotificationService>().As<INotificationService>().InstancePerLifetimeScope();
			builder.RegisterType<ShiftsService>().As<IShiftsService>().InstancePerLifetimeScope();
			builder.RegisterType<TrainingService>().As<ITrainingService>().InstancePerLifetimeScope();
			builder.RegisterType<TrainingAssignmentService>().As<ITrainingAssignmentService>().InstancePerLifetimeScope();
			builder.RegisterType<MyTrainingService>().As<IMyTrainingService>().InstancePerLifetimeScope();
			builder.RegisterType<MemberLeaveService>().As<IMemberLeaveService>().InstancePerLifetimeScope();
			builder.RegisterType<ContentAudienceService>().As<IContentAudienceService>().InstancePerLifetimeScope();
			builder.RegisterType<ContentAudienceChangeQueue>().As<IContentAudienceChangeQueue>().InstancePerLifetimeScope();
			builder.RegisterType<LearningRecordWriter>().As<ILearningRecordWriter>().InstancePerLifetimeScope();
			builder.RegisterType<LearningRecordService>().As<ILearningRecordService>().InstancePerLifetimeScope();
			// Round 7B (permission 135): completions recorded for members by an instructor (baseline; the LMS course target registers below)
			builder.RegisterType<TrainingCompletionRecordingService>().As<ITrainingCompletionRecordingService>().InstancePerLifetimeScope();
			// Round 9B: training hours submissions with evidence, their optional review and the member and reviewer notices (EventTypes 79-81)
			builder.RegisterType<TrainingSubmissionService>().As<ITrainingSubmissionService>().InstancePerLifetimeScope();
			builder.RegisterType<TrainingRecordNoticeService>().As<ITrainingRecordNoticeService>().InstancePerLifetimeScope();
			// Round 10 (Omni): the member's own transcript and certificate PDFs for the v4 API (the web pages' documents and seal revisions)
			builder.RegisterType<TrainingRecordDocumentService>().As<ITrainingRecordDocumentService>().InstancePerLifetimeScope();
			builder.RegisterType<LearningRecordBackfillService>().As<ILearningRecordBackfillService>().InstancePerLifetimeScope();
			builder.RegisterType<TrainingHistoryImportService>().As<ITrainingHistoryImportService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsAccessService>().As<ILmsAccessService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsTrainingService>().As<ILmsTrainingService>().InstancePerLifetimeScope();
			// LMS general availability (§17.3, §17.4): the pack add-on billing proxy, seats and the reactivation re-base.
			builder.Register(_ => new RestClient(new RestClientOptions(SystemBehaviorConfig.BillingApiBaseUrl) { Timeout = TimeSpan.FromSeconds(10) },
				configureSerialization: serializer => serializer.UseNewtonsoftJson())).Named<RestClient>("lms-billing-client").SingleInstance();
			builder.RegisterType<LmsBillingService>().As<ILmsBillingService>()
				.WithParameter((parameter, _) => parameter.ParameterType == typeof(Func<RestClient>), (_, context) =>
				{
					var scope = context.Resolve<ILifetimeScope>();
					return (Func<RestClient>)(() => scope.ResolveNamed<RestClient>("lms-billing-client"));
				}).InstancePerLifetimeScope();
			builder.RegisterType<LmsSeatService>().As<ILmsSeatService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsReactivationService>().As<ILmsReactivationService>().InstancePerLifetimeScope();
			// Workforce Pro add-on (shifts plan §7; owner 2026-11-26: member wellness and workplace exposure): the gate, seats and the pack
			// add-on billing proxy (the LMS contract on api/WorkforceProBilling, sharing the billing client).
			builder.RegisterType<WorkforceProAccessService>().As<IWorkforceProAccessService>().InstancePerLifetimeScope();
			builder.RegisterType<WorkforceProSeatService>().As<IWorkforceProSeatService>().InstancePerLifetimeScope();
			builder.RegisterType<WorkforceProBillingService>().As<IWorkforceProBillingService>()
				.WithParameter((parameter, _) => parameter.ParameterType == typeof(Func<RestClient>), (_, context) =>
				{
					var scope = context.Resolve<ILifetimeScope>();
					return (Func<RestClient>)(() => scope.ResolveNamed<RestClient>("lms-billing-client"));
				}).InstancePerLifetimeScope();
			// Member wellness WL0 (§13b): exposure and critical-incident log, post-call flags, screening, worker 91.
			builder.RegisterType<Resgrid.Services.Wellness.MemberWellnessService>().As<IMemberWellnessService>().InstancePerLifetimeScope();
			// Member wellness WL1: BBP exposure cases, injuries and return to duty, OSHA 300/301, NFPA 1582 clearances.
			builder.RegisterType<Resgrid.Services.Wellness.MemberHealthService>().As<IMemberHealthService>().InstancePerLifetimeScope();
			// Training T2 LMS courses and assessments, A1 Academy
			builder.RegisterType<LmsGateService>().As<ILmsGateService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsEntitlementService>().As<ILmsEntitlementService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCourseService>().As<ILmsCourseService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsQuestionBankService>().As<ILmsQuestionBankService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsLearnerService>().As<ILmsLearnerService>().As<ILmsCourseResults>().InstancePerLifetimeScope();
			builder.RegisterType<LmsAssessmentService>().As<ILmsAssessmentService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsMediaService>().As<ILmsMediaService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCrewViewingService>().As<ILmsCrewViewingService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsAcademyService>().As<ILmsAcademyService>().InstancePerLifetimeScope();
			// Academy A2/A3: major lesson revisions (worker 88), guided tours, "What's new" and the Help-menu search.
			builder.RegisterType<AcademyManifestUpdateService>().As<IAcademyManifestUpdateService>().InstancePerLifetimeScope();
			builder.RegisterType<AcademyGuidanceService>().As<IAcademyGuidanceService>().InstancePerLifetimeScope();
			// Training T3 part 2 (round 4C): annual plan, competency frameworks, training requests, report catalog and its sources,
			// certification cycles, familiarization credit, credential-status message audiences (worker 94)
			builder.RegisterType<LmsProgramNotifier>().As<ILmsProgramNotifier>().InstancePerLifetimeScope();
			builder.RegisterType<LmsProgramSettingsService>().As<ILmsProgramSettingsService>().InstancePerLifetimeScope();
			// Training T5 data exit and CE reporting (round 6D): worker 99 builds the exports; CE files are built on request
			builder.RegisterType<LmsDataExportService>().As<ILmsDataExportService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCeExportService>().As<ILmsCeExportService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsTrainingPlanService>().As<ILmsTrainingPlanService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCompetencyService>().As<ILmsCompetencyService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsTrainingRequestService>().As<ILmsTrainingRequestService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCertificationCycleService>().As<ILmsCertificationCycleService>().InstancePerLifetimeScope();
			// Round 9A: certifications backed by training (requirement sets, validity rules, the issuance ledger and its void roll-back). The
			// observer is the LMS side of the baseline ILearningRecordObserver seam (the writer resolves it lazily).
			builder.RegisterType<LmsCertificationIssuanceService>().As<ILmsCertificationIssuanceService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCertificationRequirementService>().As<ILmsCertificationRequirementService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCertificationLearningRecordObserver>().As<ILearningRecordObserver>().InstancePerLifetimeScope();
			builder.RegisterType<LmsFamiliarizationCreditService>().As<ILmsFamiliarizationCreditService>().InstancePerLifetimeScope();
			builder.RegisterType<CredentialStatusAudienceService>().As<ICredentialStatusAudienceService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsHoursReportSource>().As<ILmsReportSource>().InstancePerLifetimeScope();
			builder.RegisterType<LmsIsoReportSource>().As<ILmsReportSource>().InstancePerLifetimeScope();
			builder.RegisterType<LmsNccpProgressReportSource>().As<ILmsReportSource>().InstancePerLifetimeScope();
			builder.RegisterType<LmsNccpMemberExportSource>().As<ILmsReportSource>().InstancePerLifetimeScope();
			builder.RegisterType<LmsTrainingPlanReportSource>().As<ILmsReportSource>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCompetencyGapReportSource>().As<ILmsReportSource>().InstancePerLifetimeScope();
			builder.RegisterType<LmsTrainingRequestCostReportSource>().As<ILmsReportSource>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCertificationExceptionReportSource>().As<ILmsReportSource>().InstancePerLifetimeScope();
			// Round 5F: round-4B's compliance matrix in the report catalog.
			builder.RegisterType<LmsComplianceMatrixReportSource>().As<ILmsReportSource>().InstancePerLifetimeScope();
			builder.RegisterType<LmsReportCatalogService>().As<ILmsReportCatalogService>().InstancePerLifetimeScope();
			// Training T4 part 1 (round 5B): class sessions, lesson plans, worker 95, the event-roster message audience, the timecard seam,
			// the post-class course quiz and the class reports
			builder.RegisterType<LmsClassNotifier>().As<ILmsClassNotifier>().InstancePerLifetimeScope();
			builder.RegisterType<LmsClassService>().As<ILmsClassService>().InstancePerLifetimeScope();
			// Training T5 item 2 (round 6E): external learners, the learner portal and paid public classes
			builder.RegisterType<LmsExternalLearnerService>().As<ILmsExternalLearnerService>().InstancePerLifetimeScope();
			// Round 7B: learner class e-mails (claimed once, learner's language), recorded LMS course completions and their void target
			builder.RegisterType<LmsLearnerClassMailer>().As<ILmsLearnerClassMailer>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCompletionRecordingService>().As<ILmsCompletionRecordingService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCourseCompletionRecordingTarget>().As<ITrainingCompletionRecordingTarget>().InstancePerLifetimeScope();
			builder.RegisterType<LmsPublicClassService>().As<ILmsPublicClassService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsLessonPlanService>().As<ILmsLessonPlanService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsClassSweepService>().As<ILmsClassSweepService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsEventRosterAudienceService>().As<IEventRosterAudienceService>().InstancePerLifetimeScope();
			builder.RegisterType<UnavailableTrainingTimecardOffers>().As<ITrainingTimecardOffers>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCourseAssessmentPostClassEvaluation>().As<ILmsPostClassEvaluation>().InstancePerLifetimeScope();
			// T4: a skill sheet as the post-class evaluation (class sign-off opens a crew evaluation through the skills hook).
			builder.RegisterType<LmsSkillSheetPostClassEvaluation>().As<ILmsPostClassEvaluation>().InstancePerLifetimeScope();
			builder.RegisterType<LmsClassRosterReportSource>().As<ILmsReportSource>().InstancePerLifetimeScope();
			builder.RegisterType<LmsClassEnrollmentReportSource>().As<ILmsReportSource>().InstancePerLifetimeScope();
			builder.RegisterType<LmsClassExceptionReportSource>().As<ILmsReportSource>().InstancePerLifetimeScope();
			builder.RegisterType<LmsClassSeriesReportSource>().As<ILmsReportSource>().InstancePerLifetimeScope();
			builder.RegisterType<LmsClassCostReportSource>().As<ILmsReportSource>().InstancePerLifetimeScope();
			builder.RegisterType<LmsInstructorHoursReportSource>().As<ILmsReportSource>().InstancePerLifetimeScope();
			// Training T3: learning requirements (engine, matrix, enforcement, onboarding) and learning paths
			builder.RegisterType<LmsRequirementService>().As<ILmsRequirementService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsPathService>().As<ILmsPathService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsRequirementNotifier>().As<ILmsRequirementNotifier>().InstancePerLifetimeScope();
			// Training T4 part 2 (round 5C): skill sheets, evaluations (S3 evaluator mode: the SkillEvaluation subject source and its access rule),
			// the post-class evaluation hook, position task books, the skill notification digest (worker 96), rating and pass/fail reports
			builder.RegisterType<LmsSkillSettingsService>().As<ILmsSkillSettingsService>().InstancePerLifetimeScope();
			// Training T5 standards (round 6C): SCORM / cmi5 packages (worker 98 unpacks), the sandboxed runtime and the cmi5 minimal LRS, the LTI
			// 1.3 tool consumer, xAPI statements out (the outbox worker 98 delivers), the course builder's lesson check, the guarded HTTP transport
			builder.RegisterType<LmsContentPackageService>().As<ILmsContentPackageService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsStandardsRuntimeService>().As<ILmsStandardsRuntimeService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsLtiService>().As<ILmsLtiService>().InstancePerLifetimeScope();
			// Round 10 A8: SCORM / cmi5 / LTI lessons in the app's web view (single-use launch link, launch page session, completion message).
			builder.RegisterType<LmsAppLaunchService>().As<ILmsAppLaunchService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsXapiService>().As<ILmsXapiService>().As<ILmsXapiStatementQueue>().InstancePerLifetimeScope();
			builder.RegisterType<LmsStandardsLessonValidator>().As<ILmsStandardsLessonValidator>().InstancePerLifetimeScope();
			builder.RegisterType<LmsStandardsHttpTransport>().As<ILmsStandardsTransport>().SingleInstance();
			builder.RegisterType<LmsSkillNotificationService>().As<ILmsSkillNotificationService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsSkillSheetService>().As<ILmsSkillSheetService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsSkillEvaluationService>().As<ILmsSkillEvaluationService>().As<ILmsPostClassEvaluationHook>().InstancePerLifetimeScope();
			builder.RegisterType<LmsTaskBookService>().As<ILmsTaskBookService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsSkillCompetencySubjects>().As<ILmsSkillCompetencySubjects>().InstancePerLifetimeScope();
			builder.RegisterType<LmsSkillSignatureSource>().As<ISignatureSubjectSource>().InstancePerLifetimeScope();
			builder.RegisterType<LmsSkillSignatureAccessRule>().As<ISignatureSubjectAccessRule>().InstancePerLifetimeScope();
			builder.RegisterType<LmsSkillPassFailReportSource>().As<ILmsReportSource>().InstancePerLifetimeScope();
			builder.RegisterType<LmsSkillRatingsReportSource>().As<ILmsReportSource>().InstancePerLifetimeScope();
			builder.RegisterType<CommandsService>().As<ICommandsService>().InstancePerLifetimeScope();
			builder.RegisterType<CustomStateService>().As<ICustomStateService>().InstancePerLifetimeScope();
			builder.RegisterType<GeoService>().As<IGeoService>().InstancePerLifetimeScope();
			builder.RegisterType<RoutingService>().As<IRoutingService>().InstancePerLifetimeScope();
			builder.RegisterType<RoutingUsageService>().As<IRoutingUsageService>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentMapProvidersService>().As<IDepartmentMapProvidersService>().InstancePerLifetimeScope();
			builder.RegisterType<MapStyleService>().As<IMapStyleService>().InstancePerLifetimeScope();
			builder.RegisterType<StaticMapService>().As<IStaticMapService>().InstancePerLifetimeScope();
			builder.RegisterType<CustomMapMercatorTileRenderer>().As<ICustomMapMercatorTileRenderer>().InstancePerLifetimeScope();
			builder.RegisterType<AuditService>().As<IAuditService>().InstancePerLifetimeScope();
			builder.RegisterType<PermissionsService>().As<IPermissionsService>().InstancePerLifetimeScope();
			builder.RegisterType<ImageService>().As<IImageService>().InstancePerLifetimeScope();
			builder.RegisterType<MappingService>().As<IMappingService>().InstancePerLifetimeScope();
			builder.RegisterType<PoiImportService>().As<IPoiImportService>().InstancePerLifetimeScope();
			builder.RegisterType<InventoryService>().As<IInventoryService>().InstancePerLifetimeScope();
			//builder.RegisterType<DepartmentProfileService>().As<IDepartmentProfileService>().InstancePerLifetimeScope();
			builder.RegisterType<FileService>().As<IFileService>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentLinksService>().As<IDepartmentLinksService>().InstancePerLifetimeScope();
			builder.RegisterType<ResourceOrdersService>().As<IResourceOrdersService>().InstancePerLifetimeScope();
			builder.RegisterType<HealthService>().As<IHealthService>().InstancePerLifetimeScope();
			// Workforce & Business Operations plan Phase B2 (scaffold 2026-09-18): webhook health read for the v4 Health endpoint.
			builder.RegisterType<Invoicing.StripeConnectEndpointProbe>().As<IStripeConnectEndpointProbe>().SingleInstance();
			builder.RegisterType<Invoicing.InvoicePaymentsService>().As<IInvoicePaymentsService>().InstancePerLifetimeScope();
			builder.RegisterType<FirebaseService>().As<IFirebaseService>().InstancePerLifetimeScope();
			builder.RegisterType<TemplatesService>().As<ITemplatesService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolsService>().As<IProtocolsService>().InstancePerLifetimeScope();
			// S0 signing layer and the P1 protocol library (the shared content core).
			builder.RegisterType<SignatureService>().As<ISignatureService>().InstancePerLifetimeScope();
			// e-Signature S1 (L3 sealing) and S2 (L4 external signers); each external-signer subject kind has one source.
			builder.RegisterType<Signatures.DocumentSealingService>().As<IDocumentSealingService>().InstancePerLifetimeScope();
			builder.RegisterType<Signatures.SignatureRequestService>().As<ISignatureRequestService>().InstancePerLifetimeScope();
			builder.RegisterType<Signatures.ESignAccountService>().As<IESignAccountService>().InstancePerLifetimeScope();
			builder.RegisterType<Signatures.ESignWebhookService>().As<IESignWebhookService>().InstancePerLifetimeScope();
			builder.RegisterType<Signatures.TimeReportSignatureSource>().As<ISignatureSubjectSource>().InstancePerLifetimeScope();
			builder.RegisterType<Signatures.BidSignatureSource>().As<ISignatureSubjectSource>().InstancePerLifetimeScope();
			builder.RegisterType<Signatures.ServiceContractSignatureSource>().As<ISignatureSubjectSource>().InstancePerLifetimeScope();
			builder.RegisterType<Signatures.RecordsInspectionSignatureSource>().As<ISignatureSubjectSource>().InstancePerLifetimeScope();
			builder.RegisterType<Signatures.CustodyTransferSignatureSource>().As<ISignatureSubjectSource>().InstancePerLifetimeScope();
			// e-Signature S3: multi-signer requests (eligibility resolver, member notifications), the Readiness Pro subjects and the
			// checklist witness signature (free with Checklists).
			builder.RegisterType<Signatures.SignerEligibilityService>().As<ISignerEligibilityService>().InstancePerLifetimeScope();
			builder.RegisterType<Signatures.SignatureNotificationService>().As<ISignatureNotificationService>().InstancePerLifetimeScope();
			builder.RegisterType<Signatures.ChecklistWitnessSignatureService>().As<IChecklistWitnessSignatureService>().InstancePerLifetimeScope();
			builder.RegisterType<Signatures.WorkOrderVendorAcceptanceSignatureSource>().As<ISignatureSubjectSource>().InstancePerLifetimeScope();
			builder.RegisterType<Signatures.InventoryIssueReceiptSignatureSource>().As<ISignatureSubjectSource>().InstancePerLifetimeScope();
			// e-Signature S4 approval chains (M0385-M0386, worker 97): the chain service on the S3 request, the module gate, one module source per
			// approval kind (IBO requisitions and IAP, Business Ops invoices and MARS packages, mutual-aid agreements; payroll runs have no entity
			// yet), the signing layer's view of each kind, the IBO ledger gates and the notifications (EventTypes 73-74).
			builder.RegisterType<Approvals.ApprovalChainService>().As<IApprovalChainService>().InstancePerLifetimeScope();
			builder.RegisterType<Approvals.ApprovalGateService>().As<IApprovalGateService>().InstancePerLifetimeScope();
			builder.RegisterType<Approvals.ApprovalNotificationService>().As<IApprovalNotificationService>().InstancePerLifetimeScope();
			builder.RegisterType<Approvals.MutualAidAgreementService>().As<IMutualAidAgreementService>().InstancePerLifetimeScope();
			builder.RegisterType<Approvals.PurchaseRequisitionApprovalSource>().As<IApprovalSubjectSource>().InstancePerLifetimeScope();
			builder.RegisterType<Approvals.IncidentPlanApprovalSource>().As<IApprovalSubjectSource>().InstancePerLifetimeScope();
			builder.RegisterType<Approvals.InvoiceApprovalSource>().As<IApprovalSubjectSource>().InstancePerLifetimeScope();
			builder.RegisterType<Approvals.CalOesMarsPackageApprovalSource>().As<IApprovalSubjectSource>().InstancePerLifetimeScope();
			builder.RegisterType<Approvals.MutualAidAgreementApprovalSource>().As<IApprovalSubjectSource>().InstancePerLifetimeScope();
			foreach (var approvalKind in new[] { Model.SignatureSubjectKinds.PurchaseRequisitionApproval, Model.SignatureSubjectKinds.IncidentPlanApproval, Model.SignatureSubjectKinds.InvoiceApproval,
				Model.SignatureSubjectKinds.CalOesMarsPackageApproval, Model.SignatureSubjectKinds.MutualAidAgreementApproval })
			{
				var kind = approvalKind;
				builder.Register(c => new Approvals.ApprovalSignatureSubjectSource(kind, c.Resolve<System.Collections.Generic.IEnumerable<IApprovalSubjectSource>>(), c.Resolve<Lazy<IApprovalChainService>>()))
					.As<ISignatureSubjectSource>().InstancePerLifetimeScope();
			}
			builder.Register(c => new Approvals.ApprovalChainLedgerRule<Model.IncidentLogistics.PurchaseRequisition>(c.Resolve<IApprovalGateService>(),
				Model.SignatureSubjectKinds.PurchaseRequisitionApproval, (int)Model.IncidentLogistics.IncidentRequisitionStatuses.Approved, e => e.PurchaseRequisitionId))
				.As<IIncidentLedgerRule<Model.IncidentLogistics.PurchaseRequisition>>().InstancePerLifetimeScope();
			builder.Register(c => new Approvals.ApprovalChainLedgerRule<Model.IncidentLogistics.IncidentPlanVersion>(c.Resolve<IApprovalGateService>(),
				Model.SignatureSubjectKinds.IncidentPlanApproval, (int)Model.IncidentLogistics.IncidentPlanVersionStatuses.Approved, e => e.IncidentPlanVersionId))
				.As<IIncidentLedgerRule<Model.IncidentLogistics.IncidentPlanVersion>>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolAccessService>().As<IProtocolAccessService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolLibraryService>().As<IProtocolLibraryService>().InstancePerLifetimeScope();
			// K1 department knowledge (M0353-M0355, worker 89): the reindex queue the content services write to, the corpus sources, the
			// index, the answers and the gaps report. The page-text store default yields no pages until file extraction registers its own.
			builder.RegisterType<Knowledge.KnowledgeIndexQueue>().As<IKnowledgeIndexQueue>().InstancePerLifetimeScope();
			builder.RegisterType<Knowledge.NullKnowledgePageTextSource>().As<IKnowledgePageTextSource>().InstancePerLifetimeScope().PreserveExistingDefaults();
			// Protocol file text (P1 wave-2 extraction, M0347) feeds the ProtocolFile corpus; training files and documents still yield no pages.
			builder.RegisterType<Knowledge.ProtocolKnowledgePageTextSource>().As<IKnowledgePageTextSource>().InstancePerLifetimeScope();
			builder.RegisterType<Knowledge.ProtocolKnowledgeSource>().As<IKnowledgeCorpusSource>().InstancePerLifetimeScope();
			builder.RegisterType<Knowledge.TrainingKnowledgeSource>().As<IKnowledgeCorpusSource>().InstancePerLifetimeScope();
			builder.RegisterType<Knowledge.AcademyKnowledgeSource>().As<IKnowledgeCorpusSource>().InstancePerLifetimeScope();
			builder.RegisterType<Knowledge.DocumentKnowledgeSource>().As<IKnowledgeCorpusSource>().InstancePerLifetimeScope();
			builder.RegisterType<Knowledge.KnowledgeIndexService>().As<IKnowledgeIndexService>().InstancePerLifetimeScope();
			builder.RegisterType<Knowledge.KnowledgeGapsService>().As<IKnowledgeGapsService>().InstancePerLifetimeScope();
			builder.RegisterType<Knowledge.AiKnowledgeService>().As<IAiKnowledgeService>().InstancePerLifetimeScope();
			// K2 authoring aids: drafts from protocol versions, trainings and LMS course versions (each source decides authorship and "no AI").
			builder.RegisterType<Knowledge.ProtocolAuthoringAidSource>().As<IAuthoringAidSource>().InstancePerLifetimeScope();
			builder.RegisterType<Knowledge.TrainingAuthoringAidSource>().As<IAuthoringAidSource>().InstancePerLifetimeScope();
			builder.RegisterType<LmsCourseAuthoringAidSource>().As<IAuthoringAidSource>().InstancePerLifetimeScope();
			builder.RegisterType<Knowledge.AuthoringAidService>().As<IAuthoringAidService>().InstancePerLifetimeScope();
			// K3: LMS courses join the corpus (and serve "Ask this course"), call-context answers read a call's operational fields only (the
			// determinant reader is P4's; the default yields none and never replaces one registered by P4), scenario practice and the LMS
			// learning recommendations.
			builder.RegisterType<LmsCourseKnowledgeSource>().As<IKnowledgeCorpusSource>().As<IKnowledgeVersionContentSource>().InstancePerLifetimeScope();
			// Question bank explanations (owner decision 2026-11-26): readable only by those the bank editor shows the answer keys to.
			builder.RegisterType<LmsQuestionExplanationKnowledgeSource>().As<IKnowledgeCorpusSource>().InstancePerLifetimeScope();
			builder.RegisterType<Knowledge.NullKnowledgeCallDeterminantSource>().As<IKnowledgeCallDeterminantSource>().InstancePerLifetimeScope().PreserveExistingDefaults();
			// P4 case flow: the call's determinant (CallProtocols.Determinant) for call-context answers.
			builder.RegisterType<Knowledge.CallProtocolDeterminantSource>().As<IKnowledgeCallDeterminantSource>().InstancePerLifetimeScope();
			builder.RegisterType<Knowledge.KnowledgeCallContextService>().As<IKnowledgeCallContextService>().InstancePerLifetimeScope();
			builder.RegisterType<Knowledge.KnowledgePracticeService>().As<IKnowledgePracticeService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsRecommendationService>().As<ILmsRecommendationService>().InstancePerLifetimeScope();
			// T5 AI and content (round 6F): course drafts from a protocol or SOP and flashcards (Enhanced AI and the LMS), the Resgrid starter
			// course library (the LMS), and the publish hook that flags flashcards whose source version was superseded.
			builder.RegisterType<LmsCourseDraftService>().As<ILmsCourseDraftService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsFlashcardService>().As<ILmsFlashcardService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsStarterCourseService>().As<ILmsStarterCourseService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsFlashcardSupersedeHook>().As<IContentVersionPublishedHook>().InstancePerLifetimeScope();
			// Protocols P1 wave-2 remainder (document-backed versions, file text) and P2 (distribution and acknowledgment).
			builder.RegisterType<ProtocolDocumentService>().As<IProtocolDocumentService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolAcknowledgmentService>().As<IProtocolAcknowledgmentService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolNotificationService>().As<IProtocolNotificationService>().InstancePerLifetimeScope();
			// Protocols P3 field tools: step-through on calls, offline manifest and staleness, the PDF book and content translations.
			builder.RegisterType<ProtocolStepThroughService>().As<IProtocolStepThroughService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolOfflineService>().As<IProtocolOfflineService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolBookService>().As<IProtocolBookService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolTranslationService>().As<IProtocolTranslationService>().InstancePerLifetimeScope();
			builder.RegisterType<LmsProtocolLearningService>().As<IProtocolLearningExtensions>().InstancePerLifetimeScope();
			// Protocols P4: the shared trigger engine's facts, the case flow (overrides, caller texts) and the Enhanced AI protocol suggestion.
			builder.RegisterType<DispatchTriggerFactsService>().As<IDispatchTriggerFactsService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolCaseService>().As<IProtocolCaseService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolAiSuggestionService>().As<IProtocolAiSuggestionService>().InstancePerLifetimeScope();
			// Enhanced AI on protocols (round 7A, Enhanced AI only): protocol flashcards with their supersede hook, a baseline training drafted from a
			// protocol version, and translation drafts of a version's wording.
			builder.RegisterType<ProtocolFlashcardService>().As<IProtocolFlashcardService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolFlashcardSupersedeHook>().As<IContentVersionPublishedHook>().InstancePerLifetimeScope();

			// Round 10 A3: Protocols share kind on Department Links, protocol and course sets (publisher/subscriber, lock/override, inbox,
			// proposals), sponsored LMS seats and mutual-aid countersignature over the link.
			builder.RegisterType<LinkedProtocolService>().As<ILinkedProtocolService>().InstancePerLifetimeScope();
			builder.RegisterType<ContentSetCopyLocks>().As<IContentSetCopyLocks>().InstancePerLifetimeScope();
			builder.RegisterType<ContentSets.ContentSetService>().As<IContentSetService>().InstancePerLifetimeScope();
			builder.RegisterType<ContentSets.ProtocolSetItemSource>().As<IContentSetItemSource>().InstancePerLifetimeScope();
			builder.RegisterType<ContentSets.LmsCourseSetItemSource>().As<IContentSetItemSource>().InstancePerLifetimeScope();
			builder.RegisterType<ContentSets.TrainingSetItemSource>().As<IContentSetItemSource>().InstancePerLifetimeScope();
			builder.RegisterType<ContentSets.LmsPathSetItemSource>().As<IContentSetItemSource>().InstancePerLifetimeScope();
			builder.RegisterType<ContentSets.LmsSetFiles>().As<ILmsSetFiles>().InstancePerLifetimeScope();
			builder.RegisterType<ContentSets.ContentSetNotifier>().As<IContentSetNotifier>().InstancePerLifetimeScope();
			builder.RegisterType<ContentSets.ContentSetPublishedHook>().As<IContentVersionPublishedHook>().InstancePerLifetimeScope();
			builder.RegisterType<ContentSets.ContentSetLinkAccessListener>().As<IDepartmentLinkAccessListener>().InstancePerLifetimeScope();
			builder.RegisterType<ContentSets.LmsSponsorshipService>().As<ILmsSponsorshipService>().InstancePerLifetimeScope();
			builder.RegisterType<Approvals.MutualAidCountersignService>().As<IMutualAidCountersignService>().InstancePerLifetimeScope();
			builder.RegisterType<Approvals.MutualAidCountersignatureSource>().As<ISignatureSubjectSource>().InstancePerLifetimeScope();
			builder.RegisterType<Approvals.MutualAidCountersignatureAccessRule>().As<ISignatureSubjectAccessRule>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordDeploymentPartnerService>().As<IRecordDeploymentPartnerService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordDeploymentPartnerAcceptanceSource>().As<ISignatureSubjectSource>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordDeploymentPartnerAcceptanceAccessRule>().As<ISignatureSubjectAccessRule>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolTrainingDraftService>().As<IProtocolTrainingDraftService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolTranslationDraftService>().As<IProtocolTranslationDraftService>().InstancePerLifetimeScope();
			// Protocols P5 (round 6 A): public pages, the template library, the community exchange (with its item sources) and analytics
			builder.RegisterType<ProtocolPublicPageService>().As<IProtocolPublicPageService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolTemplateLibraryService>().As<IProtocolTemplateLibraryService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolAnalyticsService>().As<IProtocolAnalyticsService>().InstancePerLifetimeScope();
			builder.RegisterType<CommunityExchangeService>().As<ICommunityExchangeService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolExchangeItemSource>().As<IExchangeItemSource>().InstancePerLifetimeScope();
			builder.RegisterType<TrainingExchangeItemSource>().As<IExchangeItemSource>().InstancePerLifetimeScope();
			builder.RegisterType<LmsSkillSheetExchangeSource>().As<IExchangeItemSource>().InstancePerLifetimeScope();
			builder.RegisterType<LmsLessonPlanExchangeSource>().As<IExchangeItemSource>().InstancePerLifetimeScope();
			builder.RegisterType<LlmProtocolCaseSuggester>().As<IProtocolCaseSuggester>().InstancePerLifetimeScope();
			// Protocols P5 QA: dispatch case review (sampling, scoring against a Records QA rubric, dispatcher feedback, reports).
			builder.RegisterType<DispatchCaseReviewService>().As<IDispatchCaseReviewService>().InstancePerLifetimeScope();
			builder.RegisterType<DispatchCaseReviewNotifier>().As<IDispatchCaseReviewNotifier>().InstancePerLifetimeScope();
			builder.RegisterType<FormsService>().As<IFormsService>().InstancePerLifetimeScope();
			builder.RegisterType<VoiceService>().As<IVoiceService>().InstancePerLifetimeScope();
			builder.RegisterType<SystemAuditsService>().As<ISystemAuditsService>().InstancePerLifetimeScope();
			builder.RegisterType<ContactVerificationService>().As<IContactVerificationService>().InstancePerLifetimeScope();
			builder.RegisterType<SecurityPinService>().As<ISecurityPinService>().InstancePerLifetimeScope();
			builder.RegisterType<TextDepartmentSwitchService>().As<ITextDepartmentSwitchService>().InstancePerLifetimeScope();
			builder.RegisterType<AutofillsService>().As<IAutofillsService>().InstancePerLifetimeScope();
			builder.RegisterType<UnitStatesService>().As<IUnitStatesService>().InstancePerLifetimeScope();
			builder.Register(_ =>
				{
					if (string.IsNullOrWhiteSpace(TtsConfig.ServiceBaseUrl))
						throw new InvalidOperationException("TtsConfig.ServiceBaseUrl must be configured before using the TTS service.");

					// 30s: long enough to ride out a cold TTS generation (Piper model load +
					// synthesis + normalization + upload can take 10-20s on constrained pods).
					// Twilio webhooks never block on this — they bound their own waits with
					// short CancellationTokens and fall back to "please wait"/<Say> — so the
					// long timeout only affects background pre-warms and worker tasks, where
					// waiting out the generation is exactly what we want.
					var options = new RestClientOptions(TtsConfig.ServiceBaseUrl.TrimEnd('/'))
					{
						Timeout = TimeSpan.FromSeconds(30)
					};

					return new RestClient(options, configureSerialization: serializer => serializer.UseNewtonsoftJson());
				})
				.Named<RestClient>(TtsRestClientRegistrationName)
				.SingleInstance();
			builder.RegisterType<TtsAudioService>()
				.As<ITtsAudioService>()
				.WithParameter(
					(parameter, _) => parameter.ParameterType == typeof(Func<RestClient>),
					(_, context) =>
					{
						// Hand TtsAudioService a factory rather than an eagerly-resolved RestClient. This
						// keeps activation of the service (and any controller that depends on it, e.g. the
						// Twilio controller that also handles incoming SMS) from forcing the named TTS
						// RestClient to be built. The ServiceBaseUrl check only runs when the factory is
						// invoked, i.e. when TTS audio is actually generated. Capture the lifetime scope
						// instead of the transient IComponentContext so the factory is safe to call later.
						var scope = context.Resolve<ILifetimeScope>();
						return (Func<RestClient>)(() => scope.ResolveNamed<RestClient>(TtsRestClientRegistrationName));
					})
				.InstancePerLifetimeScope();

			// SSO / Security Policy
			builder.RegisterType<DepartmentSsoService>().As<IDepartmentSsoService>().InstancePerLifetimeScope();
			builder.RegisterType<UserSessionService>().As<IUserSessionService>().InstancePerLifetimeScope();
			builder.RegisterType<SharedSessionService>().As<ISharedSessionService>().InstancePerLifetimeScope();
			builder.RegisterType<MfaActivityService>().As<IMfaActivityService>().InstancePerLifetimeScope();
			builder.RegisterType<MfaCredentialStateService>().As<IMfaCredentialStateService>().InstancePerLifetimeScope();
			builder.RegisterType<AdpStepUpService>().As<IAdpStepUpService>().InstancePerLifetimeScope();
			// Department step-up policy (round 10 A5): per-area rules and evidence evaluation, and what a member can use (refusals, warning).
			builder.RegisterType<StepUpPolicyService>().As<IStepUpPolicyService>().InstancePerLifetimeScope();
			builder.RegisterType<StepUpCapabilityService>().As<IStepUpCapabilityService>().InstancePerLifetimeScope();

			// Passkey plan Phase 1 slice 2: MFA evidence, single-use ceremony challenges and the per-client relying parties.
			// The registry parses static config once; the gates only report ON when that configuration validated.
			builder.RegisterType<MfaEvidenceService>().As<IMfaEvidenceService>().InstancePerLifetimeScope();
			builder.RegisterType<MfaPolicyService>().As<IMfaPolicyService>().InstancePerLifetimeScope();
			builder.RegisterType<AuthenticationChallengeService>().As<IAuthenticationChallengeService>().InstancePerLifetimeScope();
			builder.Register(_ => new RelyingPartyRegistry()).As<IRelyingPartyRegistry>().SingleInstance();
			builder.RegisterType<PasskeyFeatureGates>().As<IPasskeyFeatureGates>().SingleInstance();
			// Needs IPasskeyProvider from Resgrid.Providers.Authentication, which only the ceremony hosts (Web, API) load.
			builder.RegisterType<PasskeyService>().As<IPasskeyService>().InstancePerLifetimeScope();
			builder.RegisterType<MfaLoginTransactionService>().As<IMfaLoginTransactionService>().InstancePerLifetimeScope();
			builder.RegisterType<MfaApprovalService>().As<IMfaApprovalService>().InstancePerLifetimeScope();
			builder.RegisterType<FactorRecoveryService>().As<IFactorRecoveryService>().InstancePerLifetimeScope();
			builder.RegisterType<SecurityNoticeService>().As<ISecurityNoticeService>().InstancePerLifetimeScope();
			builder.RegisterType<MfaAccountCleanupService>().As<IMfaAccountCleanupService>().InstancePerLifetimeScope();
			builder.Register(_ => new SsoReturnTargetRegistry()).As<ISsoReturnTargetRegistry>().SingleInstance();
			// Needs IOidcProviderClient from Resgrid.Providers.Authentication, loaded by Web and API.
			builder.RegisterType<SsoBrokerService>().As<ISsoBrokerService>().InstancePerLifetimeScope();
			builder.RegisterType<ClientSessionMetadataParser>().As<IClientSessionMetadataParser>().SingleInstance();
			builder.RegisterType<LocalIpLocationProvider>().As<IIpLocationProvider>().SingleInstance();
			builder.RegisterType<ExternalIdentityLinkService>().As<IExternalIdentityLinkService>().InstancePerLifetimeScope();
			builder.RegisterType<PasswordRecoveryService>().As<IPasswordRecoveryService>().InstancePerLifetimeScope();
			builder.RegisterType<LoginThrottleService>().As<ILoginThrottleService>().InstancePerLifetimeScope();
			builder.RegisterType<PublicFormRateLimiter>().As<IPublicFormRateLimiter>().InstancePerLifetimeScope();

			// Advanced Data Protection (ADP)
			builder.RegisterType<AdpReleaseService>().As<IAdpReleaseService>().InstancePerLifetimeScope();
			builder.RegisterType<AdpReleaseReceiptService>().As<IAdpReleaseReceiptService>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentDataProtectionService>().As<IDepartmentDataProtectionService>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentLockService>().As<IDepartmentLockService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtectedFieldCatalog>().As<IProtectedFieldCatalog>().SingleInstance();
			builder.RegisterType<DepartmentKeyService>().As<IDepartmentKeyService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtectedFieldCryptoService>().As<IProtectedFieldCryptoService>().SingleInstance();
			builder.RegisterType<ProtectedDataGrantService>().As<IProtectedDataGrantService>().SingleInstance();
			// Dedicated session-assertion key (passkey workbook section 6.2): Web and API mint, the broker validates.
			builder.RegisterType<BrokerSessionAssertionService>().As<IBrokerSessionAssertionService>().SingleInstance();
			builder.RegisterType<DepartmentMemberSensitiveDataService>().As<IDepartmentMemberSensitiveDataService>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentMemberEmergencyContactService>().As<IDepartmentMemberEmergencyContactService>().InstancePerLifetimeScope();
			builder.RegisterType<MemberProfileRelocationService>().As<IMemberProfileRelocationService>().InstancePerLifetimeScope();
			// Attended protected reads + the write safety net. Requires IProtectedDataBrokerClient,
			// so every composition root that loads this module must also load
			// ProtectedDataBrokerClientModule (client only — no key material).
			builder.RegisterType<ProtectedReadService>()
				.As<IProtectedReadService>().As<IProtectedWriteService>().InstancePerLifetimeScope();
			builder.RegisterType<WorkloadProtectedGrantContext>().As<IProtectedGrantContext>().InstancePerLifetimeScope();

			// The real engine is registered everywhere but only functions where a real key wrapping
			// provider resolves (LocalDev for synthetic testing; the broker host in production). On
			// the app tier the NotConfigured provider makes every run fail closed with
			// kms_unavailable, which the coordinator treats like any other unrecoverable error.
			builder.RegisterType<DepartmentDataMigrationEngine>().As<IDepartmentDataMigrationEngine>().InstancePerLifetimeScope();
			builder.RegisterType<AdpSizingService>().As<IAdpSizingService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtectedProjectionService>().As<IProtectedProjectionService>().InstancePerLifetimeScope();

			// Key wrapping: only the LocalDev provider (synthetic/non-PHI testing; refuses to run in
			// production) is resolvable in-process. Any other configured provider registers the
			// fail-closed placeholder — real KMS adapters live with the Protected Data Broker, which
			// Web/API/worker hosts deliberately cannot reach (ADP plan section 2.2).
			if (string.Equals(Config.DataProtectionConfig.KeyWrappingProviderType, "LocalDev", StringComparison.OrdinalIgnoreCase))
				builder.RegisterType<LocalDevKeyWrappingProvider>().As<IKeyWrappingProvider>().SingleInstance();
			else
				// PreserveExistingDefaults: if a composition root loaded a REAL adapter module before
				// this one (module order is host code, not a guarantee), the fail-closed placeholder
				// must not silently win the last-registration race and break the broker.
				builder.RegisterType<NotConfiguredKeyWrappingProvider>().As<IKeyWrappingProvider>().SingleInstance().PreserveExistingDefaults();

			//builder.RegisterType<InternalCacheService>().As<IInternalCacheService>().SingleInstance();
			builder.RegisterType<CoreEventService>().As<ICoreEventService>().SingleInstance();
			// Per scope, not SingleInstance: a singleton's repositories are resolved from the root scope, so every request
			// would share one root IUnitOfWork and never join the caller's transaction.
			builder.RegisterType<WorkShiftsService>().As<IWorkShiftsService>().InstancePerLifetimeScope();
			builder.RegisterType<ShiftStaffingService>().As<IShiftStaffingService>().InstancePerLifetimeScope();
			builder.RegisterType<ContactsService>().As<IContactsService>().InstancePerLifetimeScope();
			builder.RegisterType<IndoorMapService>().As<IIndoorMapService>().InstancePerLifetimeScope();
			builder.RegisterType<CustomMapService>().As<ICustomMapService>().InstancePerLifetimeScope();
			builder.RegisterType<RouteService>().As<IRouteService>().InstancePerLifetimeScope();
			builder.RegisterType<RoutePlanRoutingService>().As<IRoutePlanRoutingService>().InstancePerLifetimeScope();
			builder.RegisterType<CallRoutesService>().As<ICallRoutesService>().InstancePerLifetimeScope();
			builder.RegisterType<UnitNavigationStateService>().As<IUnitNavigationStateService>().InstancePerLifetimeScope();
			builder.RegisterType<OfflineMapPackService>().As<IOfflineMapPackService>().InstancePerLifetimeScope();
			builder.RegisterType<CheckInTimerService>().As<ICheckInTimerService>().InstancePerLifetimeScope();
			builder.RegisterType<UnitStatusAlertsService>().As<IUnitStatusAlertsService>().InstancePerLifetimeScope();
			builder.RegisterType<CallLocationHistoryService>().As<ICallLocationHistoryService>().InstancePerLifetimeScope();
			builder.RegisterType<RunCardsService>().As<IRunCardsService>().InstancePerLifetimeScope();
			builder.RegisterType<ResponsePackagesService>().As<IResponsePackagesService>().InstancePerLifetimeScope();
			builder.RegisterType<OutstandingRequirementsService>().As<IOutstandingRequirementsService>().InstancePerLifetimeScope();
			builder.RegisterType<TransportDestinationCheckService>().As<ITransportDestinationCheckService>().InstancePerLifetimeScope();
			builder.RegisterType<NearestDestinationService>().As<INearestDestinationService>().InstancePerLifetimeScope();
			builder.RegisterType<DestinationRulesService>().As<IDestinationRulesService>().InstancePerLifetimeScope();
			builder.RegisterType<EmailCallUpdateService>().As<IEmailCallUpdateService>().InstancePerLifetimeScope();
			// Intake channels and Enhanced AI voice intake (call-intake-and-ai-destinations-plan.md, workstream C).
			builder.RegisterType<Intake.IntakeChannelsService>().As<IIntakeChannelsService>().InstancePerLifetimeScope();
			builder.RegisterType<Intake.IntakeService>().As<IIntakeService>().InstancePerLifetimeScope();
			builder.RegisterType<Intake.VoiceIntakeService>().As<IVoiceIntakeService>().InstancePerLifetimeScope();
			// Voice intake call recording (M0332): started after the caller accepts at the gate, attached to the pending call as a file.
			builder.RegisterType<Intake.VoiceIntakeRecordingService>().As<IVoiceIntakeRecordingService>().InstancePerLifetimeScope();
			// Intake spam handling (plan §9, M0326): blocked senders, dismiss as spam, screening labels.
			builder.RegisterType<Intake.IntakeScreeningService>().As<IIntakeScreeningService>().InstancePerLifetimeScope();
			// Intake SMS conversations (plan §8): one instance serves the desk contract and the intake/worker operations.
			builder.RegisterType<Intake.IntakeConversationService>().As<IIntakeConversationService>().As<IIntakeConversationOperations>().InstancePerLifetimeScope();
			builder.RegisterType<Intake.IntakeConversationAssistant>().As<IIntakeConversationAssistant>().InstancePerLifetimeScope();
			builder.RegisterType<Intake.TwilioSpeechTranscriber>().As<IVoiceIntakeTranscriber>().InstancePerLifetimeScope();
			// Plan D8: CallsService takes it as a Lazy (it reaches the rules service, which reaches back to calls).
			builder.RegisterType<DestinationAutoFillService>().As<IDestinationAutoFillService>().InstancePerLifetimeScope();
			builder.RegisterType<RecurringCallsService>().As<IRecurringCallsService>().InstancePerLifetimeScope();
			// Dispatcher desk (M0324): presence, breaks and the conversation service reach routing through Lazy<> (it calls back
			// into them), routing reaches calls through Lazy<> (call services notify the desk).
			builder.RegisterType<DispatcherDeskConfigService>().As<IDispatcherDeskConfigService>().InstancePerLifetimeScope();
			builder.RegisterType<ShiftDispatchersService>().As<IShiftDispatchersService>().InstancePerLifetimeScope();
			builder.RegisterType<DispatcherDeskNotifier>().As<IDispatcherDeskNotifier>().InstancePerLifetimeScope();
			builder.RegisterType<DispatcherPresenceService>().As<IDispatcherPresenceService>().InstancePerLifetimeScope();
			builder.RegisterType<DispatcherRoutingService>().As<IDispatcherRoutingService>().InstancePerLifetimeScope();
			builder.RegisterType<DispatcherBreaksService>().As<IDispatcherBreaksService>().InstancePerLifetimeScope();
			builder.RegisterType<DispatcherConversationService>().As<IDispatcherConversationService>().InstancePerLifetimeScope();
			builder.RegisterType<DispatcherVoiceService>().As<IDispatcherVoiceService>().InstancePerLifetimeScope();
			// The voicemail's pending call: the voice webhook saves it, worker 82 recovers one whose request stopped.
			builder.RegisterType<DispatcherDeskPendingCalls>().As<IDispatcherDeskPendingCalls>().InstancePerLifetimeScope();
			builder.RegisterType<DispatcherDeskWorkerService>().As<IDispatcherDeskWorkerService>().InstancePerLifetimeScope();
			// Intake text threads reach the desk through this router (intake resolves every IIntakeConversationRouter; none = no desk).
			builder.RegisterType<DispatcherIntakeConversationRouter>().As<IIntakeConversationRouter>().InstancePerLifetimeScope();
			builder.RegisterType<RunCardTemplatesService>().As<IRunCardTemplatesService>().InstancePerLifetimeScope();
			builder.RegisterType<PersonnelLocationResolver>().As<IPersonnelLocationResolver>().InstancePerLifetimeScope();
			// Singleton: it keeps each department's reduced visibility matrix in memory between location pings.
			builder.RegisterType<LocationVisibilityService>().As<ILocationVisibilityService>().SingleInstance();
			builder.RegisterType<DispatchRecommendationService>().As<IDispatchRecommendationService>().InstancePerLifetimeScope();
			builder.RegisterType<DispatchScopeService>().As<IDispatchScopeService>().InstancePerLifetimeScope();
			builder.RegisterType<NearestUnitService>().As<INearestUnitService>().InstancePerLifetimeScope();

			// UDF Services
			builder.RegisterType<UserDefinedFieldsService>().As<IUserDefinedFieldsService>().InstancePerLifetimeScope();
			builder.RegisterType<UdfRenderingService>().As<IUdfRenderingService>().InstancePerLifetimeScope();


			// Stripe Services
			//builder.RegisterType<StripeSubscriptionServiceFacade>().As<IStripeSubscriptionServiceFacade>().InstancePerLifetimeScope();
			//builder.RegisterType<StripeInvoiceServiceFacade>().As<IStripeInvoiceServiceFacade>().InstancePerLifetimeScope();
			//builder.RegisterType<StripeChargeServiceFacade>().As<IStripeChargeServiceFacade>().InstancePerLifetimeScope();

			// GDPR Services
			builder.RegisterType<GdprDataExportService>().As<IGdprDataExportService>().InstancePerLifetimeScope();
			// Round 5F: the LMS part of a member's GDPR export (enrollments, attempts without keys, requirement status, training requests).
			builder.RegisterType<LmsGdprExportSection>().As<IGdprExportSection>().InstancePerLifetimeScope();
			// Round 7B: the completions an instructor recorded for the member (who, when, from where, the note, the attestation, any void).
			builder.RegisterType<TrainingCompletionRecordingsGdprSection>().As<IGdprExportSection>().InstancePerLifetimeScope();
			// Round 9B: the member's hours submissions, their review history and their evidence files' metadata (never the bytes).
			builder.RegisterType<TrainingSubmissionsGdprSection>().As<IGdprExportSection>().InstancePerLifetimeScope();
			// Protocols P5 QA: the member's own sent dispatch case reviews.
			builder.RegisterType<DispatchCaseReviewGdprExportSection>().As<IGdprExportSection>().InstancePerLifetimeScope();
			// Round 9A: the member's certification issuance ledger (automatic issues and renewals, and voids that took one back).
			builder.RegisterType<LmsCertificationIssuanceGdprSection>().As<IGdprExportSection>().InstancePerLifetimeScope();

			// Communication Test Services
			builder.RegisterType<CommunicationTestService>().As<ICommunicationTestService>().InstancePerLifetimeScope();

			// Weather Alert Services
			builder.RegisterType<WeatherAlertService>().As<IWeatherAlertService>().InstancePerLifetimeScope();

			// Menu and tab badge counts (v4 Counts/GetBadgeCounts): the registry and every IBadgeCountProvider in this assembly.
			builder.RegisterType<BadgeCounts.BadgeCountsService>().As<IBadgeCountsService>().InstancePerLifetimeScope();
			RegisterBadgeCountProviders(builder, typeof(ServicesModule).Assembly);

			// Records (RMS) Services - RMS plan section 5.11.1
			builder.RegisterType<Records.RecordsCutoverService>().As<IRecordsCutoverService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.DomainEventOutboxService>().As<IDomainEventOutboxService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsAuthorizationService>().As<IRecordsAuthorizationService>().InstancePerLifetimeScope();
			// Value seam (plan 5.9.1): the only caller of the details repository; enrollment hooks in here.
			builder.RegisterType<Records.RecordsProtectionService>().As<IRecordsProtectionService>().As<IRecordsProtectedReadService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RmsRecordValueService>().As<IRmsRecordValueService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsService>().As<IRecordsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsNumberingService>().As<IRecordsNumberingService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RmsInventoryUsageAdapter>().As<IRmsInventoryUsageAdapter>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsAccountabilityService>().As<IRecordsAccountabilityService>().InstancePerLifetimeScope();
			// RMS-2: NERIS incident reports and the submission worker logic
			builder.RegisterType<Records.IncidentReportsService>().As<IIncidentReportsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.IncidentAttachmentsService>().As<IIncidentAttachmentsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsDocumentService>().As<IRecordsDocumentService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsSubmissionService>().As<IRecordsSubmissionService>().InstancePerLifetimeScope();
			// RMS-1B v4 Records API support: keyed short-lived state, command idempotency, resumable attachment sessions
			builder.RegisterType<Records.RecordsApiStateStore>().As<IRecordsApiStateStore>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsApiIdempotencyService>().As<IRecordsApiIdempotencyService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordAttachmentUploadService>().As<IRecordAttachmentUploadService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsNotificationService>().As<IRecordsNotificationService>().InstancePerLifetimeScope();

			// Member provisioning shared by AddPerson, SCIM and Department Import (wizard plan section 11.2). The account
			// service needs UserManager, which the Web, API and worker hosts register.
			builder.RegisterType<MemberAccountService>().As<IMemberAccountService>().InstancePerLifetimeScope();
			builder.RegisterType<MemberProvisioningService>().As<IMemberProvisioningService>().InstancePerLifetimeScope();

			// Department Import (Import & Migration, wizard plan section 18.2)
			builder.RegisterType<Onboarding.DepartmentImportAccessService>().As<IDepartmentImportAccessService>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.DepartmentImportService>().As<IDepartmentImportService>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportUploadService>().As<IImportUploadService>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportRunQueue>().As<IImportRunQueue>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportRunService>().As<IImportRunService>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportTimelineService>().As<IImportTimelineService>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.DepartmentImportNotificationService>().As<IDepartmentImportNotificationService>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportKeyService>().As<Onboarding.IImportKeyService>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportStagingService>().AsSelf().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportValidationService>().As<Onboarding.IImportValidationService>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportAnalysisHandler>().As<IImportRunHandler>().As<IImportAnalysisService>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportMappingService>().As<IImportMappingService>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportIssueService>().As<IImportIssueService>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.DepartmentImportSupportService>().As<IDepartmentImportSupportService>().As<IDepartmentImportSupportOutbox>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.DepartmentImportMaintenanceService>().As<IDepartmentImportMaintenanceService>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportPlanService>().As<IImportPlanService>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportEffectsRunner>().AsSelf().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportCommitHandler>().As<IImportRunHandler>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportRollbackHandler>().As<IImportRunHandler>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportActivationRunHandler>().As<IImportRunHandler>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportApprovalService>().As<IImportApprovalService>().InstancePerLifetimeScope();
			// Configure step (wizard plan section 7.5): the Phase 2 configuration domains, written first in a commit run.
			builder.RegisterType<Onboarding.ImportConfigurationTargets>().AsSelf().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportConfigurationService>().As<IImportConfigurationService>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportActivationService>().As<IImportActivationService>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.ImportTemplateService>().As<IImportTemplateService>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.DepartmentImportStaffService>().As<IDepartmentImportStaffService>().InstancePerLifetimeScope();
			// Wave-1 target writers (wizard plan section 11.1): resolved as IEnumerable<IImportTargetWriter>, per chunk scope by the commit engine.
			builder.RegisterType<Onboarding.Writers.CustomStatusesImportWriter>().As<Model.Onboarding.IImportTargetWriter>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.Writers.CertificationTypesImportWriter>().As<Model.Onboarding.IImportTargetWriter>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.Writers.GroupsImportWriter>().As<Model.Onboarding.IImportTargetWriter>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.Writers.RolesImportWriter>().As<Model.Onboarding.IImportTargetWriter>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.Writers.PersonnelImportWriter>().As<Model.Onboarding.IImportTargetWriter>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.Writers.PersonnelCertificationsImportWriter>().As<Model.Onboarding.IImportTargetWriter>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.Writers.EmergencyContactsImportWriter>().As<Model.Onboarding.IImportTargetWriter>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.Writers.UnitCertificationsImportWriter>().As<Model.Onboarding.IImportTargetWriter>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.Writers.RoleRequirementsImportWriter>().As<Model.Onboarding.IImportTargetWriter>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.Writers.UnitTypesImportWriter>().As<Model.Onboarding.IImportTargetWriter>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.Writers.UnitsImportWriter>().As<Model.Onboarding.IImportTargetWriter>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.Writers.CallTypesImportWriter>().As<Model.Onboarding.IImportTargetWriter>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.Writers.CallPrioritiesImportWriter>().As<Model.Onboarding.IImportTargetWriter>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.Writers.PoiTypesImportWriter>().As<Model.Onboarding.IImportTargetWriter>().InstancePerLifetimeScope();
			builder.RegisterType<Onboarding.Writers.PoisImportWriter>().As<Model.Onboarding.IImportTargetWriter>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsSearchIndexMaintenanceService>().As<IRecordsSearchIndexMaintenanceService>().InstancePerLifetimeScope();
			// Unified Search (plan R4 Phase 1b/2, worker 70): projections written from the entity services, the global
			// index sweep, the unified endpoint and the system-functionality catalog.
			builder.RegisterType<Search.SearchProjectionService>().As<ISearchProjectionService>().InstancePerLifetimeScope();
			builder.RegisterType<Search.SearchIndexMaintenanceService>().As<ISearchIndexMaintenanceService>().InstancePerLifetimeScope();
			builder.RegisterType<Search.SystemActionsService>().As<ISystemActionsService>().InstancePerLifetimeScope();
			builder.RegisterType<Search.UnifiedSearchService>().As<IUnifiedSearchService>().InstancePerLifetimeScope();
			// RMS-3: worker 42 (due-state evaluation, trigger 112 + notification 32) and worker 43 (retention,
			// legal hold, attachment purge and the Pending-attachment rescan).
			builder.RegisterType<Records.RecordsDueStateService>().As<IRecordsDueStateService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsRetentionService>().As<IRecordsRetentionService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.IncidentAnalysisService>().As<IIncidentAnalysisService>().InstancePerLifetimeScope();
			// RMS-3c evidence: the service owns checksum, classification, retention and audit; the six adapters
			// only decide what a bounded snapshot of their own subsystem looks like (plan section 4.5, all six ship).
			builder.RegisterType<Records.RecordsEvidenceService>().As<IRecordsEvidenceService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordEvidenceSelectionService>().As<IRecordEvidenceSelectionService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.Evidence.ReadinessPacketEvidenceAdapter>().As<IRecordEvidenceAdapter>().InstancePerLifetimeScope();
			builder.RegisterType<Records.Evidence.RunCardActivationEvidenceAdapter>().As<IRecordEvidenceAdapter>().InstancePerLifetimeScope();
			builder.RegisterType<Records.Evidence.TrackingFixEvidenceAdapter>().As<IRecordEvidenceAdapter>().InstancePerLifetimeScope();
			builder.RegisterType<Records.Evidence.ChatPromotionEvidenceAdapter>().As<IRecordEvidenceAdapter>().InstancePerLifetimeScope();
			builder.RegisterType<Records.Evidence.InventoryUsageEvidenceAdapter>().As<IRecordEvidenceAdapter>().InstancePerLifetimeScope();
			builder.RegisterType<Records.Evidence.CertificationSnapshotEvidenceAdapter>().As<IRecordEvidenceAdapter>().InstancePerLifetimeScope();
			builder.RegisterType<Records.Evidence.PackProjectionEvidenceAdapter>().As<IRecordEvidenceAdapter>().InstancePerLifetimeScope();
			// RMS-3d: public-records workflow (M0171) and the Records queue dashboards.
			builder.RegisterType<Records.RecordsDisclosureService>().As<IRecordsDisclosureService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsLegalHoldService>().As<IRecordsLegalHoldService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsUdfService>().As<IRecordsUdfService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsDashboardService>().As<IRecordsDashboardService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsReportingService>().As<IRecordsReportingService>().InstancePerLifetimeScope();
			// RMS-3: command key-time and contact/preplan feeds, the read-only NFIRS crosswalk rendering, and the
			// RecordOperationalSummaryV1 contract for downstream consumers (plan sections 4.2, 4.3, 5.1, 4.7).
			builder.RegisterType<Records.IncidentSourceFeedService>().As<IIncidentSourceFeedService>().InstancePerLifetimeScope();
			// A call's report sources (statuses with who set them, crews, dispatches, check-ins, Incident Command) for report
			// prefill and the report editors' lookup panel.
			builder.RegisterType<Records.CallSourceDataService>().As<ICallSourceDataService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordCallReportsService>().As<IRecordCallReportsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsNfirsLegacyService>().As<IRecordsNfirsLegacyService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordOperationalSummaryService>().As<IRecordOperationalSummaryService>().InstancePerLifetimeScope();
			// Department report exports via the Workflow system (RMS plan section 5.6, registry M0177, worker 45)
			builder.RegisterType<Records.RecordsExportService>().As<IRecordsExportService>().InstancePerLifetimeScope();
			// RMS-1B configurable definitions and RMS-1C packs (registry M0158-M0163)
			builder.RegisterType<Records.RecordTypedValuesService>().As<IRecordTypedValuesService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordTemplatePacksService>().As<IRecordTemplatePacksService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordDefinitionsService>().As<IRecordDefinitionsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordSavedReportsService>().As<IRecordSavedReportsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordDeploymentsService>().As<IRecordDeploymentsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsRevealService>().As<IRecordsRevealService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsBulkPacketService>().As<IRecordsBulkPacketService>().InstancePerLifetimeScope();
			// Field Records for the operational apps (RMS-1D)
			builder.RegisterType<Records.RecordWorkAssignmentsService>().As<IRecordWorkAssignmentsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.FieldRecordsService>().As<IFieldRecordsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsFieldRolloutService>().As<IRecordsFieldRolloutService>().InstancePerLifetimeScope();
			// RMS-5 prevention + investigations, RMS-4 quality review and release telemetry
			builder.RegisterType<Records.RecordsPreventionGate>().AsSelf().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsOccupancyService>().As<IRecordsOccupancyService>().As<IContactPreplanOwnershipGate>().As<IOccupancyLocationLookup>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsInspectionsService>().As<IRecordsInspectionsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsHydrantsService>().As<IRecordsHydrantsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsPermitsService>().As<IRecordsPermitsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsCrrService>().As<IRecordsCrrService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsAlarmRequestsService>().As<IRecordsAlarmRequestsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsPreventionAttachmentsService>().As<IRecordsPreventionAttachmentsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsInvestigationsService>().As<IRecordsInvestigationsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsQualityReviewService>().As<IRecordsQualityReviewService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsReleaseTelemetryService>().As<IRecordsReleaseTelemetryService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsPreventionSweepService>().As<IRecordsPreventionSweepService>().InstancePerLifetimeScope();
			// RMS-6 records analytics
			builder.RegisterType<Records.RecordsAnalyticsService>().As<IRecordsAnalyticsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.Connectors.RecordDeploymentConnectorsService>().As<IRecordDeploymentConnectorsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.Connectors.GenericOrderFeedProvider>().As<IExternalOrderFeedProvider>().SingleInstance();
			builder.RegisterType<Records.Connectors.AgencyOrderFeedProvider>().As<IExternalOrderFeedProvider>().SingleInstance();
			builder.RegisterType<Records.Connectors.IrocOrderFeedProvider>().As<IExternalOrderFeedProvider>().SingleInstance();
			builder.RegisterType<Records.Connectors.CiffcOrderFeedProvider>().As<IExternalOrderFeedProvider>().SingleInstance();
			// Default attachment scanner: no engine, rows stay Skipped. A real scanner provider replaces this registration.
			builder.RegisterType<Records.NullRecordAttachmentScanner>().As<Resgrid.Model.Providers.IRecordAttachmentScanner>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentProfileMediaService>().As<IDepartmentProfileMediaService>().InstancePerLifetimeScope();
			builder.RegisterType<TypeIconsService>().As<ITypeIconsService>().InstancePerLifetimeScope();
			builder.RegisterType<MapIconsService>().As<IMapIconsService>().InstancePerLifetimeScope();
			builder.RegisterType<MapVisualizationService>().As<IMapVisualizationService>().InstancePerLifetimeScope();
			builder.RegisterType<RoadClosureService>().As<IRoadClosureService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsPrintLayoutService>().As<IRecordsPrintLayoutService>().InstancePerLifetimeScope();

			// Home dashboard (home-dashboard-redesign-plan.md). Widget providers and option sources register themselves by
			// attribute, keyed by widget key / source name, so adding one never touches this module.
			builder.RegisterType<Dashboard.DashboardService>().As<IDashboardService>().InstancePerLifetimeScope();
			builder.RegisterType<Dashboard.DashboardEntitlementService>().As<IDashboardEntitlementService>().InstancePerLifetimeScope();
			builder.RegisterType<Dashboard.DashboardWidgetDataService>().As<IDashboardWidgetDataService>().InstancePerLifetimeScope();
			RegisterDashboardParts(builder, typeof(ServicesModule).Assembly);
		}

		/// <summary>
		/// Registers every <see cref="IBadgeCountProvider"/> in <paramref name="assembly"/>; the badge counts service takes
		/// them all and keys them by <see cref="IBadgeCountProvider.Key"/>. Public so another composition root can add its own.
		/// </summary>
		public static void RegisterBadgeCountProviders(ContainerBuilder builder, System.Reflection.Assembly assembly)
		{
			builder.RegisterAssemblyTypes(assembly)
				.Where(t => typeof(IBadgeCountProvider).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
				.As<IBadgeCountProvider>()
				.InstancePerLifetimeScope();
		}

		/// <summary>
		/// Registers every [DashboardWidget] provider and [DashboardOptionSource] in <paramref name="assembly"/> keyed by its key.
		/// Public so another composition root can scan its own assembly the same way.
		/// </summary>
		public static void RegisterDashboardParts(ContainerBuilder builder, System.Reflection.Assembly assembly)
		{
			builder.RegisterAssemblyTypes(assembly)
				.Where(t => typeof(IDashboardWidgetDataProvider).IsAssignableFrom(t) && !t.IsAbstract &&
					System.Reflection.CustomAttributeExtensions.GetCustomAttribute<Resgrid.Model.Dashboard.DashboardWidgetAttribute>(t) != null)
				.Keyed<IDashboardWidgetDataProvider>(t => System.Reflection.CustomAttributeExtensions.GetCustomAttribute<Resgrid.Model.Dashboard.DashboardWidgetAttribute>(t).Key)
				.InstancePerLifetimeScope();

			builder.RegisterAssemblyTypes(assembly)
				.Where(t => typeof(IDashboardOptionSource).IsAssignableFrom(t) && !t.IsAbstract &&
					System.Reflection.CustomAttributeExtensions.GetCustomAttribute<Resgrid.Model.Dashboard.DashboardOptionSourceAttribute>(t) != null)
				.Keyed<IDashboardOptionSource>(t => System.Reflection.CustomAttributeExtensions.GetCustomAttribute<Resgrid.Model.Dashboard.DashboardOptionSourceAttribute>(t).Name)
				.InstancePerLifetimeScope();
		}
	}
}
