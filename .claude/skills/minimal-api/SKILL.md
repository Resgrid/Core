---
name: minimal-api
description: >
  .NET 10 minimal APIs — the default for building HTTP endpoints. Covers MapGroup,
  endpoint filters, TypedResults, OpenAPI metadata, parameter binding, and route
  conventions.
  Load this skill when creating API endpoints, configuring routing, setting up
  OpenAPI documentation, or when the user mentions "endpoint", "MapGet", "MapPost",
  "MapGroup", "TypedResults", "route", "minimal API", "OpenAPI", "swagger",
  "rate limiting", or "output caching".
---

# Minimal APIs (.NET 10)

## Core Principles

1. **Minimal APIs are the default** — Use controllers only when migrating legacy code. Minimal APIs are lighter, faster, and compose well with any architecture style.
2. **Group endpoints with `MapGroup`** — Never scatter individual `MapGet`/`MapPost` calls in `Program.cs`. Group related endpoints together.
3. **Use `TypedResults` for OpenAPI** — `TypedResults.Ok(value)` gives you compile-time type safety AND correct OpenAPI documentation. `Results.Ok(value)` does not.
4. **Metadata over comments** — Use `.WithName()`, `.WithTags()`, `.WithSummary()` to document endpoints. The metadata feeds into OpenAPI specs.

## Patterns

### Endpoint Group Auto-Discovery (Required Pattern)

Every endpoint group lives in its own file and implements `IEndpointGroup`. A single `app.MapEndpoints()` call in `Program.cs` discovers and registers all groups automatically. **Program.cs never changes when you add new endpoint groups.**

```csharp
// Extensions/IEndpointGroup.cs
public interface IEndpointGroup
{
    void Map(IEndpointRouteBuilder app);
}
```

```csharp
// Extensions/EndpointExtensions.cs
public static class EndpointExtensions
{
    public static WebApplication MapEndpoints(this WebApplication app)
    {
        var groups = typeof(Program).Assembly
            .GetTypes()
            .Where(t => t.IsAssignableTo(typeof(IEndpointGroup)) && !t.IsInterface && !t.IsAbstract)
            .Select(Activator.CreateInstance)
            .Cast<IEndpointGroup>();

        foreach (var group in groups)
            group.Map(app);

        return app;
    }
}
```

```csharp
// Program.cs — this NEVER changes when adding endpoints
var app = builder.Build();
app.MapEndpoints();
app.Run();
```

```csharp
// Features/Orders/OrderEndpoints.cs — one file per endpoint group
public sealed class OrderEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders").WithTags("Orders");

        group.MapPost("/", CreateOrder)
            .WithName("CreateOrder")
            .WithSummary("Create a new order")
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .RequireAuthorization();

        group.MapGet("/{id:guid}", GetOrder)
            .WithName("GetOrder")
            .Produces<OrderResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", ListOrders)
            .WithName("ListOrders")
            .Produces<PagedList<OrderResponse>>();
    }

    private static async Task<Results<Created<OrderResponse>, ValidationProblem>> CreateOrder(
        CreateOrderRequest request,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(new CreateOrder.Command(request.CustomerId, request.Items), ct);
        return result.IsSuccess
            ? TypedResults.Created($"/api/orders/{result.Value.Id}", result.Value)
            : TypedResults.ValidationProblem(result.Errors);
    }

    private static async Task<Results<Ok<OrderResponse>, NotFound>> GetOrder(
        Guid id,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetOrder.Query(id), ct);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.NotFound();
    }

    private static async Task<Ok<PagedList<OrderResponse>>> ListOrders(
        [AsParameters] ListOrdersQuery query,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(query, ct);
        return TypedResults.Ok(result);
    }
}
```

### TypedResults for Type-Safe Responses

`TypedResults` provides compile-time guarantees and automatic OpenAPI schema generation.

```csharp
// GOOD — TypedResults with union return type
private static async Task<Results<Ok<Product>, NotFound, ValidationProblem>> GetProduct(
    Guid id,
    AppDbContext db,
    CancellationToken ct)
{
    var product = await db.Products.FindAsync([id], ct);
    return product is not null
        ? TypedResults.Ok(product)
        : TypedResults.NotFound();
}
```

### Parameter Binding

.NET 10 minimal APIs bind parameters from route, query, header, body, and DI automatically.

```csharp
// Route parameters
app.MapGet("/orders/{id:guid}", (Guid id) => ...);

// Query parameters (nullable = optional)
app.MapGet("/orders", (int page, int? pageSize, string? status) => ...);

// Complex query parameters with [AsParameters]
public record ListOrdersQuery(int Page = 1, int PageSize = 20, string? Status = null);
app.MapGet("/orders", ([AsParameters] ListOrdersQuery query) => ...);

// Header binding
app.MapGet("/orders", ([FromHeader(Name = "X-Correlation-Id")] string? correlationId) => ...);

// DI services are auto-resolved (no attribute needed)
app.MapPost("/orders", (CreateOrderRequest request, ISender sender) => ...);
```

