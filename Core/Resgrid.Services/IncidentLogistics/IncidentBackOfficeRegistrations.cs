using System;
using Autofac;
using Resgrid.Config;
using Resgrid.Model.Services;
using RestSharp;
using RestSharp.Serializers.NewtonsoftJson;

namespace Resgrid.Services.IncidentLogistics
{
	/// <summary>
	/// Autofac registrations of the Incident Back Office (large-incident-logistics-finance-plan.md rev 4.1), called from
	/// ServicesModule so the module's services, rules and lifecycles are registered in one reviewed place. Everything is
	/// constructor-injected; the ledger service is an open generic and each table's rules and lifecycle are collected by
	/// IEnumerable injection.
	/// </summary>
	public static class IncidentBackOfficeRegistrations
	{
		public static void Register(ContainerBuilder builder)
		{
			// D6 entitlement and checkout proxy (cloned from Business Ops).
			builder.Register(_ => new RestClient(new RestClientOptions(SystemBehaviorConfig.BillingApiBaseUrl) { Timeout = TimeSpan.FromSeconds(10) },
				configureSerialization: serializer => serializer.UseNewtonsoftJson())).Named<RestClient>("incident-back-office-billing-client").SingleInstance();
			builder.RegisterType<IncidentBackOfficeBillingService>().As<IIncidentBackOfficeBillingService>()
				.WithParameter((parameter, _) => parameter.ParameterType == typeof(Func<RestClient>), (_, context) =>
				{
					var scope = context.Resolve<ILifetimeScope>();
					return (Func<RestClient>)(() => scope.ResolveNamed<RestClient>("incident-back-office-billing-client"));
				}).InstancePerLifetimeScope();
			builder.RegisterType<IncidentBackOfficeAccessService>().As<IIncidentBackOfficeAccessService>().InstancePerLifetimeScope();

			// Foundation.
			builder.RegisterType<IncidentAuditService>().As<IIncidentAuditService>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentBackOfficeSettingsService>().As<IIncidentBackOfficeSettingsService>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentEventPublisher>().As<IIncidentEventPublisher>().InstancePerLifetimeScope();
			builder.RegisterGeneric(typeof(IncidentLedgerService<>)).As(typeof(IIncidentLedgerService<>)).InstancePerLifetimeScope();
			builder.RegisterType<IncidentLookupService>().As<IIncidentLookupService>().InstancePerLifetimeScope();

			IncidentLifecycles.Register(builder);
			RegisterDomain(builder);
		}

		private static void RegisterDomain(ContainerBuilder builder)
		{
			// LI-A Foundation (plan §13.1).
			builder.RegisterType<IncidentActivationService>().As<IIncidentActivationService>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentAuthorityService>().As<IIncidentAuthorityService>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentPlanningService>().As<IIncidentPlanningService>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentOrganizationService>().As<IIncidentOrganizationService>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentParticipantsService>().As<IIncidentParticipantsService>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentNotificationRoutingService>().As<IIncidentNotificationRoutingService>().InstancePerLifetimeScope();

			// Desks, snapshots and the read seams other modules use (plan §9.17, §12.3, §13.4, §10A.3).
			builder.RegisterType<IncidentAlertLatch>().AsSelf().InstancePerLifetimeScope();
			builder.RegisterType<IncidentOperationalIntelligenceService>().As<IIncidentOperationalIntelligenceService>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentRequestLinkReader>().As<IIncidentRequestLinkReader>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentProjectionService>().As<IIncidentProjectionService>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentAutomationService>().As<IIncidentAutomationService>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentSelfServiceService>().As<IIncidentSelfServiceService>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentBackOfficeGdprExport>().As<IIncidentBackOfficeGdprExport>().InstancePerLifetimeScope();

			// LI-B / LI-C / LI-D table rules: invariants, derived rows and the write-path workflow triggers.
			IncidentDomainRules.Register(builder);
			IncidentTriggerRules.Register(builder);
		}
	}
}
