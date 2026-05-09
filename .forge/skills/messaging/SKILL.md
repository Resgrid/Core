---
name: messaging
description: >
  Asynchronous messaging patterns for .NET applications. Covers Wolverine and
  MassTransit, outbox pattern, saga and choreography, and broker configuration
  for RabbitMQ and Azure Service Bus.
  Load this skill when implementing event-driven communication, background
  processing, module-to-module messaging, or when the user mentions "Wolverine",
  "MassTransit", "message bus", "RabbitMQ", "Azure Service Bus", "event",
  "publish", "consumer", "outbox", "saga", "integration event", "queue",
  or "pub/sub".
---

# Messaging

## Core Principles

1. **Wolverine is the recommended default** — MIT licensed, combines mediator + messaging in one library with built-in outbox, saga support, and convention-based handlers. MassTransit is an alternative but requires a commercial license from v9.
2. **Outbox pattern for reliability** — Always use the transactional outbox to ensure messages are published only when the database transaction succeeds.
3. **Choreography for simple flows, saga for complex** — If a workflow has 2-3 steps, use event choreography. If it has compensating actions or complex state, use a saga.
4. **Messages are contracts** — Put message types in a shared contracts project. Keep them as simple records with primitive types.

## Patterns

### Wolverine Setup

```csharp
// Program.cs
builder.Host.UseWolverine(opts =>
{
    // Auto-discover handlers from this assembly
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly);

    // RabbitMQ transport
    opts.UseRabbitMq(rabbit =>
    {
        rabbit.HostName = "localhost";
        // Or from configuration:
        // rabbit.HostName = builder.Configuration["RabbitMq:Host"]!;
    })
    .AutoProvision()   // Create queues/exchanges automatically
    .AutoPurgeOnStartup(); // Dev only — clear queues on startup

    // Enable transactional outbox with EF Core
    opts.Services.AddDbContextWithWolverineIntegration<AppDbContext>(x =>
        x.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

    opts.Policies.AutoApplyTransactions(); // Wrap handlers in DB transactions
});
```

**Why**: `UseWolverine()` registers handler discovery, transport, and outbox in one place. `AutoProvision()` eliminates manual broker setup during development.

### Publishing Events

Wolverine supports two publishing styles: cascading messages (return values) and explicit publishing.

```csharp
// Message contract (in shared Contracts project)
public record OrderCreated(Guid OrderId, string CustomerId, decimal Total, DateTimeOffset CreatedAt);

// Style 1: Cascading messages — return the event from the handler
// Wolverine automatically publishes returned messages after the handler completes.
public static class CreateOrder
{
    public record Command(string CustomerId, List<OrderItem> Items);
    public record Response(Guid OrderId, decimal Total);

    public static async Task<(Response, OrderCreated)> HandleAsync(
        Command command, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var order = Order.Create(command.CustomerId, command.Items, clock.GetUtcNow());
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        var response = new Response(order.Id, order.Total);
        var @event = new OrderCreated(order.Id, order.CustomerId, order.Total, order.CreatedAt);

        return (response, @event); // Both are published automatically
    }
}
```

```csharp
// Style 2: Explicit publishing via IMessageBus
public static class CreateOrder
{
    public record Command(string CustomerId, List<OrderItem> Items);
    public record Response(Guid OrderId, decimal Total);

    public static async Task<Response> HandleAsync(
        Command command, AppDbContext db, IMessageBus bus, TimeProvider clock, CancellationToken ct)
    {
        var order = Order.Create(command.CustomerId, command.Items, clock.GetUtcNow());
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        await bus.PublishAsync(new OrderCreated(
            order.Id, order.CustomerId, order.Total, order.CreatedAt));

        return new Response(order.Id, order.Total);
    }
}
```

**Why**: Cascading messages (tuple return) are simpler and testable — the handler is a pure function. Use explicit `IMessageBus` when publishing is conditional or requires multiple events.

### Consuming Events

Wolverine uses convention-based handlers — no interface, no base class. Just a `Handle` method with the message type as the first parameter.

```csharp
// Notifications module — handles OrderCreated from Orders module
public static class OrderCreatedHandler
{
    public static async Task HandleAsync(
        OrderCreated message, NotificationsDbContext db, ILogger logger, CancellationToken ct)
    {
        logger.LogInformation("Processing OrderCreated: {OrderId}", message.OrderId);

        var notification = new OrderNotification(message.OrderId, message.CustomerId);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);
    }
}
```

**Why**: Convention-based handlers have zero ceremony. Wolverine discovers them by signature: any public method named `Handle`/`HandleAsync`/`Consume`/`ConsumeAsync` with the message type as the first parameter.

### Transactional Outbox

Ensures messages are only published if the database transaction succeeds.

