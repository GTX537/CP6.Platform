# WP02-11 HTTP v2: explicit local library capability

This change adds `CP6.Platform.AspNetCore.HttpV2`. It does not enable Main/CRM routes, publish a package, install an Owner adapter, change a database, or prove durable business deduplication. Production write bindings remain **Disabled / UNPROVEN** until their Host and business Owner supply the required evidence. Test authorities in `OwnerProtocolFixtureTests` are explicitly library protocol fixtures.

## Opt in one named client

```csharp
using CP6.Platform.AspNetCore;
using CP6.Platform.AspNetCore.HttpV2;
using Microsoft.Extensions.DependencyInjection;

services.AddHttpClient("catalog-read", client =>
    client.BaseAddress = new Uri("https://catalog.example/"))
    .AddCp6HttpResilience(new Cp6HttpV2Profile(
        "catalog-read", Cp6HttpOperationKind.IdempotentRead,
        retryMode: Cp6HttpRetryMode.OwnerProvenIdempotent));

// An ordinary write has one send and no automatic replay, even with an Idempotency-Key.
services.AddHttpClient("order-write")
    .AddCp6HttpResilience(new Cp6HttpV2Profile(
        "order-write", Cp6HttpOperationKind.IdempotentWrite));
```

`Disabled` is the default and means zero retries for **all** operation kinds. An explicitly enabled read profile needs no write authority. The approved methods are GET/HEAD/OPTIONS for reads and POST/PUT/PATCH/DELETE for both write classes. `NonIdempotent` forces retry mode Disabled and retry count zero. One-time credential exchange, login codes and unproven external payments must remain non-idempotent.

| Parameter | Default | Accepted range / rule |
| --- | --- | --- |
| clientName | required | nonempty, trimmed, no controls, ≤128 characters |
| retryAttempts | 2 | 0–5; NonIdempotent forces 0 |
| attemptTimeout | 2 seconds | 100 ms–30 seconds; must not exceed total |
| totalTimeout | 10 seconds | 250 ms–120 seconds |
| circuitSamplingDuration | 10 seconds | 1–120 seconds |
| circuitMinimumThroughput | 10 | 2–1000 |
| circuitBreakDuration | 30 seconds | 1–300 seconds |
| baseDelayMs / maxDelayMs | 200 / 2000 | 0≤base≤max≤30000 |
| jitterRatio | 0.2 | finite 0–0.5 |
| requestReplayLimitBytes | 1048576 | 1–1048576 |
| responseLimitBytes | 1048576 | 1–16777216 |
| policyVersion | cp6-http-v2 | immutable nonempty bounded reference, ≤256 characters |

Identical profile/binding/authority-factory registrations are idempotent; conflicts fail. Wrong client or policy version fails. Legacy and v2 policy handlers, additional resilience handlers and other additional handlers cannot stack on the v2 client. The normal HttpClient logging filters remain available. An opted-in client's `HttpClient.Timeout` is infinite; v2 owns the total deadline. A later configuration restoring a finite client timeout fails at factory creation. Consumers must not mutate it afterward.

The opt-in closes automatic redirects and cookie acquisition on the existing SocketsHttpHandler/HttpClientHandler while preserving its other settings; later attempts to enable either fail. This avoids hidden sends to another destination or credentials outside the verified snapshot. A custom primary transport remains trusted Host code: its internal retry, redirect, credential and timeout behavior must be inventoried and independently accepted. The library cannot prove opaque transport/SDK/sidecar behavior. Host acceptance must identify one retry owner and bound all external layers; this library permits at most `1 + retryAttempts` sends in one call, plus at most one original-result query per uncertain send.

## Registered Owner proof, not caller assertions

An enabled write registration additionally requires a `Cp6HttpOperationBinding` and an `ICp6HttpOperationAuthority` factory:

```csharp
// binding is an immutable Host configuration reviewed against actual Owner evidence.
// No production binding or authority implementation is supplied by this package.
services.AddHttpClient(binding.ClientName)
    .AddCp6HttpResilience(writeProfile, binding,
        provider => provider.GetRequiredService<MyOwnerAuthority>());

var original = new Cp6HttpOperationIdentity(binding, tenant, action, originalKey,
    requestVersion, canonicalBodyBytes);
using var request = new HttpRequestMessage(binding.Method, binding.Destination)
{
    Content = new ByteArrayContent(original.CopyBody())
};
request.Headers.Add("Idempotency-Key", original.Key);
// Host builds the registered credential/tenant headers; the authority checks the actual snapshot.
request.Options.Set(Cp6HttpOperationIdentity.RequestOption, original);
using var response = await client.SendAsync(request, cancellationToken);
```

`MyOwnerAuthority`, the binding and its credentials are Host-owned integrations, not a sample proof to copy into production. The binding carries binding/client/host/Owner/endpoint identities, tenant scope and key/digest domain references, method, exact HTTPS destination, credential profile, configuration and route epoch, HTTP/gateway policy versions, receipt/query/retention contracts, and registration/runtime evidence references. Missing references are rejected; populated references by themselves are **not** evidence.

