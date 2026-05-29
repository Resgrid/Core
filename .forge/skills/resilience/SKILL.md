---
name: resilience
description: >
  Resilience patterns for .NET 10 applications using Polly v8.
  Covers retry, circuit breaker, timeout, fallback, rate limiter, hedging,
  and composing resilience pipelines.
  Load this skill when implementing retry logic, circuit breakers, handling
  transient failures, or when the user mentions "Polly", "resilience",
  "retry", "circuit breaker", "timeout", "fallback", "rate limit",
  "hedging", "transient fault", "HttpClient resilience", or "resilience pipeline".
---

# Resilience

## Core Principles

1. **Polly v8 resilience pipelines, not v7 policies** — Polly v8 replaced `Policy` with `ResiliencePipeline`. Never use `PolicyBuilder`, `Policy.Handle<>()`, or `ISyncPolicy`. The new API is composable, type-safe, and integrates natively with `IHttpClientFactory`.
2. **Configure via `AddResilienceHandler`, not manual wrapping** — For HTTP calls, use `Microsoft.Extensions.Http.Resilience` which adds pipelines directly to `HttpClient` via DI. No manual `ExecuteAsync` wrapping.
3. **Compose strategies, don't nest them** — A single `ResiliencePipeline` can chain retry + circuit breaker + timeout. Strategies execute outer-to-inner (first added = outermost). No need for nested try/catch or manual orchestration.
4. **Always set timeouts** — Every external call needs a timeout. Use Polly's `AddTimeout()` as the innermost strategy so it applies per-attempt, and optionally an outer timeout for total elapsed time.
5. **Instrument everything** — Polly v8 emits `Metering` events and supports `TelemetryOptions` for OpenTelemetry. Use them to monitor retry rates, circuit breaker state, and timeout frequency.

## Patterns

### HTTP Client Resilience (Recommended Default)

```csharp
// Program.cs — Standard resilience handler covers 90% of use cases
builder.Services.AddHttpClient<IPaymentGateway, PaymentGatewayClient>(client =>
{
    client.BaseAddress = new Uri("https://api.payments.example.com");
})
.AddStandardResilienceHandler(); // Retry + circuit breaker + timeout out of the box

// That's it. The standard handler configures:
// - Retry: 3 attempts, exponential backoff, jitter
// - Circuit breaker: 10% failure ratio over 30s sampling, 30s break
// - Attempt timeout: 10s per attempt
// - Total request timeout: 30s
```

**Why**: `AddStandardResilienceHandler()` from `Microsoft.Extensions.Http.Resilience` applies production-ready defaults. Override only when you need different thresholds.

### Custom HTTP Resilience Configuration

```csharp
builder.Services.AddHttpClient<ICatalogService, CatalogServiceClient>(client =>
{
    client.BaseAddress = new Uri("https://api.catalog.example.com");
})
.AddResilienceHandler("catalog", builder =>
{
    // Total timeout — outermost, caps total elapsed time
    builder.AddTimeout(TimeSpan.FromSeconds(15));

    // Retry — exponential backoff with jitter
    builder.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        Delay = TimeSpan.FromMilliseconds(500),
        ShouldHandle = static args => ValueTask.FromResult(
            args.Outcome.Result?.StatusCode is HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.ServiceUnavailable
                || args.Outcome.Exception is HttpRequestException)
    });

    // Circuit breaker — prevent cascading failures
    builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        SamplingDuration = TimeSpan.FromSeconds(10),
        MinimumThroughput = 10,
        BreakDuration = TimeSpan.FromSeconds(30)
    });

    // Per-attempt timeout — innermost
    builder.AddTimeout(TimeSpan.FromSeconds(5));
});
```

**Why**: Named resilience handlers let you tune per-service. The order matters: total timeout > retry > circuit breaker > attempt timeout.

### Non-HTTP Resilience Pipeline

```csharp
// For database calls, message queues, or any non-HTTP operation
builder.Services.AddResiliencePipeline("database", builder =>
{
    builder
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(200),
            ShouldHandle = new PredicateBuilder()
                .Handle<TimeoutException>()
                .Handle<InvalidOperationException>(ex =>
                    ex.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase))
        })
        .AddTimeout(TimeSpan.FromSeconds(10));
});

// Inject and use
public sealed class OrderRepository(
    AppDbContext db,
    [FromKeyedServices("database")] ResiliencePipeline pipeline)
{
    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await pipeline.ExecuteAsync(
            async token => await db.Orders.FindAsync([id], token),
            ct);
    }
}
```

**Why**: `AddResiliencePipeline` registers a named pipeline in DI. Inject with `[FromKeyedServices]` for clean, testable code.

### Typed Resilience Pipeline

```csharp
// When the operation returns a specific type, use ResiliencePipeline<T>
builder.Services.AddResiliencePipeline<string, HttpResponseMessage>("external-api", builder =>
{
    builder
        .AddFallback(new FallbackStrategyOptions<HttpResponseMessage>
        {
            FallbackAction = static args =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"degraded\",\"data\":[]}")
                };
                return Outcome.FromResultAsValueTask(response);
            },
            ShouldHandle = static args => ValueTask.FromResult(
                args.Outcome.Exception is not null
                || args.Outcome.Result?.IsSuccessStatusCode == false)
        })
        .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = 2,
            Delay = TimeSpan.FromMilliseconds(500)
        })
        .AddTimeout(TimeSpan.FromSeconds(5));
});
```

**Why**: Typed pipelines let you add fallback strategies that return a default value when all retries are exhausted — critical for graceful degradation.

### Hedging (Parallel Requests)

