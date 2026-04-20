# C# Application Rules

## Sealing

- Use `sealed` for classes and records by default.

## Dependency Injection

- Use DI for services and objects with lifetime. Never `new` up a dependency that belongs in the container. Directly instantiating value objects, DTOs, records, and framework types (`List<T>`, `CancellationTokenSource`, etc.) is fine — the rule is about collaborators, not every `new`.

## Asynchronous Initialization

- When classes need asynchronous initialization after construction, implement `IAsyncInitializable`.

## Async / Await

- Never use `async void` methods or discard `Task` results with fire-and-forget. Commands return `Task`; callers await them.
- Bridging from a synchronous callback that cannot be made async (e.g. event handlers) is the only acceptable fire-and-forget site. In that case, add a comment explaining why fire-and-forget is unavoidable and wrap the entire body in `try/catch` so exceptions are logged, not swallowed.

## ConfigureAwait

- Application code (executables, apps, top-level services) does not need `ConfigureAwait(false)`. We do not run on a synchronization context that requires it.
- Library code meant for reuse outside this codebase (NuGet packages, shared infrastructure libraries) should use `ConfigureAwait(false)` on every `await`. Consumers may have a sync context, and omitting it creates deadlock risk on their side.

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

Accept and forward `CancellationToken` on read/query operations and pass it through to I/O calls. `CancellationToken` is always the last parameter.

For writes, classify the operation first. The goal is to force "what's the state if this is cancelled?" to have a written answer — not to ban cancellation.

1. **Atomic writes** — a single operation the underlying library already makes all-or-nothing (one `SaveChangesAsync`, one HTTP request, one `File.WriteAllBytesAsync`). **Accept the token.** Cancellation is safe; ignoring it causes hung shutdowns and unresponsive UIs.

2. **Composite writes** — multiple steps without a unifying transaction (write file + update DB + publish event). **Do not accept a token** unless you document the compensation/rollback strategy in an XML doc comment on the method. "What is the state if cancelled between steps?" must have a written answer, reviewed at PR time.

3. **Long-running writes** (bulk imports, report generation, large aggregations) — accept a token and honor it only at designated safe points between units of work. Document where those points are.

If you can't confidently classify the write, treat it as composite until proven otherwise.

## Collections as Return Types

- Return `IReadOnlyList<T>` or `IReadOnlyCollection<T>` from methods, not `List<T>` or `IEnumerable<T>`. The first leaks mutability; the second hides multiple enumeration risks.

## Configuration

- Bind configuration sections to strongly typed `IOptions<T>` classes. Never read `IConfiguration` directly in services — it makes dependencies on specific config keys invisible.

## Logging

- Use structured logging with message templates (`Log.Information("Processing order {OrderId}", id)`), never string interpolation (`$"Processing order {id}"`). The interpolated version defeats structured log storage and filtering.
- Message templates must be compile-time constants, not dynamically built strings.

## JSON Serialization

- Default serializer is `System.Text.Json`. Only reach for Newtonsoft.Json when a specific feature requires it.
- **Register `JsonSerializerOptions` as a singleton via DI and inject it.** STJ caches type metadata on the options instance; constructing new options per call defeats that cache and noticeably degrades performance.
- **Casing and enum representation are contract-level decisions, not global defaults.** Do not set a global `PropertyNamingPolicy` and do not globally register `JsonStringEnumConverter`. Declare them per property or per type with `[JsonPropertyName]` and `[JsonConverter]` where the wire format must differ from the C# default.

## Data Access

EF Core and Dapper (or raw ADO.NET) are both in use. The rules below apply wherever each is used; choosing between them is a per-case judgment and not prescribed here.

### EF Core

- Use `AsNoTracking()` for read-only queries. The change tracker is a cost you only pay when you plan to write back.
- **Project to DTOs in the query**: `.Select(e => new FooDto { … })` rather than materializing entities and mapping in memory. This also pushes column selection into SQL.
- Do not let `IQueryable<T>` escape a repository or service method. Once the caller holds it, query composition and materialization happen far from where the SQL contract lives. Return materialized DTOs or read-only collections.
- Eager-load (`Include`) only when you need the related data on every row. Prefer projection to shape exactly what the caller wants.
- Watch for N+1 when iterating a collection and accessing navigation properties lazily. Profile with a SQL log sink when a query path feels slow.
- Use `FromSqlInterpolated` (parameterized) rather than `FromSqlRaw` with string concatenation. Never build SQL from user input via string interpolation.
- Keep the `DbContext` scoped (the standard `AddDbContext` lifetime). Don't manually manage the underlying connection.

### Dapper and raw SQL

- **Always parameterize.** Never concatenate values into SQL, ever. This is the one absolute in this section.
- Pass `CancellationToken` through to `*Async` methods on reads per the general cancellation rule.
- For non-trivial queries, keep SQL in `.sql` resource files or in clearly-named `const string` literals in a dedicated class — not scattered inline across services.
- Use strongly-typed parameter objects or `DynamicParameters` rather than anonymous objects when the parameter set is reused across calls.
- Acquire connections from a factory, wrap in `using`, dispose promptly. Don't hand connections around between methods.
- Map results to `record` DTOs. Don't pass `dynamic` beyond the data-access layer.

