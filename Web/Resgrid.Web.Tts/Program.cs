using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Resgrid.Config;
using Resgrid.Web.Tts.Configuration;
using Resgrid.Web.Tts.Health;
using Resgrid.Web.Tts.Services;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

ConfigProcessor.LoadAndProcessConfig(builder.Configuration["AppOptions:ConfigPath"]);
ConfigProcessor.LoadAndProcessEnvVariables(builder.Configuration.AsEnumerable());

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
builder.Services.AddTtsConfiguration();
builder.Services.Configure<ForwardedHeadersOptions>(TtsRequestIdentity.ConfigureForwardedHeaders);
builder.Services.AddHealthChecks()
	.AddCheck<TtsDependencyHealthCheck>("tts_dependencies");
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

builder.Services.AddSingleton<IAmazonS3>(sp =>
{
	var options = sp.GetRequiredService<IOptions<S3StorageOptions>>().Value;
	return CreateS3Client(options);
});
builder.Services.AddSingleton<IStorageService, S3StorageService>();
builder.Services.AddSingleton<ICacheService, CacheService>();
builder.Services.AddSingleton<IAudioProcessingService, AudioProcessingService>();
builder.Services.AddSingleton<ITtsPlaybackUrlService, TtsPlaybackUrlService>();
builder.Services.AddSingleton<ITtsService, TtsService>();
builder.Services.AddHostedService<PromptWarmupHostedService>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseForwardedHeaders();
app.UseRateLimiter();

app.MapHealthChecks("/health", new HealthCheckOptions
{
	ResponseWriter = static async (context, report) =>
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
					description = entry.Value.Description
				})
		};

		await context.Response.WriteAsync(JsonSerializer.Serialize(payload), context.RequestAborted);
	}
});
app.MapControllers();

app.Run();

static AmazonS3Client CreateS3Client(S3StorageOptions options)
{
	var credentials = new BasicAWSCredentials(options.AccessKey, options.SecretKey);
	var config = new AmazonS3Config
	{
		ForcePathStyle = options.ForcePathStyle,
		AuthenticationRegion = options.Region
	};

	if (!string.IsNullOrWhiteSpace(options.Endpoint))
	{
		if (Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpointUri))
		{
			config.ServiceURL = endpointUri.GetLeftPart(UriPartial.Authority);
			config.UseHttp = endpointUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
		}
		else
		{
			config.ServiceURL = $"{(options.UseSsl ? Uri.UriSchemeHttps : Uri.UriSchemeHttp)}://{options.Endpoint}";
			config.UseHttp = !options.UseSsl;
		}
	}
	else
	{
		config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
	}

	return new AmazonS3Client(credentials, config);
}

public partial class Program;