`VerifyBindingAsync(identity, actualRequestSnapshot, deadline, token)` must verify accepted runtime registration, tenant/action/key/version/digest, destination/method, exact body and actual credential/tenant headers against the trusted Host and remote Owner contract. `Cp6HttpRequestSnapshot` includes sensitive headers for this check; **never log or serialize it**. Its copies cannot mutate the sent request. The identity's constructor and `CopyBody()` also copy bytes. Request headers, destination, method and content are frozen for all replayable sends, including the first. Changing a caller request afterward cannot change later attempts.

Only byte-array based content under the replay limit is eligible for replay. Streams, arbitrary custom content, and oversized bodies can still make their single normal send, but do not acquire replay or original-query authority from a caller-created DTO. The latter limitation is deliberate: the library cannot prove their sent bytes equal the claimed original identity. Their uncertain outcomes stay Unknown for the Host's separate authorized recovery workflow. A verified bounded identity can still query its original result when retries are Disabled or zero.

Owner proof must cover durable tenant+action+key/input-digest binding, same-key/different-input conflict, original immutable result, sufficient retention, and effect+receipt+required Outbox in the same Owner transaction. The Owner retains its **current final authorization fence** in that transaction. Neither this library's precheck, a valid header, an HTTP verb, nor a caller bool can establish that proof. The transport snapshot is not an authorization ticket.

## One monotonic deadline, explicit timeout source

The execution order is total deadline → bounded retry/recovery → Polly circuit → attempt deadline including the entire response body. `Cp6HttpDeadline` uses `TimeProvider.GetTimestamp/GetElapsedTime`; UTC is audit/date conversion only. It caps children to the live parent's remaining time and links both parent and current caller cancellation. Nested callers can set `Cp6HttpV2Handler.ParentDeadlineOption` with a live parent. All retry anchors then use that parent's timestamp domain, even when the handler was registered with another TimeProvider.

Deadline hierarchy retains original caller cancellation separately from timer expiry. Cancelling any ancestor's caller propagates ordinary cancellation through send, retry wait and original-result query, even when the current SendAsync token is None. An ancestor's elapsed timer remains a total timeout; neither source permits a later attempt.

Connection/send, content snapshot, verification, waiting, parsing/queueing, response-body reads, Owner query and original-result body reads consume the same total budget. Original queries have one bounded attempt with `min(attemptTimeout, remainingTotal)` and no internal automatic query loop. Response bodies are buffered under the active deadline and byte limit before returning, including with `ResponseHeadersRead`. This opt-in therefore is not a streaming-response API.

Authority callbacks and original-result/body completions are checked again against caller/parent cancellation and the active total/child deadline before their result is interpreted. An already-completed task is not evidence that it completed within budget. Cancelled or late results cannot become a binding/replay denial, Applied result, or returned 503; late response bodies are disposed. Direct response-stream `IOException`/`HttpIOException` is classified as a transport failure so a verified uncertain write still queries its original receipt; IO failure reading the original result remains Unknown/unavailable.

Timeout classification uses the actual timer/deadline source, not comparison of configured durations. Equal attempt and total durations still classify a reached total deadline as TotalTimeout. Unrelated cancellation/Polly timeout sources become UnknownTimeoutSource and are not retried. Caller cancellation propagates `OperationCanceledException`, records the existing operation state, and starts no later send. A cancelled wait or deadline is not a business rollback or cancellation command. Noncooperative transports may finish in the background; their late response is disposed and cannot cause another library send.

## Retry decision

Approved transient responses are 408, 429, 500, 502, 503 and 504, plus HttpRequestException. V2's own explicitly identified attempt timeout is eligible within remaining budget; total/unknown-source timeouts are not. 401/403/409/422 and other business rejections are not generic retry triggers. The legacy profile still has its original transient and timeout exclusions.

At the first observable response, sample trusted UTC `t0` and monotonic `m0` once. A transport exception with no response is anchored when the exception is observed. Retry-After accepts exactly one field value; a date's comma is not a list separator. Only HTTP OWS is trimmed. Delta-seconds are ASCII nonnegative integers. IMF-fixdate, RFC850 and asctime are parsed invariantly in UTC; RFC850's >50-year future interpretation moves to the matching past century. HTTP leap-second syntax is converted through the preceding representable second plus one second, preserving earliest wait. Dates in the past yield zero. Duplicate/merged/negative/fractional/invalid values mark InvalidRetryAfter and use local waiting. Arbitrarily large legal digit strings stop with StopServerWaitExceeded instead of becoming invalid.

For retry `k` starting at 1, sample `u∈[0,1]` exactly once:

```text
B = min(maxDelayMs, baseDelayMs × 2^(k−1))
L = ceil(clamp(B × (1−jitterRatio + 2×jitterRatio×u), 0, maxDelayMs))
earliest = m0 + max(S, L)
W = max(0, earliest − nowMonotonic)
admit only if W + fullAttemptTimeout ≤ remainingTotal
```