### Endpoint Filters

Filters are the minimal API equivalent of action filters. Use them for cross-cutting concerns.

```csharp
// Validation filter
public class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (request is null)
            return TypedResults.BadRequest("Request body is required.");

        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
            return TypedResults.ValidationProblem(validationResult.ToDictionary());

        return await next(context);
    }
}

// Apply to an endpoint
group.MapPost("/", CreateOrder)
    .AddEndpointFilter<ValidationFilter<CreateOrderRequest>>();

// Apply to a group (affects all endpoints in the group)
group.AddEndpointFilter<LoggingFilter>();
```

### OpenAPI / Swagger Configuration

.NET 10 has built-in OpenAPI support. Use it instead of Swashbuckle.

```csharp
// Program.cs — service registration only, no endpoint wiring
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.MapEndpoints(); // auto-discovers all IEndpointGroup implementations

// Endpoint metadata enriches the OpenAPI spec
group.MapPost("/", CreateOrder)
    .WithName("CreateOrder")
    .WithSummary("Create a new order")
    .WithDescription("Creates a new order for the specified customer with the given line items.")
    .Produces<OrderResponse>(StatusCodes.Status201Created)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status500InternalServerError);
```

### Rate Limiting

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
    });
});

// Apply inside an IEndpointGroup.Map method
var group = app.MapGroup("/api/orders")
    .WithTags("Orders")
    .RequireRateLimiting("api");
```

### Output Caching

```csharp
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(builder => builder.Expire(TimeSpan.FromMinutes(5)));
    options.AddPolicy("ByIdCache", builder => builder
        .Expire(TimeSpan.FromMinutes(10))
        .SetVaryByRouteValue("id"));
});

group.MapGet("/{id:guid}", GetOrder)
    .CacheOutput("ByIdCache");
```

## Anti-patterns

### Don't Put Endpoints in Program.cs

```csharp
// BAD — endpoints scattered in Program.cs
app.MapGet("/orders", async (AppDbContext db) => await db.Orders.ToListAsync());
app.MapGet("/orders/{id}", async (Guid id, AppDbContext db) => await db.Orders.FindAsync(id));
app.MapPost("/orders", async (Order order, AppDbContext db) => { /* ... */ });
app.MapGet("/products", async (AppDbContext db) => await db.Products.ToListAsync());

// ALSO BAD — manual MapGroup calls in Program.cs (grows with every feature)
app.MapGroup("/api/orders").WithTags("Orders").MapOrderEndpoints();
app.MapGroup("/api/products").WithTags("Products").MapProductEndpoints();
app.MapGroup("/api/customers").WithTags("Customers").MapCustomerEndpoints();
// Program.cs grows every time you add a feature...

// GOOD — auto-discovered, Program.cs never changes
app.MapEndpoints(); // discovers all IEndpointGroup implementations
```

### Don't Use Untyped Results

```csharp
// BAD — Results.Ok doesn't contribute to OpenAPI schema
private static async Task<IResult> GetOrder(Guid id, AppDbContext db)
{
    var order = await db.Orders.FindAsync(id);
    return order is not null ? Results.Ok(order) : Results.NotFound();
}

// GOOD — TypedResults with explicit union type
private static async Task<Results<Ok<Order>, NotFound>> GetOrder(Guid id, AppDbContext db)
{
    var order = await db.Orders.FindAsync(id);
    return order is not null ? TypedResults.Ok(order) : TypedResults.NotFound();
}
```

### Don't Return Domain Entities Directly

```csharp
// BAD — leaks internal structure, can't evolve independently
app.MapGet("/orders/{id}", async (Guid id, AppDbContext db) =>
    await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id));

// GOOD — map to a response DTO
app.MapGet("/orders/{id}", async (Guid id, AppDbContext db) =>
{
    var order = await db.Orders
        .Where(o => o.Id == id)
        .Select(o => new OrderResponse(o.Id, o.Total, o.CreatedAt))
        .FirstOrDefaultAsync();
    return order is not null ? TypedResults.Ok(order) : TypedResults.NotFound();
});
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| New HTTP API | `IEndpointGroup` per feature + `app.MapEndpoints()` auto-discovery |
| Existing MVC project | Keep controllers, migrate incrementally |
| OpenAPI documentation | Use `TypedResults` + `.WithName()` + `.WithSummary()` |
| Request validation | Endpoint filter with FluentValidation |
| Authentication/authorization | `.RequireAuthorization("PolicyName")` on group or endpoint |
| Rate limiting | `AddRateLimiter` + `.RequireRateLimiting()` |
| Response caching | `AddOutputCache` + `.CacheOutput()` |
| Complex model binding | `[AsParameters]` with a record type |
