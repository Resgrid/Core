using System.Linq;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Config;
using Resgrid.Providers.Tracking.Protocols;
using Resgrid.TrackerGateway;
using Resgrid.TrackerGateway.Health;
using Resgrid.TrackerGateway.Hosting;
using Resgrid.TrackerGateway.Listeners;
using Resgrid.TrackerGateway.Sessions;

var builder = WebApplication.CreateBuilder(args);

ConfigProcessor.LoadAndProcessConfig(
	builder.Configuration["AppOptions:ConfigPath"]);
ConfigProcessor.LoadAndProcessEnvVariables(
	builder.Configuration.AsEnumerable());

var gatewaySettings = TrackingGatewaySettings.FromCurrentConfig();

builder.Host.UseServiceProviderFactory(
	new AutofacServiceProviderFactory());
builder.Host.ConfigureContainer<ContainerBuilder>(
	(_, containerBuilder) => Bootstrapper.ConfigureContainer(containerBuilder));
builder.WebHost.ConfigureKestrel(
	options => options.ListenAnyIP(gatewaySettings.InternalHealthPort));

builder.Services.AddSingleton(gatewaySettings);
builder.Services.AddSingleton<TrackingListenerPlanBuilder>();
builder.Services.AddSingleton<TrackingConnectionAdmission>();
builder.Services.AddSingleton<TrackingSessionGenerationRegistry>();
builder.Services.AddSingleton<TrackingGatewayMetrics>();
builder.Services.AddSingleton<ITrackingTransportSessionHandler,
	TrackingTransportSessionHandler>();
builder.Services.AddSingleton<ITrackingListenerFactory,
	TrackingSocketListenerFactory>();
builder.Services.AddSingleton<TrackingGatewayReadinessState>();
builder.Services.AddSingleton(
	serviceProvider =>
	{
		var moduleRegistry = serviceProvider
			.GetRequiredService<ITrackingProtocolModuleRegistry>();
		TrackingProtocolCatalogValidator.Validate(
			moduleRegistry);
		return serviceProvider
			.GetRequiredService<TrackingListenerPlanBuilder>()
			.Build(
				serviceProvider.GetRequiredService<TrackingGatewaySettings>(),
				moduleRegistry,
				serviceProvider.GetRequiredService<ITrackingListenerFactory>());
	});
builder.Services.AddHostedService<TrackingListenerSupervisor>();
builder.Services.AddHealthChecks()
	.AddCheck<TrackingGatewayLivenessHealthCheck>(
		"tracker_gateway_live",
		tags: new[] { "live" })
	.AddCheck<TrackingGatewayReadinessHealthCheck>(
		"tracker_gateway_ready",
		tags: new[] { "ready" });

var app = builder.Build();

var listenerPlan = app.Services.GetRequiredService<TrackingListenerPlan>();
app.Services.GetRequiredService<TrackingGatewayReadinessState>()
	.Initialize(listenerPlan);

app.MapHealthChecks(
	"/health/live",
	new HealthCheckOptions
	{
		Predicate = registration => registration.Tags.Contains("live")
	});
app.MapHealthChecks(
	"/health/ready",
	new HealthCheckOptions
	{
		Predicate = registration => registration.Tags.Contains("ready")
	});
app.MapGet(
	"/metrics",
	(TrackingGatewayReadinessState readiness,
		TrackingGatewayMetrics metrics,
		TrackingGatewaySettings settings) => Results.Text(
		TrackingGatewayMetricsWriter.Write(
			readiness.GetSnapshot(),
			metrics,
			settings.MaxConnections),
		"text/plain; version=0.0.4"));

app.Run();

public partial class Program;