`S>30000ms` stops immediately; 30000 is an admission cap, never a shortened server wait. MaxDelay and jitter only affect `L`. The decision and actual send both reserve a complete attempt; equality is allowed. Early wakes continue waiting against the same sampled earliest time; late wakes recalculate remaining budget. Parsing and queue time do not reset the anchor. Examples at `u=.5,k=1`: S100 waits200; S5000 waits5000 despite local max2000; S60000/R100000/A2000 stops immediately; S15000/R10000 and S9000/R10000/A2000 stop; S8000/R10000/A2000 is admissible.

## Unknown and original-result recovery

`Cp6HttpV2Outcome` (`cp6-http-outcome-v2`) separates operation state from transport failure and retry stop/header classification. Returned responses place the outcome on the original request's `Cp6HttpV2Outcome.RequestOption`; `Cp6HttpV2Exception.Outcome` carries it on bounded failure. Transport observation alone yields ResponseReceived, not business Applied. Do not serialize internal identity/outcome objects directly as an external status DTO; the Host supplies its authorized safe projection.

A proven sent write with uncertain outcome queries the original receipt **before** `AuthorizeReplayAsync`. Applied/Rejected/Pending reuse the original authorized masked response. Unknown, unavailable, or NotFound retains Unknown and stops. NotFound is not proof of nonapplication. Only trusted `NotAppliedProven` can reach new-effect authorization and then the remaining retry/budget gates. Losing today's new-write permission cannot block an independently authorized historical read. Query results never grant permission for new effects. No key, tenant, digest, policy, route epoch, body or credential is replaced mid-call.

An open circuit reports NotSent only when this call has never sent; a prior uncertain send remains Unknown. Polly controls open/half-open probes; no new business identity is created and no DLQ is cleared.

This package implements recovery within one bounded `SendAsync`. A fresh `SendAsync` is **not** an approved continuation of a prior Unknown command. Cross-call/offline recovery belongs to the Host/S6 adapter, which must query the original operation, retain policy and lifecycle budgets, and apply the Owner's typed recovery gates. Creating a new DTO/key, resubmitting after timeout, or pretending the old call was NotSent is prohibited. No caller-supplied previous-outcome option creates recovery authority.

## Endpoint-bound safe problems

```csharp
var policy = new Cp6SafeProblemPolicy("order-validation-v2",
    new Cp6ProblemDefinition("https://errors.example/invalid", "Invalid input", 422,
        "ORDER_INVALID", "orders.error.invalid"),
    new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        ["quantity"] = new Dictionary<string, string> { ["positive"] = "Enter a positive quantity." }
    });
services.AddCp6ProblemDetails(policy);
app.MapPost("/orders", async (HttpContext context) =>
    await context.WriteCp6ProblemAsync(policy,
        new Dictionary<string, string[]> { ["quantity"] = ["positive"] }))
    .WithMetadata(policy);
```

Only the same registered policy instance on the actual endpoint may write. Error input strings are **codes**, resolved to the policy's exact field/message allowlist; arbitrary input/exception text and hidden fields, including empty message arrays, never echo. Limits are 32 fields, four messages per field, 256 characters per message and actual serialized UTF-8 ≤16KiB. Any invalid/overflowing output becomes a complete generic JSON containing safe code/trace/correlation; it is not truncated. Trace is a valid nonzero Activity 32-hex identifier or generated; correlation uses the bounded safe key syntax or is regenerated. No detail/instance is emitted.

ResponseStarted/RequestAborted returns a typed non-write result without appending a second body. PolicyNotBound also writes nothing. Hosts must handle these results by their existing abort/low-sensitive logging flow, never invent a completed successful response. The new writer bypasses arbitrary global ProblemDetails customization to keep its measured serialized bytes authoritative.

## Compatibility and evidence scope

Existing `Cp6HttpResilienceProfile`, old overload behavior/defaults, gateway headers, authentication, CP6 definitions and old ProblemDetails writer remain intact. The only old source edit is the additive safe-policy overloads. CRM about:blank/business codes, OIDC 10-second timeout, redirect/cookie choices, CSRF/session/Origin behavior and all actual Host registrations remain Owner-owned and unchanged. No routing/forwarded-header trust, tenant isolation, distributed rate-limit or production receipt guarantee is inferred from these tests.

Tests are under `tests/CP6.Platform.AspNetCoreTests/HttpV2/`; immutable run logs/TRX and exact SDK/source/dependency/artifact manifests are under `docs/development/evidence/WP02-11/implementation/`. Required local gate filters include all HttpV2 tests; unchanged HttpResilience/GatewayContract/AuthenticationContract tests; and ArchitectureTests. Failed parser/regression runs are retained. The initial parser had preimplementation behavioral RED; other modules were drafted before their complete suites, with subsequent actual failure-first boundary regressions. This is not a claim of uniform per-behavior TDD or production acceptance. The parent delivery report maps the original 42 AC identities to library subassertions and unverified Host/Owner requirements.