```csharp
builder.Services.AddHttpClient<ISearchService, SearchServiceClient>()
    .AddResilienceHandler("search-hedging", builder =>
    {
        builder.AddHedging(new HttpHedgingStrategyOptions
        {
            MaxHedgedAttempts = 2,
            Delay = TimeSpan.FromMilliseconds(500) // Send parallel request after 500ms
        });
        builder.AddTimeout(TimeSpan.FromSeconds(3));
    });
```

**Why**: Hedging sends a parallel request if the first hasn't responded within the delay. Use for latency-sensitive reads where you can tolerate duplicate work.

### Telemetry Integration

```csharp
builder.Services.AddResiliencePipeline("monitored", (builder, context) =>
{
    builder
        .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions())
        .AddTimeout(TimeSpan.FromSeconds(10));

    // Polly v8 emits metrics via System.Diagnostics.Metrics automatically
    // Configure enrichment for better dashboards
    builder.TelemetryListener = new TelemetryOptions
    {
        LoggerFactory = context.ServiceProvider.GetRequiredService<ILoggerFactory>()
    }.TelemetryListener;
});

// In Program.cs — wire up OpenTelemetry to capture Polly metrics
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter("Polly"));
```

### Rate Limiting (.NET Built-in)

.NET provides built-in rate limiting middleware via `AddRateLimiter()` — no external packages needed. Algorithms: `AddFixedWindowLimiter`, `AddSlidingWindowLimiter`, `AddTokenBucketLimiter`, `AddConcurrencyLimiter`.

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("fixed", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromSeconds(60);
        opt.QueueLimit = 0;
    });

    // Always return ProblemDetails with Retry-After on 429
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString();
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ProblemDetails { Title = "Too many requests", Status = 429 }, ct);
    };
});

app.UseRateLimiter();
app.MapGet("/api/orders", ListOrders).RequireRateLimiting("fixed");
```

## Anti-patterns

### BAD: Using Polly v7 API

```csharp
// BAD — v7 policy syntax, do not use
var retryPolicy = Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

var response = await retryPolicy.ExecuteAsync(() => httpClient.GetAsync("/api/data"));
```

### GOOD: Polly v8 Resilience Pipeline

```csharp
// GOOD — v8 pipeline via DI
builder.Services.AddHttpClient<IDataService, DataServiceClient>()
    .AddStandardResilienceHandler();
```

---

### BAD: Wrapping Every Call Manually

```csharp
// BAD — manual resilience per call site
public async Task<Order> GetOrderAsync(Guid id)
{
    try
    {
        return await _pipeline.ExecuteAsync(async ct =>
            await _httpClient.GetFromJsonAsync<Order>($"/orders/{id}", ct));
    }
    catch (TimeoutRejectedException)
    {
        return Order.Empty;
    }
    catch (BrokenCircuitException)
    {
        return Order.Empty;
    }
}
```

### GOOD: Pipeline Handles Everything via HttpClient DI

```csharp
// GOOD — resilience is configured at the HttpClient level
public async Task<Order?> GetOrderAsync(Guid id, CancellationToken ct)
{
    var response = await _httpClient.GetAsync($"/orders/{id}", ct);
    if (!response.IsSuccessStatusCode) return null;
    return await response.Content.ReadFromJsonAsync<Order>(ct);
}
```

---

### BAD: Retry on Non-Idempotent Operations

```csharp
// BAD — retrying a POST that creates a resource risks duplicates
builder.AddRetry(new RetryStrategyOptions
{
    MaxRetryAttempts = 5 // This will create 5 orders on transient failures!
});
```

### GOOD: Retry Only Idempotent Operations or Use Idempotency Keys

```csharp
// GOOD — use idempotency key header for non-idempotent operations
builder.AddRetry(new HttpRetryStrategyOptions
{
    MaxRetryAttempts = 3,
    ShouldHandle = static args => ValueTask.FromResult(
        args.Outcome.Result?.StatusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.ServiceUnavailable)
});

// Pair with idempotency key in the request
httpClient.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
```

---

### BAD: Circuit Breaker Without Monitoring

```csharp
// BAD — circuit breaker with no visibility into state changes
builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions());
// How do you know when it trips? You don't.
```

### GOOD: Circuit Breaker with Telemetry

```csharp
// GOOD — Polly v8 metrics captured via OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter("Polly"));

// Dashboard alerts on: polly.circuit_breaker.state = Open
```

## Decision Guide

| Scenario | Strategy | Configuration |
|----------|----------|---------------|
| HTTP calls to external APIs | `AddStandardResilienceHandler()` | Use defaults, override only specific thresholds |
| HTTP with custom thresholds | `AddResilienceHandler("name", ...)` | Named handler with per-service tuning |
| Database / EF Core calls | `AddResiliencePipeline("db", ...)` | Retry on deadlock/timeout, no circuit breaker |
| Message queue publishing | `AddResiliencePipeline("mq", ...)` | Retry with exponential backoff, timeout |
| Latency-sensitive reads | `AddHedging(...)` | Parallel request after delay threshold |
| Graceful degradation | `AddFallback(...)` | Return cached/default value on total failure |
| Per-attempt time limit | `AddTimeout(...)` innermost | 2-10s depending on operation |
| Total operation time limit | `AddTimeout(...)` outermost | Sum of all retries + buffer |
| Non-idempotent writes | Retry with idempotency key | Or no retry — fail fast |
| Read-heavy microservice | Standard handler + hedging | Low latency with redundancy |
| API rate limiting | `AddRateLimiter()` + `RequireRateLimiting()` | Fixed, sliding, or token bucket per endpoint |