```csharp
// 1. Register DbContext with Wolverine integration
builder.Host.UseWolverine(opts =>
{
    opts.Services.AddDbContextWithWolverineIntegration<AppDbContext>(x =>
        x.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

    opts.Policies.AutoApplyTransactions();
});

// 2. DbContext — add Wolverine outbox tables
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Wolverine inbox/outbox tables — required for transactional messaging
        modelBuilder.AddIncomingWolverineMessageTable();
        modelBuilder.AddOutgoingWolverineMessageTable();
    }
}
```

**Why**: `AddDbContextWithWolverineIntegration` + `AutoApplyTransactions` wraps every handler in a transaction that includes outbox writes. Messages are only sent after the transaction commits — no dual-write problem.

### Saga (Stateful Orchestration)

Wolverine sagas use a `Saga<T>` base class with `Start` and `Handle` methods. Cascading messages drive the saga forward.

```csharp
public record OrderSagaState(Guid Id)
{
    public string? CustomerId { get; set; }
    public bool PaymentReceived { get; set; }
}

public class OrderSaga : Saga<OrderSagaState>
{
    public Guid Id { get; set; }

    // Start the saga when an OrderCreated event arrives
    public static (OrderSagaState, ProcessPayment) Start(OrderCreated message)
    {
        var state = new OrderSagaState(message.OrderId)
        {
            CustomerId = message.CustomerId
        };

        var command = new ProcessPayment(message.OrderId, message.Total);
        return (state, command); // State is persisted, command is sent
    }

    // Handle payment result
    public CompleteOrder Handle(PaymentCompleted message)
    {
        PaymentReceived = true;
        MarkCompleted(); // Ends the saga
        return new CompleteOrder(Id);
    }

    // Compensating action on failure
    public CancelOrder Handle(PaymentFailed message)
    {
        MarkCompleted();
        return new CancelOrder(Id);
    }
}
```

**Why**: Wolverine sagas use simple C# methods instead of a state machine DSL. Each handler returns cascading messages to drive the workflow. `MarkCompleted()` cleans up the saga state.

### Alternative: MassTransit

MassTransit is a mature alternative with a commercial license requirement from v9+. Key API surface:

```csharp
// Setup
builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();
    x.AddConsumers(typeof(Program).Assembly);
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("RabbitMq"));
        cfg.ConfigureEndpoints(context);
    });
});

// Publishing
await publishEndpoint.Publish(new OrderCreated(...), ct);

// Consuming — requires IConsumer<T> interface
public class OrderCreatedConsumer(AppDbContext db) : IConsumer<OrderCreated>
{
    public async Task Consume(ConsumeContext<OrderCreated> context)
    {
        var message = context.Message;
        // Handle event...
    }
}

// Outbox
x.AddEntityFrameworkOutbox<AppDbContext>(o =>
{
    o.UsePostgres();
    o.UseBusOutbox();
});

// Saga — uses MassTransitStateMachine<TState>
public class OrderSaga : MassTransitStateMachine<OrderSagaState> { /* ... */ }
```

> **License note**: MassTransit v9+ requires a commercial license for production use. Wolverine (MIT) is the recommended default for new projects.

## Anti-patterns

### Don't Publish Events Without Outbox

```csharp
// BAD — if SaveChanges succeeds but Publish fails, data is inconsistent
await db.SaveChangesAsync(ct);
await bus.PublishAsync(new OrderCreated(...));

// GOOD — use transactional outbox (messages are in the same transaction)
// Configure AddDbContextWithWolverineIntegration() + AutoApplyTransactions()
// Wolverine handles this automatically
```

### Don't Put Complex Logic in Message Contracts

```csharp
// BAD — behavior in a message
public record OrderCreated(Guid OrderId)
{
    public decimal CalculateShipping() => /* logic */; // DON'T
}

// GOOD — messages are pure data
public record OrderCreated(Guid OrderId, string CustomerId, decimal Total, DateTimeOffset CreatedAt);
```

### Don't Use Fire-and-Forget for Important Events

```csharp
// BAD — no guarantee of delivery
_ = Task.Run(() => bus.PublishAsync(new OrderCreated(...)));

// GOOD — await the publish (with outbox, this is transactional)
await bus.PublishAsync(new OrderCreated(...));
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| Module-to-module communication (new project) | Wolverine with events (MIT, free) |
| Module-to-module communication (existing MassTransit) | MassTransit (commercial license required from v9) |
| Reliable event publishing | Transactional outbox (both Wolverine and MassTransit support this) |
| Simple 2-3 step workflow | Event choreography |
| Complex workflow with compensation | Wolverine saga or MassTransit saga |
| Local development broker | RabbitMQ (via Docker or Aspire) |
| Production cloud broker | Azure Service Bus or RabbitMQ |
| Want single lib for mediator + messaging | Wolverine (replaces both Mediator and MassTransit) |
