# Resgrid Project Guide

## Overview

Resgrid is a logistics and resource management platform for emergency services (fire, EMS, SAR). It's a .NET (C#) monolith solution organized into 30+ projects across 7 areas.

## Solution Structure

```
Resgrid.sln
├── Web/                          # ASP.NET web apps
│   ├── Resgrid.Web/              # Main MVC web application
│   ├── Resgrid.Web.Services/     # REST API (v4 controllers)
│   ├── Resgrid.Web.Eventing/     # Webhook/event endpoint
│   ├── Resgrid.Web.Mcp/          # MCP endpoint
│   └── Resgrid.Web.Tts/          # Text-to-speech
├── Core/                         # Core business logic
│   ├── Resgrid.Config/           # Static config classes (one per domain)
│   ├── Resgrid.Framework/        # Utilities: Logging, Serialization, Hashing
│   ├── Resgrid.Localization/     # Localization strings
│   ├── Resgrid.Model/            # Entities, enums, interfaces (Services, Repositories, Providers)
│   └── Resgrid.Services/         # Service implementations
├── Repositories/                 # Data access
│   ├── Resgrid.Repositories.DataRepository/   # SQL Server / Dapper
│   └── Resgrid.Repositories.NoSqlRepository/  # MongoDB
├── Providers/                    # Infrastructure implementations
│   ├── Resgrid.Providers.Cache/       # Redis caching (AzureRedisCacheProvider)
│   ├── Resgrid.Providers.Bus/         # Azure Service Bus
│   ├── Resgrid.Providers.Bus.Rabbit/  # RabbitMQ alternative
│   ├── Resgrid.Providers.Email/       # Email delivery
│   ├── Resgrid.Providers.Geo/         # Geolocation
│   ├── Resgrid.Providers.Marketing/   # Marketing/CRM
│   ├── Resgrid.Providers.Messaging/   # Push notifications
│   ├── Resgrid.Providers.Migrations/  # SQL Server migrations
│   ├── Resgrid.Providers.MigrationsPg/# PostgreSQL migrations
│   ├── Resgrid.Providers.Number/      # Phone number provisioning
│   ├── Resgrid.Providers.Pdf/         # PDF generation
│   ├── Resgrid.Providers.Voip/        # VoIP/SIP
│   ├── Resgrid.Providers.Weather/     # Weather data
│   ├── Resgrid.Providers.Workflow/    # Workflow execution
│   ├── Resgrid.Providers.Claims/      # Custom auth claims
│   └── Resgrid.Providers.AddressVerification/
├── Workers/                      # Background job processing
│   ├── Resgrid.Workers.Framework/     # Worker logic + Bootstrapper
│   ├── Resgrid.Workers.Console/       # Worker host (console app)
│   └── Support/Quidjibo.Postgres/     # Queue backend for PostgreSQL
├── Tests/                        # Test projects
│   ├── Resgrid.Tests/
│   ├── Resgrid.SmokeTests/
│   └── Resgrid.Intergration.Tests/
└── Tools/
    └── Resgrid.Console/          # Admin CLI tools
```

## Build Configurations

7 solution configurations: `Debug`, `Release`, `Docker`, `Azure`, `Cloud`, `Staging`, plus `x86`/`x64` variants.

Build command: `dotnet build Resgrid.sln`

The `Directory.Build.props` sets OS-conditional intermediate output paths:
- Windows: `obj/windows/`
- Linux/Unix: `obj/unix/`

## Architecture & Conventions

### Layered Architecture

```
Config  →  Model  →  Services  →  Repositories/Providers  →  Web/Workers
```

Each layer depends only on the layer(s) to its left:
- **Config** (`Resgrid.Config`): Static configuration classes, no dependencies
- **Model** (`Resgrid.Model`): Entities, enums, interfaces — no external deps
- **Services** (`Resgrid.Services`): Business logic — depends on Model
- **Repositories** (`Resgrid.Repositories.*`): Data access — depends on Model
- **Providers** (`Resgrid.Providers.*`): External integrations — depends on Model
- **Web/Workers**: Entry points — depend on everything

