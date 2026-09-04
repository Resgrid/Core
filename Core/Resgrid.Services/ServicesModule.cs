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
			builder.RegisterType<EncryptionService>().As<IEncryptionService>().InstancePerLifetimeScope();
			builder.RegisterType<WorkflowService>().As<IWorkflowService>().InstancePerLifetimeScope();
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
			builder.RegisterType<PushService>().As<IPushService>().SingleInstance();
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
			builder.RegisterType<EmailService>().As<IEmailService>().InstancePerLifetimeScope();
			builder.RegisterType<CallsService>().As<ICallsService>().InstancePerLifetimeScope();
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
			builder.RegisterType<CallDispatchStatusService>().As<ICallDispatchStatusService>().InstancePerLifetimeScope();
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
			builder.RegisterType<CommandsService>().As<ICommandsService>().InstancePerLifetimeScope();
			builder.RegisterType<CustomStateService>().As<ICustomStateService>().InstancePerLifetimeScope();
			builder.RegisterType<GeoService>().As<IGeoService>().InstancePerLifetimeScope();
			builder.RegisterType<AuditService>().As<IAuditService>().InstancePerLifetimeScope();
			builder.RegisterType<PermissionsService>().As<IPermissionsService>().InstancePerLifetimeScope();
			builder.RegisterType<ImageService>().As<IImageService>().InstancePerLifetimeScope();
			builder.RegisterType<MappingService>().As<IMappingService>().InstancePerLifetimeScope();
			builder.RegisterType<InventoryService>().As<IInventoryService>().InstancePerLifetimeScope();
			//builder.RegisterType<DepartmentProfileService>().As<IDepartmentProfileService>().InstancePerLifetimeScope();
			//builder.RegisterType<IncidentService>().As<IIncidentService>().InstancePerLifetimeScope();
			builder.RegisterType<FileService>().As<IFileService>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentLinksService>().As<IDepartmentLinksService>().InstancePerLifetimeScope();
			builder.RegisterType<ResourceOrdersService>().As<IResourceOrdersService>().InstancePerLifetimeScope();
			builder.RegisterType<HealthService>().As<IHealthService>().InstancePerLifetimeScope();
			builder.RegisterType<FirebaseService>().As<IFirebaseService>().InstancePerLifetimeScope();
			builder.RegisterType<TemplatesService>().As<ITemplatesService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtocolsService>().As<IProtocolsService>().InstancePerLifetimeScope();
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
			builder.RegisterType<ClientSessionMetadataParser>().As<IClientSessionMetadataParser>().SingleInstance();
			builder.RegisterType<LocalIpLocationProvider>().As<IIpLocationProvider>().SingleInstance();
			builder.RegisterType<ExternalIdentityLinkService>().As<IExternalIdentityLinkService>().InstancePerLifetimeScope();
			builder.RegisterType<PasswordRecoveryService>().As<IPasswordRecoveryService>().InstancePerLifetimeScope();

			// Advanced Data Protection (ADP)
			builder.RegisterType<DepartmentDataProtectionService>().As<IDepartmentDataProtectionService>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentLockService>().As<IDepartmentLockService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtectedFieldCatalog>().As<IProtectedFieldCatalog>().SingleInstance();
			builder.RegisterType<DepartmentKeyService>().As<IDepartmentKeyService>().InstancePerLifetimeScope();
			builder.RegisterType<ProtectedFieldCryptoService>().As<IProtectedFieldCryptoService>().SingleInstance();
			builder.RegisterType<ProtectedDataGrantService>().As<IProtectedDataGrantService>().SingleInstance();
			builder.RegisterType<DepartmentMemberSensitiveDataService>().As<IDepartmentMemberSensitiveDataService>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentMemberEmergencyContactService>().As<IDepartmentMemberEmergencyContactService>().InstancePerLifetimeScope();
			builder.RegisterType<MemberProfileRelocationService>().As<IMemberProfileRelocationService>().InstancePerLifetimeScope();
			// Attended protected reads + the write safety net. Requires IProtectedDataBrokerClient,
			// so every composition root that loads this module must also load
			// ProtectedDataBrokerClientModule (client only — no key material).
			builder.RegisterType<ProtectedReadService>()
				.As<IProtectedReadService>().As<IProtectedWriteService>().InstancePerLifetimeScope();

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
			builder.RegisterType<WorkShiftsService>().As<IWorkShiftsService>().SingleInstance();
			builder.RegisterType<ContactsService>().As<IContactsService>().SingleInstance();
			builder.RegisterType<IndoorMapService>().As<IIndoorMapService>().SingleInstance();
			builder.RegisterType<CustomMapService>().As<ICustomMapService>().SingleInstance();
			builder.RegisterType<RouteService>().As<IRouteService>().SingleInstance();
			builder.RegisterType<CheckInTimerService>().As<ICheckInTimerService>().InstancePerLifetimeScope();
			builder.RegisterType<RunCardsService>().As<IRunCardsService>().InstancePerLifetimeScope();
			builder.RegisterType<PersonnelLocationResolver>().As<IPersonnelLocationResolver>().InstancePerLifetimeScope();
			builder.RegisterType<DispatchRecommendationService>().As<IDispatchRecommendationService>().InstancePerLifetimeScope();

			// UDF Services
			builder.RegisterType<UserDefinedFieldsService>().As<IUserDefinedFieldsService>().InstancePerLifetimeScope();
			builder.RegisterType<UdfRenderingService>().As<IUdfRenderingService>().InstancePerLifetimeScope();


			// Stripe Services
			//builder.RegisterType<StripeSubscriptionServiceFacade>().As<IStripeSubscriptionServiceFacade>().InstancePerLifetimeScope();
			//builder.RegisterType<StripeInvoiceServiceFacade>().As<IStripeInvoiceServiceFacade>().InstancePerLifetimeScope();
			//builder.RegisterType<StripeChargeServiceFacade>().As<IStripeChargeServiceFacade>().InstancePerLifetimeScope();

			// GDPR Services
			builder.RegisterType<GdprDataExportService>().As<IGdprDataExportService>().InstancePerLifetimeScope();

			// Communication Test Services
			builder.RegisterType<CommunicationTestService>().As<ICommunicationTestService>().InstancePerLifetimeScope();

			// Weather Alert Services
			builder.RegisterType<WeatherAlertService>().As<IWeatherAlertService>().InstancePerLifetimeScope();

			// Records (RMS) Services - RMS plan section 5.11.1
			builder.RegisterType<Records.RecordsCutoverService>().As<IRecordsCutoverService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.DomainEventOutboxService>().As<IDomainEventOutboxService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsAuthorizationService>().As<IRecordsAuthorizationService>().InstancePerLifetimeScope();
			// Value seam (plan 5.9.1): the only caller of the details repository; enrollment hooks in here.
			builder.RegisterType<Records.RmsRecordValueService>().As<IRmsRecordValueService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsService>().As<IRecordsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RmsInventoryUsageAdapter>().As<IRmsInventoryUsageAdapter>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsAccountabilityService>().As<IRecordsAccountabilityService>().InstancePerLifetimeScope();
			// RMS-2: NERIS incident reports and the submission worker logic
			builder.RegisterType<Records.IncidentReportsService>().As<IIncidentReportsService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsSubmissionService>().As<IRecordsSubmissionService>().InstancePerLifetimeScope();
			// RMS-1B v4 Records API support: keyed short-lived state, command idempotency, resumable attachment sessions
			builder.RegisterType<Records.RecordsApiStateStore>().As<IRecordsApiStateStore>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsApiIdempotencyService>().As<IRecordsApiIdempotencyService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordAttachmentUploadService>().As<IRecordAttachmentUploadService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsNotificationService>().As<IRecordsNotificationService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsSearchIndexMaintenanceService>().As<IRecordsSearchIndexMaintenanceService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsReportingService>().As<IRecordsReportingService>().InstancePerLifetimeScope();
			// Default attachment scanner: no engine, rows stay Skipped. A real scanner provider replaces this registration.
			builder.RegisterType<Records.NullRecordAttachmentScanner>().As<Resgrid.Model.Providers.IRecordAttachmentScanner>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentProfileMediaService>().As<IDepartmentProfileMediaService>().InstancePerLifetimeScope();
			builder.RegisterType<Records.RecordsPrintLayoutService>().As<IRecordsPrintLayoutService>().InstancePerLifetimeScope();
		}
	}
}
