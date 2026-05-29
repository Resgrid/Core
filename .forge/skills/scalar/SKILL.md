---
name: scalar
description: >
  Scalar API documentation UI for .NET 10 applications. Covers setup, themes,
  authentication prefill, multiple documents, layout options, and security.
  A modern replacement for Swagger UI.
  Load this skill when setting up API documentation UI, or when the user mentions
  "Scalar", "MapScalarApiReference", "API reference", "Swagger UI replacement",
  "API documentation UI", "Scalar theme", "interactive API docs", or "Try It".
---

# Scalar

## Core Principles

1. **Scalar replaces Swagger UI** — Scalar is the recommended API documentation UI for .NET 10. Faster rendering, built-in dark mode, code generation for dozens of languages, and full OpenAPI 3.1 support.
2. **Development only by default** — Wrap `MapScalarApiReference()` in an `IsDevelopment()` check. API documentation exposes internal structure. If needed in production, add authorization.
3. **Disable the proxy for sensitive APIs** — Scalar's "Try It" feature routes through `proxy.scalar.com` by default. Disable it with `.WithProxy(null)` to keep auth headers local.
4. **Security schemes come from OpenAPI** — Scalar reads security schemes from the OpenAPI document. Configure them via document transformers, not in Scalar directly.

## Patterns

### Basic Setup

```csharp
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();  // UI at /scalar/v1
}

app.Run();
```

### Customized Configuration

```csharp
app.MapScalarApiReference(options =>
{
    options
        .WithTitle("Checkout API")
        .WithTheme(ScalarTheme.Mars)
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
        .WithPreferredScheme("Bearer")
        .WithProxy(null)  // Disable external proxy
        .WithSidebar(true);
});
```

### Authentication Prefill (Development Only)

Pre-fill credentials so developers don't have to paste tokens manually. The OpenAPI document must already include the security scheme via a document transformer.

```csharp
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference(options =>
    {
        options
            .WithPreferredScheme("Bearer")
            .AddHttpAuthentication("Bearer", auth =>
            {
                auth.Token = "dev-only-test-token";
            });
    });
}
```

Other auth types:

```csharp
// API Key
options.WithApiKeyAuthentication(apiKey =>
{
    apiKey.Token = "dev-api-key";
});

// OAuth2
options.WithOAuth2Authentication(oauth =>
{
    oauth.ClientId = "your-client-id";
    oauth.Scopes = ["openid", "profile"];
});
```

### Available Themes

```csharp
// ScalarTheme options: Default, Moon, Purple, BluePlanet, Saturn, Mars, DeepSpace, Kepler, Solarized, Laserwave
options.WithTheme(ScalarTheme.Mars);
```

### Multiple API Documents

```csharp
// Register multiple OpenAPI documents
builder.Services.AddOpenApi("v1");
builder.Services.AddOpenApi("v2-beta");

// Scalar picks them up automatically
app.MapOpenApi();
app.MapScalarApiReference();
// Available at /scalar/v1 and /scalar/v2-beta
```

Or configure documents explicitly:

```csharp
app.MapScalarApiReference(options =>
{
    options
        .AddDocument("v1", "Production API")
        .AddDocument("v2-beta", "Beta API", isDefault: true);
});
```

### Custom Route Prefix

```csharp
// Default is /scalar/{documentName}
app.MapScalarApiReference("/api-docs");
// Now at /api-docs/v1
```

### Production with Authorization

```csharp
// When partners need access to docs in production
app.MapOpenApi().RequireAuthorization("ApiDocs");
app.MapScalarApiReference().RequireAuthorization("ApiDocs");
```

### Force Dark Mode

```csharp
options.ForceDarkMode();
```

### Classic Layout (Swagger-like)

```csharp
options.WithClassicLayout();
```

## Anti-patterns

### Don't Expose Scalar in Production Without Auth

```csharp
// BAD — anyone can see your API structure
app.MapOpenApi();
app.MapScalarApiReference();

// GOOD — development only
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// GOOD — production with auth
app.MapOpenApi().RequireAuthorization("ApiDocs");
app.MapScalarApiReference().RequireAuthorization("ApiDocs");
```

### Don't Pre-fill Real Credentials

```csharp
// BAD — real tokens visible in browser
options.AddHttpAuthentication("Bearer", auth =>
{
    auth.Token = "eyJhbG...real-production-token";
});

// GOOD — dev-only test tokens
if (app.Environment.IsDevelopment())
{
    options.AddHttpAuthentication("Bearer", auth =>
    {
        auth.Token = "dev-only-test-token";
    });
}
```

### Don't Forget the Security Scheme Transformer

```csharp
// BAD — no auth UI in Scalar because OpenAPI doc has no security schemes
builder.Services.AddOpenApi();
app.MapScalarApiReference(options =>
{
    options.WithPreferredScheme("Bearer"); // Does nothing!
});

// GOOD — register the document transformer first
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
});
app.MapScalarApiReference(options =>
{
    options.WithPreferredScheme("Bearer");
});
```

### Don't Leave the Proxy Enabled for Sensitive APIs

```csharp
// BAD — auth headers flow through proxy.scalar.com
app.MapScalarApiReference();

// GOOD — disable proxy for APIs with sensitive data
app.MapScalarApiReference(options =>
{
    options.WithProxy(null);
});
```

### Don't Use Swagger UI for New .NET 10 Projects

```csharp
// BAD — Swashbuckle removed from templates, maintenance concerns
builder.Services.AddSwaggerGen();
app.UseSwaggerUI();

// GOOD — built-in OpenAPI + Scalar
builder.Services.AddOpenApi();
app.MapOpenApi();
app.MapScalarApiReference();
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| API documentation UI | `MapScalarApiReference()` with `MapOpenApi()` |
| Development environment | Default setup with `IsDevelopment()` guard |
| Production API docs | Add `.RequireAuthorization()` to both endpoints |
| Auth testing in dev | `AddHttpAuthentication()` with test tokens |
| Dark theme preference | `.ForceDarkMode()` or `.WithTheme(ScalarTheme.Moon)` |
| Multiple API versions | Multiple `AddOpenApi()` calls — Scalar detects automatically |
| Sensitive APIs | `.WithProxy(null)` to disable external proxy |
| Swagger-like layout | `.WithClassicLayout()` |
| Custom route | `app.MapScalarApiReference("/api-docs")` |