### Shared

- Connection strings and credentials come from configuration (`IOptions<T>`), never from source.
- Migrations are owned by exactly one tool per database. If EF owns some tables and another tool owns others, document the split and configure EF so it does not generate migrations that touch non-EF tables.
- Translate DB types to application types at the data-access boundary: `DateTime` columns → `DateTimeOffset` in the domain, `DECIMAL` → `decimal`. The rest of the app should not see raw provider types.

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

## Numeric Types

- **Money and business quantities** use `decimal`. Exact decimal arithmetic, no binary-float rounding surprises.
- **Scientific, statistical, signal, and graphics computation** use `double` (or `float`). Fast, with transcendentals (`Math.Sin`, etc.) that `decimal` does not offer.
- Never `double` for money. Never `decimal` for physical computation — orders of magnitude slower, and missing the math library.
- Match DB column types at the boundary: `DECIMAL(p,s)` ↔ `decimal`; `FLOAT`/`REAL` ↔ `double`/`float`. Mismatches silently truncate or lose precision.
- When parsing numbers from machine input (JSON, config, IDs, CSV), always pass `CultureInfo.InvariantCulture`. Culture-aware parsing is only for genuine user input.

## Const vs Static Readonly

- Use `const` only for values that are truly universal and will never change across versions (e.g. mathematical constants).
- Prefer `static readonly` for everything else. `const` values are baked into calling assemblies at compile time and will not update without recompilation.

## Magic Strings and Numbers

- Extract literals into named constants, enums, or configuration. This applies especially to dictionary keys, status codes, and role names that appear in multiple places.

## Allocation Awareness

Awareness of modern tools, not ritual. Most code is line-of-business where the GC handles short-lived allocations fine — focus on code that runs in loops, processes collections, or handles high-throughput input. The concern is *allocation pressure*, not micro-optimization and not working-set memory. Outside hot paths, write the clear version first; sloppy string handling is the most common offender.

### Strings

- Every string op that returns a new string (`Substring`, `Split`, `ToLower`, `+` concatenation) allocates. Before reaching for these, ask whether a non-allocating alternative exists — this is the single largest source of avoidable allocations we see.
- Prefer `ReadOnlySpan<char>` / `AsSpan()` over `Substring` when the result is only used for parsing, comparison, or further slicing.
- Use `StringBuilder` or `string.Create` when building strings from multiple parts. `+=` in a loop allocates per iteration.
- Prefer `string.Concat` / `string.Join` over `+` chains for a known set of values — one allocation instead of N.
- For structured parsing, prefer span-based APIs (`MemoryExtensions.Split` since .NET 8, `int.Parse(ReadOnlySpan<char>)`) over splitting into `string[]` and parsing each element.
- Spans can't cross `await` and don't compose with LINQ. If a span forces you to contort the code, the allocation is cheaper than the contortion — use the string version.

### Library and Shared API Signatures

For shared/infrastructure methods — not every service method — prefer span or memory over `string` / `byte[]` where the implementation allows. Overload rules differ between sync and async because the implicit conversions do.

**Sync APIs — `ReadOnlySpan<char>` alone is usually enough.** `string` → `ReadOnlySpan<char>` is implicit, so a `string` overload that just forwards to the span version is redundant. Add a `string` overload only when: null semantics matter (spans can't distinguish null from empty), a default parameter value is needed (spans have no constant form), the method is used in expression trees, or the implementation needs string identity (interning, reference-based caching, dictionary keys without materializing).

**Async / storing APIs — provide both `string` and `ReadOnlyMemory<char>`.** `string` → `ReadOnlyMemory<char>` is **not** implicit; without the `string` overload every caller writes `.AsMemory()` and skips it. Memory is the primitive; the `string` overload is a thin forwarder.

**Bytes.** `byte[]` has implicit conversions to both `ReadOnlySpan<byte>` and `ReadOnlyMemory<byte>`. Dedicated `byte[]` overloads are rarely needed.

### ArrayPool (situational)

Applies to serializers, binary parsers, streaming pipelines, and I/O code with variable-size buffers. **Not a default for general app code** — Gen 0 GC handles short-lived arrays well, and misuse creates silent bugs.

- Use `ArrayPool<T>.Shared` in the scenarios above; for I/O byte buffers, prefer it over `new byte[]`.
- `Rent(n)` returns an array of length **at least** `n`, usually larger. Track the requested size separately; don't trust `.Length`.
- Return in `finally` or via a disposable wrapper. A missed return on an error path is a silent pool leak.
- For sensitive data, use `Return(array, clearArray: true)`.
- Never store a rented array past the method that rented it unless ownership transfer is explicit and documented.

## Regular Expressions

- Use source-generated regexes (`[GeneratedRegex]`) for all regular expressions by default, where possible.
- Fall back to pre-compiling with `RegexOptions.Compiled` if source generation is not an option (e.g. dynamic patterns, unsupported features).
