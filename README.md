# Santander Backend Developer Coding Test

This project is a RESTful ASP.NET Core API designed to return the highest-ranked *n* Hacker News stories based on their score.

## Running the Service

**Requirements:**
- .NET 10 SDK (`10.0.301` or newer)

```bash
# From the repository root
dotnet run --project src/CodingTest.Api
```

By default, the API listens on `http://localhost:5149` and `https://localhost:7194`. Visiting the root path redirects to the OpenAPI document, which is the main entry point for this service rather than a front-end UI.

### Example request

```
GET /api/stories/best?n=10
```

This returns an array containing the best *n* stories, ordered from highest score to lowest:

```json
[
  {
    "title": "A uBlock Origin update was rejected from the Chrome Web Store",
    "uri": "https://github.com/uBlockOrigin/uBlock-issues/issues/745",
    "postedBy": "ismaildonmez",
    "time": "2019-10-12T13:43:01+00:00",
    "score": 1716,
    "commentCount": 572
  }
]
```

### OpenAPI / Swagger

The OpenAPI specification is available at `/openapi/v1.json` in every environment. This URL can be imported into tools such as Postman, Scalar, Swagger UI, or any client that supports the OpenAPI standard.

## Running Tests

```bash
dotnet test
```

## Design Choices and Assumptions

The solution follows a lightweight Clean Architecture approach, with a single Core project responsible for the application and domain logic. The implementation instead prioritises maintainability, testability, resilience, and scalability in a way that maps more directly to the actual problem being solved.

### Caching strategy: a mix of caching and proactive background refresh

The goal is to reduce unnecessary load on the Hacker News API during periods of higher traffic:

| Layer | Cached data | TTL |
|---|---|---|
| Best-story ID list | `int[]` from `beststories.json` | 5 min |
| Individual story details | Per-story `StoryResponse` | 10 min |

A **`CacheRefreshBackgroundService`** wakes up every 4 minutes (this is configurable) and prewarms the cache for the top *N* stories, using the default value of 200. In practice, most incoming requests hit a warm cache, and the external service is only touched when the application starts or when timing lines up in an unusual way.

A **`SemaphoreSlim`** with a default concurrency of 20 limits outbound requests to Hacker News. That helps prevent a burst of cache misses from turning into a burst of external calls, which would otherwise create a classic thundering-herd problem. The semaphore is owned by the singleton `StoryService`, so the cap applies across all in-flight HTTP work.

Double-checked locking is also used when fetching the ID list so that only one thread can trigger a real request for that resource at a time.

### Resilience: Polly circuit breaker and stale-data fallback

The `HackerNewsClient` uses an **`AddStandardResilienceHandler`** pipeline from `Microsoft.Extensions.Http.Resilience` (Polly v8):

| Strategy | Configuration | Purpose |
|---|---|---|
| Attempt timeout | 10 s | Stops a single hung HTTP request |
| Retry | 2 attempts, exponential backoff + jitter, 200 ms base | Handles short-lived HN instability |
| **Circuit breaker** | ≥ 50 % failure ratio, 30 s sampling window, min 5 calls, 30 s break | Prevents the app from repeatedly hammering HN during outages |
| Total request timeout | 30 s | Sets a strict overall deadline for all attempts |

**Circuit breaker states:**
- **Closed** - normal operation; failures are counted within a rolling time window.
- **Open** - the breaker trips when the failure ratio exceeds the configured threshold. All subsequent calls are rejected immediately rather than making an HTTP request and waiting another 30 seconds. This keeps the request queue from piling up when the upstream service is unavailable.
- **Half-open** - after the break interval expires, a single probe request is allowed through. If it succeeds, the circuit closes again; if it fails, the cooldown timer is reset.

**Stale-data fallback:**
Each successful assembly of the story list stores a process-lifetime backup in `IMemoryCache` using `CacheItemPriority.NeverRemove` and no expiry. When the circuit is open and the live TTL cache has already expired, `StoryService` catches the `BrokenCircuitException` and serves the stale backup instead of returning a 500. The controller adds:

```
X-Cache-Status: stale
```

The `beststories.json` endpoint yields a maximum of 200 IDs. Any request asking for more than that is rejected with a 400 status code.

### Score ordering

The order supplied by `beststories.json` reflects the ranking logic used by Hacker News, which can incorporate factors such as recency and engagement, rather than a simple descending score sort. The service therefore fetches the full candidate set, sorts it by score, and only then takes the top *n* results. Truncating before sorting would risk dropping a story that should genuinely appear in the true top-*n* set.

### `uri` may be `null` for text-only posts

Some Hacker News stories do not have an external link, such as “Ask HN” or “Show HN” posts. In these cases, the source data has no `url` field because there is simply no destination to point to. This API passes that through unchanged: `uri` is `null` for those entries rather than inventing a fallback. Consumers should treat `uri` as optional.