### Dependency Injection (Autofac + Service Locator)

This codebase uses **Service Locator** pattern, NOT constructor injection:

```csharp
// How services are resolved throughout the codebase:
var service = Bootstrapper.GetKernel().Resolve<ISomeService>();
```

The `Bootstrapper` class (in `Resgrid.Workers.Framework/Bootstrapper.cs`) initializes Autofac with module-based registration:
```csharp
var builder = new ContainerBuilder();
builder.RegisterModule(new DataModule());
builder.RegisterModule(new ServicesModule());
builder.RegisterModule(new CacheProviderModule());
// ... more modules
_container = builder.Build();
```

**When adding new services, you MUST update the Autofac module files** (typically `DataModule.cs` or `ServicesModule.cs`) to register your new type against its interface.

### Configuration System

Configuration is NOT in `appsettings.json`. It uses **static classes with mutable fields** loaded via reflection:

1. Individual static classes in `Core/Resgrid.Config/` — one per domain (e.g., `SystemBehaviorConfig`, `CacheConfig`, `ApiConfig`)
2. All config fields are `public static` (NOT properties with getters/setters)
3. `ConfigProcessor.LoadAndProcessConfig()` uses reflection to find classes in the `Resgrid.Config` namespace and set their static fields
4. Values come from a JSON file (keyed as `"ClassName.FieldName"`) or environment variables (keyed as `RESGRID:ClassName:FieldName`)

**Usage:** `Config.SystemBehaviorConfig.CacheEnabled`, `Config.CacheConfig.RedisConnectionString`

### Caching (Redis Cache-Aside)

All caching goes through `ICacheProvider` — implemented by `AzureRedisCacheProvider`.

**Key method used everywhere:**
```csharp
T Retrieve<T>(string cacheKey, Func<T> fallbackFunction, TimeSpan expiration)
Task<T> RetrieveAsync<T>(string cacheKey, Func<Task<T>> fallbackFunction, TimeSpan expiration)
```

**Cache-Aside Pattern:** Try cache → on miss call fallback → store result → return. Cache keys are environment-prefixed (e.g., `DEV_`, `QA_`, `ST_`) based on `SystemBehaviorConfig.Environment`.

**Common pattern in Services** (local function + cache wrapper):
```csharp
public async Task<Foo> GetFooAsync(int departmentId, bool bypassCache = false)
{
    async Task<Foo> getFoo()
    {
        // ... actual logic ...
        return foo;
    }

    if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
        return await _cacheProvider.RetrieveAsync<Foo>(cacheKey, getFoo, cacheDuration);
    else
        return await getFoo();
}
```

**IMPORTANT:** The `bypassCache` parameter defaults to `false`. Many production callers do NOT bypass cache, so changes may not take effect for up to the cache duration (commonly 14 days for plan limits, 1 day for general data). Call `Invalidate*Cache` methods or set `bypassCache: true` when testing.

### Logging

```csharp
Resgrid.Framework.Logging.LogException(Exception ex, string extraMessage = null, string correlationId = null)
Resgrid.Framework.Logging.LogError(string message)
Resgrid.Framework.Logging.LogInfo(string message)
Resgrid.Framework.Logging.LogDebug(string message)
```

Uses Serilog under the hood with optional Sentry integration. `LogException` automatically captures `[CallerFilePath]`, `[CallerMemberName]`, `[CallerLineNumber]`.

### Naming Conventions

| Layer | Interface | Implementation | Location |
|---|---|---|---|
| Services | `I{Name}Service` | `{Name}Service` | `Core/Resgrid.Services/` |
| Repositories | `I{Name}Repository` | `{Name}Repository` | `Repositories/Resgrid.Repositories.DataRepository/` |
| Providers | `I{Name}Provider` | `{Name}Provider` | `Providers/Resgrid.Providers.{Domain}/` |

