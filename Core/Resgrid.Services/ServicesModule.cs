using Autofac;
//using Resgrid.Model.Facades.Stripe;
using Resgrid.Model.Services;
using Resgrid.Services.CallEmailTemplates;

namespace Resgrid.Services
{
	public class ServicesModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<LogService>().As<ILogService>().InstancePerLifetimeScope();
			builder.RegisterType<QueueService>().As<IQueueService>().InstancePerLifetimeScope();
			builder.RegisterType<DeleteService>().As<IDeleteService>().InstancePerLifetimeScope();
			builder.RegisterType<CommunicationService>().As<ICommunicationService>().InstancePerLifetimeScope();
			builder.RegisterType<SmsService>().As<ISmsService>().InstancePerLifetimeScope();
			builder.RegisterType<PushLogsService>().As<IPushLogsService>().InstancePerLifetimeScope();
			builder.RegisterType<PushService>().As<IPushService>().SingleInstance();
			builder.RegisterType<MessageService>().As<IMessageService>().InstancePerLifetimeScope();
			builder.RegisterType<AddressService>().As<IAddressService>().InstancePerLifetimeScope();
			builder.RegisterType<UserStateService>().As<IUserStateService>().InstancePerLifetimeScope();
			builder.RegisterType<DepartmentsService>().As<IDepartmentsService>().InstancePerLifetimeScope();
			builder.RegisterType<UsersService>().As<IUsersService>().InstancePerLifetimeScope();
			builder.RegisterType<ActionLogsService>().As<IActionLogsService>().InstancePerLifetimeScope();
			builder.RegisterType<EmailService>().As<IEmailService>().InstancePerLifetimeScope();
			builder.RegisterType<CallsService>().As<ICallsService>().InstancePerLifetimeScope();
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
			builder.RegisterType<DepartmentSettingsService>().As<IDepartmentSettingsService>().InstancePerLifetimeScope();
			builder.RegisterType<PersonnelRolesService>().As<IPersonnelRolesService>().InstancePerLifetimeScope();
			builder.RegisterType<ScheduledTasksService>().As<IScheduledTasksService>().InstancePerLifetimeScope();
			builder.RegisterType<DistributionListsService>().As<IDistributionListsService>().InstancePerLifetimeScope();
			builder.RegisterType<DocumentsService>().As<IDocumentsService>().InstancePerLifetimeScope();
			//builder.RegisterType<PaymentProviderService>().As<IPaymentProviderService>().InstancePerLifetimeScope();
			builder.RegisterType<CalendarService>().As<ICalendarService>().InstancePerLifetimeScope();
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
			builder.RegisterType<AutofillsService>().As<IAutofillsService>().InstancePerLifetimeScope();
			builder.RegisterType<UnitStatesService>().As<IUnitStatesService>().InstancePerLifetimeScope();

			//builder.RegisterType<InternalCacheService>().As<IInternalCacheService>().SingleInstance();
			builder.RegisterType<CoreEventService>().As<ICoreEventService>().SingleInstance();
			builder.RegisterType<WorkShiftsService>().As<IWorkShiftsService>().SingleInstance();
			builder.RegisterType<ContactsService>().As<IContactsService>().SingleInstance();


			// Stripe Services
			//builder.RegisterType<StripeSubscriptionServiceFacade>().As<IStripeSubscriptionServiceFacade>().InstancePerLifetimeScope();
			//builder.RegisterType<StripeInvoiceServiceFacade>().As<IStripeInvoiceServiceFacade>().InstancePerLifetimeScope();
			//builder.RegisterType<StripeChargeServiceFacade>().As<IStripeChargeServiceFacade>().InstancePerLifetimeScope();
		}
	}
}
