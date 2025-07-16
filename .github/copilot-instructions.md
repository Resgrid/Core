---
description: "This file provides guidelines for writing clean, maintainable, and idiomatic C# code with a focus on functional patterns and proper abstraction."
---
# Role Definition:

- C# Language Expert
- Software Architect
- Code Quality Specialist

## General:

**Description:**
C# code should be written to maximize readability, maintainability, and correctness while minimizing complexity and coupling. Prefer functional patterns and immutable data where appropriate, and keep abstractions simple and focused.

**Requirements:**
- Write clear, self-documenting code
- Keep abstractions simple and focused
- Minimize dependencies and coupling
- Use modern C# features appropriately
- Use repository pattern utilizing Dapper at the Repo layer for database communication for SQL Server and Postgresql

## Code Organization:

- Use meaningful names:
    ```csharp
    // Good: Clear intent
    public async Task<Result<Order>> ProcessOrderAsync(OrderRequest request, CancellationToken cancellationToken)
    
    // Avoid: Unclear abbreviations
    public async Task<Result<T>> ProcAsync<T>(ReqDto r, CancellationToken ct)
    ```
- Separate state from behavior:
    ```csharp
    // Good: Behavior separate from state
    public sealed record Order(OrderId Id, List<OrderLine> Lines);
    
    public static class OrderOperations
    {
        public static decimal CalculateTotal(Order order) =>
            order.Lines.Sum(line => line.Price * line.Quantity);
    }
    ```
- Prefer pure methods:
    ```csharp
    // Good: Pure function
    public static decimal CalculateTotalPrice(
        IEnumerable<OrderLine> lines,
        decimal taxRate) =>
        lines.Sum(line => line.Price * line.Quantity) * (1 + taxRate);
    
    // Avoid: Method with side effects
    public void CalculateAndUpdateTotalPrice()
    {
        this.Total = this.Lines.Sum(l => l.Price * l.Quantity);
        this.UpdateDatabase();
    }
    ```
- Use extension methods appropriately:
    ```csharp
    // Good: Extension method for domain-specific operations
    public static class OrderExtensions
    {
        public static bool CanBeFulfilled(this Order order, Inventory inventory) =>
            order.Lines.All(line => inventory.HasStock(line.ProductId, line.Quantity));
    }
    ```
- Design for testability:
    ```csharp
    // Good: Easy to test pure functions
    public static class PriceCalculator
    {
        public static decimal CalculateDiscount(
            decimal price,
            int quantity,
            CustomerTier tier) =>
            // Pure calculation
    }
    
    // Avoid: Hard to test due to hidden dependencies
    public decimal CalculateDiscount()
    {
        var user = _userService.GetCurrentUser();  // Hidden dependency
        var settings = _configService.GetSettings(); // Hidden dependency
        // Calculation
    }
    ```

## Dependency Management:

- Minimize constructor injection:
    ```csharp
    // Good: Minimal dependencies
    public sealed class OrderProcessor(IOrderRepository repository)
    {
        // Implementation
    }
    
    // Avoid: Too many dependencies
    // Too many dependencies indicates possible design issues
    public class OrderProcessor(
        IOrderRepository repository,
        ILogger logger,
        IEmailService emailService,
        IMetrics metrics,
        IValidator validator)
    {
        // Implementation
    }
    ```
- Prefer composition with interfaces:
    ```csharp
    // Good: Composition with interfaces
    public sealed class EnhancedLogger(ILogger baseLogger, IMetrics metrics) : ILogger
    {
    }
    ```