Service methods are almost all `async` returning `Task<T>`. Method naming: `{Verb}{Entity}{Filter}Async` (e.g., `GetAllUsersForDepartmentAsync`, `CreateUserState`).

### Worker Pattern

Workers follow a consistent pattern (`Workers/Resgrid.Workers.Framework/Logic/`):
```csharp
public async Task<Tuple<bool, string>> Process({Type}QueueItem item)
{
    try
    {
        // ... process item ...
        return new Tuple<bool, string>(true, "");
    }
    catch (Exception ex)
    {
        Logging.LogException(ex);
        return new Tuple<bool, string>(false, ex.ToString());
    }
}
```

Task type discrimination uses `(int)TaskTypes.SomeEnum`.

## Critical Gotchas & Common Bug Patterns

### 1. Billing API Response Null Safety

**`SubscriptionsService.GetCurrentPlanForDepartmentAsync()`** and **`GetPlanCountsForDepartmentAsync()`** call the external Billing API. Both check `response.Data == null` but the inner `response.Data.Data` can still be null when the API succeeds with an empty payload. Always null-check results from these methods.

### 2. Null Plan from GetCurrentPlanForDepartmentAsync

When Billing API is configured but returns a response where `Data.Data` is null, `GetCurrentPlanForDepartmentAsync` returns null instead of the free plan fallback. Callers that access `plan.PlanId` or `plan.GetLimitForTypeAsInt()` will NRE.

### 3. Service Locator in Constructors

Unlike modern DI, this codebase resolves dependencies explicitly in constructors via `Bootstrapper.GetKernel().Resolve<T>()`. When examining stack traces, dependencies are never null due to constructor injection failures — the Bootstrapper would fail at app start. If a NullReferenceException occurs on a service call, the issue is typically in the return value of the called method, not the service reference itself.

### 4. Async State Machine Line Numbers

PDB line numbers in async stack traces can be off by 1-2 lines from the actual source. An NRE reported at the `await` line often actually occurs on the next line where the awaited result is used.

### 5. Cache Duration

Plan limits are cached for **14 days** (`TimeSpan.FromDays(14)`). Most user/department data is cached for **1 day**. Use `bypassCache: true` or call invalidation methods when you need fresh data.

## Key File Index

| Purpose | File |
|---|---|
| Solution file | `Resgrid.sln` |
| Build props | `Directory.Build.props` |
| DI Bootstrapper | `Workers/Resgrid.Workers.Framework/Bootstrapper.cs` |
| Logging | `Core/Resgrid.Framework/Logging.cs` |
| Config processor | `Core/Resgrid.Config/ConfigProcessor.cs` |
| System behavior config | `Core/Resgrid.Config/SystemBehaviorConfig.cs` |
| Cache config | `Core/Resgrid.Config/CacheConfig.cs` |
| Redis cache provider | `Providers/Resgrid.Providers.Cache/AzureRedisCacheProvider.cs` |
| Cache interface | `Core/Resgrid.Model/Providers/ICacheProvider.cs` |
| Subscriptions (billing) | `Core/Resgrid.Services/SubscriptionsService.cs` |
| Limits service | `Core/Resgrid.Services/LimitsService.cs` |
| Departments service | `Core/Resgrid.Services/DepartmentsService.cs` |
| Service interfaces | `Core/Resgrid.Model/Services/` (83 interfaces) |
| Billing API DTOs | `Core/Resgrid.Model/Billing/Api/` |
| Worker logic | `Workers/Resgrid.Workers.Framework/Logic/` |
| Worker queue items | `Core/Resgrid.Model/Queue/` |

## Common Tasks

**Build the entire solution:**
```bash
dotnet build Resgrid.sln
```

**Build a specific project:**
```bash
dotnet build Core/Resgrid.Services/Resgrid.Services.csproj
```

**Find all implementations of an interface:**
```bash
grep -r "I{Name}Service" --include="*.cs"
```

## Agent Coding

Always use the hindsight recall tool before answering coding questions.
Always retain important project information using hindsight retain.