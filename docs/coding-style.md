# C# Application Rules

## Sealing

- Use `sealed` for classes and records by default.

## Dependency Injection

- Use dependency injection consistently and throughout. Never `new` up dependencies in classes.

## Asynchronous Initialization

- When classes need asynchronous initialization after construction, implement `IAsyncInitializable`.

## Async / Await

- Never use `async void` methods or discard `Task` results with fire-and-forget. Commands return `Task`; callers await them.
- Bridging from a synchronous callback that cannot be made async (e.g. event handlers) is the only acceptable fire-and-forget site. In that case, add a comment explaining why fire-and-forget is unavoidable and wrap the entire body in `try/catch` so exceptions are logged, not swallowed.

## Date and Time

- Always use `DateTimeOffset` in application code. `DateTime` is only acceptable at the database boundary where the DB column type requires it.
- When accessing the current time or a stopwatch, always inject and use `TimeProvider`.

## Nullability

- Enable nullable reference types project-wide. Treat nullable warnings as errors.
- Do not use the null-forgiving operator (`!`) except at deserialization boundaries where you have validated separately.

## String Comparison

- Always pass an explicit `StringComparison` to methods like `Equals`, `Contains`, `StartsWith`. Never rely on the default comparison behavior.

## Exception Handling

- Do not catch `Exception` broadly except at top-level boundaries (middleware, message handlers).
- Never catch and swallow exceptions silently.
- When rethrowing, use `throw;` not `throw ex;` to preserve the stack trace.

## Cancellation

- Accept and forward `CancellationToken` on read/query operations and pass it through to I/O calls. `CancellationToken` should be the last parameter.
- Write operations should generally not accept cancellation tokens — once a write begins, it runs to completion. The exception is operations that are fully atomic (e.g. a single database transaction that rolls back on cancellation).
- When a write must be cancellable for responsiveness reasons, document the rollback or compensation strategy explicitly.

## Collections as Return Types

- Return `IReadOnlyList<T>` or `IReadOnlyCollection<T>` from methods, not `List<T>` or `IEnumerable<T>`. The first leaks mutability; the second hides multiple enumeration risks.

## Configuration

- Bind configuration sections to strongly typed `IOptions<T>` classes. Never read `IConfiguration` directly in services — it makes dependencies on specific config keys invisible.

## Logging

- Use structured logging with message templates (`Log.Information("Processing order {OrderId}", id)`), never string interpolation (`$"Processing order {id}"`). The interpolated version defeats structured log storage and filtering.
- Message templates must be compile-time constants, not dynamically built strings.

## Disposal

- Implement `IAsyncDisposable` on classes that hold async resources. Register them in DI with appropriate lifetimes. Never rely on finalizers.

## Records and Value Semantics

- Use records for DTOs, events, and value objects.
- Use `init`-only properties over mutable setters where the object should not change after creation.

## Guard Clauses

- Validate arguments at method entry with `ArgumentNullException.ThrowIfNull` and similar helpers, rather than letting nulls propagate deeper into the call stack.

## Equality and GetHashCode

- When overriding `Equals`, always override `GetHashCode` as well. Mismatched implementations cause silent bugs in dictionaries and hash sets.
- Prefer records for types that need value equality — they generate correct `Equals` and `GetHashCode` automatically.

## Enum Handling

- Always handle unknown enum values defensively. Switch expressions must include a discard arm that throws.
- At deserialization boundaries, validate that the incoming value maps to a defined member. Enums are integers under the hood, so invalid values pass through silently.

## HttpClient Lifetime Management

- Never `new` up `HttpClient` per request. Use `IHttpClientFactory` or register named/typed clients through DI. Direct instantiation leads to socket exhaustion under load.

## Static State

- Do not use static mutable fields or properties for shared state. They break testability, create hidden coupling, and cause concurrency bugs.
- If something needs to be shared, model it as a singleton service through DI.

## Primitive Obsession

- For domain concepts like email addresses, currency amounts, or identifiers, wrap them in strongly typed value objects or `readonly record struct`s rather than passing raw strings, decimals, or GUIDs.
- This prevents mixing up parameters of the same primitive type and gives a place to put validation.

## Const vs Static Readonly

- Use `const` only for values that are truly universal and will never change across versions (e.g. mathematical constants).
- Prefer `static readonly` for everything else. `const` values are baked into calling assemblies at compile time and will not update without recompilation.

## Magic Strings and Numbers

- Extract literals into named constants, enums, or configuration. This applies especially to dictionary keys, status codes, and role names that appear in multiple places.

## String Allocation Awareness

- Be mindful that every string operation that returns a new string (`Substring`, `Split`, `ToLower`, concatenation) allocates on the heap. Before reaching for these, consider whether a non-allocating alternative exists.
- Prefer `ReadOnlySpan<char>` and `AsSpan()` over `Substring` when the result is only used for parsing, comparison, or further slicing — no allocation needed for intermediate values.
- Use `StringBuilder` or `string.Create` when building strings from multiple parts. Repeated concatenation in loops creates a new string on every iteration.
- Prefer `string.Concat` or `string.Join` over manual `+` chains for combining a known set of values — they allocate once.
- When parsing structured strings, prefer `Span<T>`-based APIs like `MemoryExtensions.Split` (available since .NET 8) and `int.Parse(ReadOnlySpan<char>)` over splitting into `string[]` and parsing each element.
- This is about avoiding *waste*, not micro-optimization. Focus on code paths that process collections, run in loops, or handle high-throughput input.

## ArrayPool Usage

- Use `ArrayPool<T>.Shared` for temporary arrays, especially in code paths that allocate frequently or handle variable-size buffers. Return arrays in a `finally` block or via `IDisposable` wrapper to prevent pool leaks.
- For byte buffers in I/O scenarios, prefer `ArrayPool<byte>` over `new byte[]` allocations.
