# Dashboard RIMAPI Probe Cache

## Problem

The dashboard now polls `/api/status` every 5 seconds and `/api/system/health` every 15 seconds, but both endpoints independently call `RimApiRuntimeProbe.ProbeAsync`, which reads RIMAPI `api/v1/game/state`. When the Host is up but RIMAPI is down or slow, these endpoints still return successful Host responses with offline metadata. Two distinct probe-side costs follow: (a) nearby status and health requests repeat the same `api/v1/game/state` probe, and (b) a live Host with dead RIMAPI keeps hammering localhost on every poll. This slice targets (a) and (b). Dashboard-side polling backoff and response shapes are out of scope and stay unchanged.

## Goal

Keep the dashboard status responsive while making RIMAPI reachability probing shared, bounded, and quiet under failure. Do not change endpoint response shapes.

## Suggested Slice

1. Add a singleton `RimApiRuntimeProbeCache` in `Src/ApiHost` that owns the latest `RimApiRuntimeSnapshot`, fetched timestamp, and one in-flight refresh task.
2. Keep `RimApiRuntimeProbe` transient so the typed `RimApiClient` lifetime stays safe. The singleton cache takes `IServiceScopeFactory` (not the root `IServiceProvider`) and creates a short scope only when it needs a fresh probe, so the transient probe is never captured by the singleton. To keep the cache's TTL/coalescing logic unit-testable without standing up DI, route the actual probe through an injectable delegate seam `Func<CancellationToken, Task<RimApiRuntimeSnapshot>>`; production wires it to `scope.ServiceProvider.GetRequiredService<RimApiRuntimeProbe>().ProbeAsync`, tests pass a counting fake.
3. Use separate TTLs: healthy snapshots can be reused briefly, around 2 seconds, to collapse near-simultaneous `/api/status` and `/api/system/health` calls; offline/error snapshots can be reused longer, around 5 seconds, so a live Host with dead RIMAPI does not keep hammering localhost. Read the clock through an injected `TimeProvider` (default `TimeProvider.System`) rather than `DateTimeOffset.UtcNow`, so the TTL-expiry test can advance a `FakeTimeProvider` deterministically instead of sleeping.
4. Coalesce concurrent callers: if a probe is already running, await the same task rather than starting another `api/v1/game/state` request. Guard the read-decide-start of the shared task field under a lock (or `Lazy<Task>`) so two arrivals cannot both start a probe. This coalescing is safe only because `ProbeAsync` never throws — it returns an `Offline` snapshot on every failure; if that swallow-all contract ever changes, a faulted shared task would poison every awaiter and could be cached, so revisit this then.
5. Do not let a browser request abort cancel the shared probe for all callers. Producer side: run the shared probe under a token linked to `IHostApplicationLifetime.ApplicationStopping` plus the existing probe-level 2 second timeout, never the per-request token. Consumer side: a caller's `ct` firing must not fault or cancel the shared task for the other awaiters. Because `ProbeAsync` is already bounded to ~2 seconds and never throws, the chosen contract is: `GetAsync(ct)` ignores the caller `ct` while awaiting the shared task (bounded wait at most the probe timeout). State this contract on the cache so a future reader does not "fix" it by threading the request token into the shared probe.
6. Replace endpoint injections in `StatusEndpoints` and `SystemEndpoints` with the cache and call `GetAsync(ct)`.
7. Add unit tests, using the delegate seam from step 2 and a fake or local manual `TimeProvider` from step 3:
   - cached healthy reuse within TTL invokes the fake probe once;
   - cached offline reuse within the longer offline TTL invokes once;
   - TTL expiry triggers a fresh probe after advancing the fake clock past the TTL;
   - N concurrent callers coalesce to a single probe invocation;
   - a caller whose `ct` is cancelled does not cancel or fault the shared probe observed by the other callers (the step-5 invariant).
8. Update `Docs/design/dashboard.md` to say runtime reachability metadata is shared/cached briefly behind the status and health endpoints.

## Verification

Run targeted backend tests for the new cache and existing `RimApiRuntimeProbeTests`, then run `dotnet build Src/RimBob.sln --no-restore`. If the dashboard contract text changes only, no dashboard rebuild is needed for this backend-only slice.
