using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Resgrid.Config;
using Resgrid.Web.Tts.Configuration;
using Resgrid.Web.Tts.Health;
using Resgrid.Web.Tts.Services;
using System.Reflection;
using System.Text.Json;
using System.Threading.RateLimiting;
using Sentry.Profiling;

var builder = WebApplication.CreateBuilder(args);

ConfigProcessor.LoadAndProcessConfig(builder.Configuration["AppOptions:ConfigPath"]);
ConfigProcessor.LoadAndProcessEnvVariables(builder.Configuration.AsEnumerable());

// Configure Sentry error tracking and performance monitoring
if (!string.IsNullOrWhiteSpace(ExternalErrorConfig.ExternalErrorServiceUrlForTts))
{
	builder.WebHost.UseSentry(options =>
	{
		options.Dsn = ExternalErrorConfig.ExternalErrorServiceUrlForTts;
		options.AttachStacktrace = true;
		options.SendDefaultPii = true;
		options.AutoSessionTracking = true;
		options.TracesSampleRate = ExternalErrorConfig.SentryPerfSampleRate;
		options.Environment = ExternalErrorConfig.Environment;
		options.Release = Assembly.GetEntryAssembly()?.GetName().Version?.ToString();
		options.ProfilesSampleRate = ExternalErrorConfig.SentryProfilingSampleRate;

		// Add profiling integration for performance tracing
		options.AddIntegration(new ProfilingIntegration());

		options.TracesSampler = samplingContext =>
		{
			if (samplingContext?.CustomSamplingContext != null)
			{
				if (samplingContext.CustomSamplingContext.TryGetValue("__HttpPath", out var httpPath))
				{
					var pathValue = httpPath?.ToString();
					if ((pathValue is not null && pathValue.StartsWith("/health", StringComparison.OrdinalIgnoreCase)) ||
					    string.Equals(pathValue, "/livez", StringComparison.OrdinalIgnoreCase) ||
					    string.Equals(pathValue, "/readyz", StringComparison.OrdinalIgnoreCase))
					{
						return 0;
					}
				}
			}

			return ExternalErrorConfig.SentryPerfSampleRate;
		};
	});
}

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddStackExchangeRedisCache(options =>
{
	options.Configuration = CacheConfig.RedisConnectionString;
	options.InstanceName = $"{SystemBehaviorConfig.GetEnvPrefix()}resgrid-tts:";
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddTtsConfiguration();
builder.Services.Configure<ForwardedHeadersOptions>(TtsRequestIdentity.ConfigureForwardedHeaders);
// Shallow (config-presence) check backs /health for the k8s probes; the full check runs
// functional probes against Redis, S3 and the Piper/ffmpeg pipeline and only executes
// when /health/full is called explicitly.
builder.Services.AddSingleton<TtsFullHealthCheck>();
builder.Services.AddHealthChecks()
	.AddCheck<TtsDependencyHealthCheck>("tts_dependencies", tags: new[] { "live" })
	.AddCheck<TtsFullHealthCheck>("tts_full", tags: new[] { "full" });
builder.Services.AddRateLimiter(options =>
{
	options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
	options.AddPolicy("tts", httpContext =>
	{
		var clientId = TtsRequestIdentity.ResolveRateLimitPartitionKey(httpContext);
		var rateLimitOptions = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

		return RateLimitPartition.GetFixedWindowLimiter(
			clientId,
			_ => new FixedWindowRateLimiterOptions
			{
				PermitLimit = rateLimitOptions.PermitLimit,
				QueueLimit = rateLimitOptions.QueueLimit,
				QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
				Window = TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds)
			});
	});
	options.OnRejected = async (context, cancellationToken) =>
	{
		context.HttpContext.Response.ContentType = "application/problem+json";
		await context.HttpContext.Response.WriteAsJsonAsync(
			new ProblemDetails
			{
				Status = StatusCodes.Status429TooManyRequests,
				Title = "Rate limit exceeded",
				Detail = "Too many TTS requests were received. Please retry shortly."
			},
			cancellationToken);
	};
});

builder.Services.AddSingleton<IStorageService, S3StorageService>();
builder.Services.AddSingleton<IPiperWorkerFactory, PiperWorkerFactory>();
builder.Services.AddSingleton<IPiperProcessPool, PiperProcessPool>();
builder.Services.AddSingleton<ITextPreprocessor, TextPreprocessor>();
builder.Services.AddSingleton<ICacheService, CacheService>();
builder.Services.AddSingleton<IAudioProcessingService, AudioProcessingService>();
builder.Services.AddSingleton<ITtsPlaybackUrlService, TtsPlaybackUrlService>();
builder.Services.AddSingleton<ITtsService, TtsService>();
builder.Services.AddHostedService<PromptWarmupHostedService>();
builder.Services.AddHostedService<TempDirectorySweepHostedService>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseForwardedHeaders();
app.UseRateLimiter();

// Deep-probe gate: /health/full runs a real Piper/ffmpeg synthesis plus Redis and
// S3 round-trips, and this service is internet-reachable (Twilio fetches playback
// audio from it). Restricted to trusted monitoring via the shared key
// (SystemBehaviorConfig.FullHealthCheckKey); an unconfigured key fails closed.
// The shallow /health endpoint stays public for the k8s probes.
app.UseWhen(
	context => context.Request.Path.StartsWithSegments("/health/full", StringComparison.OrdinalIgnoreCase),
	healthApp => healthApp.Use(async (context, next) =>
	{
		if (!TtsHealthCheckAccess.IsAuthorized(context.Request))
		{
			context.Response.StatusCode = StatusCodes.Status401Unauthorized;
			return;
		}

		await next();
	}));

// Shallow liveness endpoint: configuration presence only, no external calls. This is
// what the k8s liveness/readiness/startup probes point at.
app.MapHealthChecks("/health", new HealthCheckOptions
{
	Predicate = registration => !registration.Tags.Contains("full"),
	ResponseWriter = WriteHealthResponseAsync
});

// Deep functional probe: Redis round-trip, S3 reachability, and a real Piper/ffmpeg
// synthesis. For monitoring and diagnostics — do not wire probes to this endpoint.
// Requires the X-Resgrid-Health-Key header (gate registered above).
app.MapHealthChecks("/health/full", new HealthCheckOptions
{
	ResponseWriter = WriteHealthResponseAsync
});
app.MapControllers();

static async Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
{
	context.Response.ContentType = "application/json";

	var payload = new
	{
		status = report.Status.ToString(),
		checks = report.Entries.ToDictionary(
			entry => entry.Key,
			entry => new
			{
				status = entry.Value.Status.ToString(),
				description = entry.Value.Description,
				data = entry.Value.Data.Count > 0
					? entry.Value.Data.ToDictionary(item => item.Key, item => item.Value?.ToString())
					: null
			})
	};

	await context.Response.WriteAsync(JsonSerializer.Serialize(payload), context.RequestAborted);
}

app.Run();

public partial class Program;
