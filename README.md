# WebhookHub

A production-grade webhook notification service built with .NET 10. WebhookHub allows internal business applications to publish events that are reliably delivered as signed HTTP POST requests to registered subscriber callback URLs — with automatic retry, dead-lettering, escalation notifications, and full audit trails. It ships with a Blazor WebAssembly portal for subscribers and administrators.

Open source and free to use.

---

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Features](#features)
- [Tech Stack](#tech-stack)
- [Getting Started](#getting-started)
- [Web Portal](#web-portal)
- [Service Client Onboarding](#service-client-onboarding)
- [Delivery Pipeline](#delivery-pipeline)
- [Background Workers](#background-workers)
- [Security](#security)
- [Health Checks](#health-checks)
- [API Reference](#api-reference)
- [Testing](#testing)
- [Project Structure](#project-structure)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [License](#license)

---

## Overview

WebhookHub sits between your internal services and your subscribers. An internal application raises an event — WebhookHub validates it, fans it out to every active subscription registered for that event type, and handles the full delivery lifecycle: retry on failure, escalation emails when an endpoint is slow, dead-lettering when all retries are exhausted, and admin-initiated manual retry for dead-lettered deliveries.

```
Internal Service (onboarded with ClientId + ClientKey)
      │
      ▼
POST /api/WebhookEvent
X-Client-Id: order-service-prod
X-Client-Key: <raw key>
      │
      ▼
Client credential validation → Event type authorization
      │
      ▼
Validation → Persist (Pending) → Write to EventRaised channel
      │                       │
      │                       ▼
      │             EventRaisedWorker ◄──────────────┐
      │                       │                      │
      │                       │       PendingRaisedEventsWorker
      │                       │       (re-queues events still Pending
      │                       │        past the threshold)
      │                       ▼
      │          Create one delivery per active subscription
      │                       │
      │                       ▼
      │      WebhookDeliveryProcessorWorker (first attempt, two-phase claim)
      │                       │
      │       ┌───────────────┴───────────────┐
      │       ▼                               ▼
      │  Delivered                         Failed ◄──── StaleClaimedDeliverReleaseWorker
      │                                       │         (releases claims whose lock expired)
      │                                       ▼
      │                        RetryPendingDeliveriesWorker
      │                                       │
      │                            ┌──────────┴──────────┐
      │                            ▼                     ▼
      │                      Delivered            Max attempts exceeded
      │                                                  │
      │                                                  ▼
      │                                         Dead Letter Queue
      │                                                  │
      │                                                  ▼
      │                                          Escalation Email
      │                                                  │
      │                                                  ▼
      └────────────────────────────────────── Admin Manual Retry (back to Pending)
```

---

## Architecture

WebhookHub is a .NET solution (`Webhook.slnx`) with three back-end projects, a Blazor front end and one test project:

```
WebHook.Api             — ASP.NET Core 10 Web API
                          Controllers, filters, middleware, service registration, health endpoints
WebHook.Core            — Domain entities, DTOs, interfaces, constants, mappers, configuration models
WebHook.Infrastructure  — EF Core (PostgreSQL), Identity, background workers, services, security,
                          health checks, migrations
src/Webhook.Web         — Blazor WebAssembly portal for subscribers and administrators
tests/WebHook.Tests     — Unit and integration tests (xUnit, Moq, Testcontainers PostgreSQL,
                          WebApplicationFactory)
```

All public types in Core, Infrastructure and Api carry XML documentation comments, and the build generates the documentation files so they show in IntelliSense and the OpenAPI document.

---

## Features

### Service Client Onboarding
- Internal services are onboarded by an Admin before they can publish events
- Each client is issued a unique `ClientId` (Admin-defined) and a cryptographically random `ClientKey` shown once at onboarding and stored only as a PBKDF2 hash
- Clients present `X-Client-Id` and `X-Client-Key` headers on every publish request
- Each client is authorised to publish only the event types assigned to it — attempts to publish unauthorised event types are rejected with `403 Forbidden`
- Admins can assign or remove event catalog entries per client at any time without re-onboarding
- Client keys can be rotated on demand — the previous key is invalidated immediately
- Clients can be deactivated and reactivated. Deactivation also removes the client's event type assignments; they are not restored on reactivation

### Event Publishing
- Internal services raise events via a simple POST endpoint — authenticated by `ClientId` and `ClientKey`
- Events are validated against a catalog of known event types with declared field schemas
- JSON payloads are validated against the catalog schema at publish time — missing or invalid fields are rejected with a descriptive error identifying each failing field
- Correlation IDs group related events from a single business transaction; the correlation ID and event type are unique together, so the same event cannot be raised twice

### Reliable Delivery
- Automatic fan-out to every active subscription registered for the raised event type
- **Two-phase claim/process pattern** — deliveries are locked with a worker identity and expiry timestamp before any HTTP attempt, preventing duplicate delivery when multiple worker instances run concurrently
- Linear retry back-off with computed `NextRetryAt` timestamps
- Request timeouts are recorded as failed attempts (`408`) without interrupting the rest of the batch
- Slow endpoint detection — if an attempt takes longer than the configured threshold, a notification email is sent to the subscription contact
- Dead-lettering when a delivery exhausts its attempts for the current retry cycle
- Escalation email to the subscription contact on dead-letter transition
- Admin-initiated manual retry for dead-lettered deliveries, with a justification recorded for audit and a configurable limit on retry cycles

### Event Recovery
- If an event is persisted but the `EventRaisedWorker` never processes it — due to a crash, restart, or a lost channel message — the `PendingRaisedEventsWorker` periodically finds events still in `Pending` status beyond a configured threshold and writes them back to the channel, so no published event is silently lost

### Stale Delivery Recovery
- A dedicated background worker finds deliveries still `Processing` well after their `LockedUntil` timestamp — indicating a crashed worker — and releases them as `Failed` so the retry worker picks them up

### Webhook Signatures
- Every delivery includes `X-Webhook-Timestamp` (Unix seconds), `X-Webhook-Event` and `X-Webhook-Signature: sha256=<hmac>` headers
- The signature is `HMAC-SHA256("{timestamp}.{body}")`, keyed with the UTF-8 bytes of the subscription secret, so a captured request cannot be replayed with a new timestamp
- The secret is shown on the subscription's detail page (`GET` by ID); it is never included in list responses and is rotated when a subscription is reactivated
- Secrets are encrypted at rest with AES-GCM

To verify a delivery, a subscriber should:
1. Read the raw request body and the `X-Webhook-Timestamp` and `X-Webhook-Signature` headers.
2. Compute `"sha256=" + lowercase_hex(HMAC_SHA256(key = utf8(secret), message = utf8(timestamp + "." + body)))`.
3. Compare it with `X-Webhook-Signature` in constant time.
4. Reject the request if the timestamp is more than a few minutes from the current time.

### Identity and Access
- JWT authentication with rotating refresh tokens (refresh before the access token expires)
- OTP flow for sensitive self-service operations — password reset and self-deactivation require a verified OTP and a short-lived, purpose-bound operation token sent in the `X-Operation-Token` header
- Admin bypass — Admins can deactivate accounts directly without an OTP token
- Role-based access — `User` and `Admin` roles enforced per endpoint
- Data scoping — users only see their own subscriptions, deliveries, delivery attempts and dead-letter entries; admins see everything
- Custom JTI cache validation on every authenticated request — revoked tokens are rejected immediately without waiting for expiry
- Rate limiting on sensitive endpoints — OTP request and validation are limited per email address, and selected endpoints are limited per user

### Event Catalog Management
- Admins define subscribable event types with their available field schemas (`string`, `int`, `bool`, `guid`, `datetime`, `double`, `decimal`, `float`)
- Event types can be activated or deactivated without deletion
- Subscribers choose which events to subscribe to per subscription

### Dashboards and Reporting
- Admin dashboard — platform summary, most recent events, most-raised event types and top failing subscriptions
- User dashboard — the user's recent deliveries, top failing subscriptions and a summary for a chosen date range
- Trend analytics — in progress and currently hidden in the portal

---

## Tech Stack

| Concern | Technology |
|---|---|
| Runtime | .NET 10 |
| Web framework | ASP.NET Core 10 |
| Front end | Blazor WebAssembly |
| ORM | EF Core 10 with Npgsql |
| Database | PostgreSQL |
| Identity | ASP.NET Core Identity |
| Authentication | JWT Bearer + custom `IAsyncAuthorizationFilter` |
| Background workers | `BackgroundService` / `IHostedService` |
| In-process messaging | `System.Threading.Channels` (bounded) |
| Payload signing | HMAC-SHA256 |
| Secret encryption | AES-GCM |
| Data protection | ASP.NET Core Data Protection |
| Caching | `IMemoryCache` |
| Logging | Serilog |
| Rate limiting | ASP.NET Core `RateLimiter` middleware |
| API documentation | OpenAPI + Scalar (`/scalar/v1`, Development) and ReDoc (`/webhookapi/api-docs`) |
| Tests | xUnit, Moq, Testcontainers PostgreSQL, `WebApplicationFactory` |

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [PostgreSQL 14+](https://www.postgresql.org/download/)
- [Docker](https://www.docker.com/) — only needed to run the tests

### Environment Variables

| Variable | Required | Purpose |
|---|---|---|
| `webhook_secret_key` | Yes | JWT signing key — at least 32 characters |
| `env_webhook_encrypt_key` | Yes | Hex-encoded AES key (16, 24 or 32 bytes) used to encrypt subscription signing secrets |
| `env_webhook_encrypt_iv` | Only for existing data | Hex-encoded IV used by the previous AES-CBC format. Needed only while secrets encrypted before the AES-GCM change remain in the database |
| `SmtpClientPassword` | Yes, to send email | Password for the SMTP account in `EmailSmtpSettings` |

Keep these out of source control. In production, prefer a secrets manager.

### Configuration (`appsettings.json`)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DbConnection": ""
  },
  "CorsPolicy": {
    "AllowedMethods": "POST,GET,PUT,DELETE,OPTIONS",
    "AllowedOrigins": "https://localhost:<portal-port>",
    "AllowedHeaders": "X-Operation-Token"
  },
  "SignatureSecretKey": {
    "KeySize": 32
  },
  "WebhookDeliveryWorker": {
    "DeliveryProcessorIntervalSeconds": 60,
    "TotalBatchSize": 10,
    "DeliveryLockDuration": 300,
    "ThresholdDuration": 125000
  },
  "RetryDeliveriesAfterFailed": {
    "ThresholdDuration": 125000,
    "MaximumAttendedCount": 5,
    "TotalBatchSize": 10,
    "DeliveryLockDuration": 300,
    "StaleDeliveryReleaseIntervalSeconds": 120,
    "RetryFailedDeliveryIntervalSeconds": 150
  },
  "EventRaisedWorker": {
    "ProcessingIntervalInSeconds": 5
  },
  "EmailSmtpSettings": {
    "Host": "",
    "Port": 587,
    "Username": "",
    "SenderEmail": ""
  },
  "EmailProcessorWorker": {
    "ProcessingIntervalInSeconds": 10,
    "ProcessingDelayInMilliSeconds": 4500
  },
  "PendingRaisedEventsWorker": {
    "PendingEventsWorkerIntervalSeconds": 300,
    "PendingEventsThresholdMinutes": 30
  },
  "DeadLetterManualRetry": {
    "MaximumRetryCycle": 3
  },
  "UserSettingsConfiguration": {
    "MinimumPasswordLength": 10,
    "MaximumAuthenticationAttempt": 3
  },
  "JwtSettings": {
    "ValidIssuer": "https://localhost:<api-port>",
    "ValidAudiences": "https://localhost:<portal-port>",
    "TokenExpirationAfterInSeconds": 600,
    "RefreshTokenExpirationAfterInSeconds": 60000
  },
  "TokenValidation": {
    "OtpExpirationAfterInSeconds": 1200.00,
    "OtpOperationTokenExpiresAFterInSceonds": 1200.00
  },
  "OtpSettings": {
    "MaximumOtpLength": 12,
    "OtpToGenerateLength": 6
  }
}
```

Notes:
- Durations named `...Seconds` are in seconds; `DeliveryLockDuration` is in seconds and `ThresholdDuration` is in milliseconds.
- `MaximumAttendedCount` is the number of attempts allowed per retry cycle.
- `JwtSettings:ValidAudiences` is a `;`-separated list. The caller's `Origin` header must match one of them to sign in, so it must include the portal's origin.

### Apply Migrations

WebhookHub does not run `Database.Migrate()` at startup. Migrations are scripted and applied before deployment:

```powershell
dotnet ef migrations script <LastAppliedMigration> <NewMigration> `
    --project WebHook.Infrastructure `
    --startup-project WebHook.Api `
    --idempotent `
    --output ./scripts/delta.sql
```

Run the generated script against your PostgreSQL database, then start the application. The analytics dashboards depend on database views and a function created by migrations, so apply every migration, including the SQL-only ones.

### Run the API

```bash
dotnet run --project WebHook.Api
```

API documentation is available at:

```
https://localhost:<api-port>/scalar/v1             (Development only)
https://localhost:<api-port>/webhookapi/api-docs   (ReDoc)
```

---

## Web Portal

`src/Webhook.Web` is a Blazor WebAssembly app that calls the API.

| Area | Pages |
|---|---|
| Authentication | Login, register, request OTP, validate OTP, reset password |
| User | Dashboard, subscriptions (list, create, detail with signing secret), deliveries and attempts, dead letters |
| Admin | Dashboard, users, event catalog, service clients, health |
| Trend analytics | In progress; hidden from the navigation for now |

The API base URL is read at startup from `wwwroot/environment/config.json`:

```json
{
  "AppBaseUrl": "https://localhost:<api-port>/",
  "ApiTimeOutInSeconds": 30
}
```

Run it with:

```bash
dotnet run --project src/Webhook.Web
```

Make sure the portal's origin is listed in both `CorsPolicy:AllowedOrigins` and `JwtSettings:ValidAudiences`.

---

## Service Client Onboarding

Before an internal service can publish events it must be onboarded by an Admin. Onboarding establishes the service's identity and defines which event types it is permitted to publish.

### Onboarding a New Client

```
POST /api/WebhookServiceClients
Authorization: Bearer <admin-token>

{
  "serviceName":       "Order Management Service",
  "clientId":          "order-service-prod",
  "contactEmail":      "platform@company.com",
  "allowedEventTypes": ["OrderCreated", "OrderCancelled"]
}
```

The `clientId` must be unique, lowercase alphanumeric with hyphens only, and follow the convention `{service-name}-{environment}` — for example `order-service-prod` or `payment-gateway-staging`. Every event type in `allowedEventTypes` must exist and be active.

The response includes the raw `ClientKey` — **store it securely, it will not be shown again**:

```json
{
  "clientId":  "order-service-prod",
  "clientKey": "<raw-key>",
  "message":   "Store your ClientKey securely. It will not be shown again."
}
```

### Publishing Events

The onboarded service passes its credentials in request headers:

```
POST /api/WebhookEvent
X-Client-Id:  order-service-prod
X-Client-Key: <raw key>
Content-Type: application/json

{
  "eventType":     "OrderCreated",
  "payLoad":       "{ ... }",
  "source":        "order-service-prod",
  "correlationId": "..."
}
```

The system validates the credentials and checks the event type is in the client's authorised catalog before proceeding. Any attempt to publish an unauthorised event type is rejected with `403 Forbidden`.

### Key Rotation

If a key is lost or compromised, request a new one:

```
PUT /api/WebhookServiceClients/request-new-key
Authorization: Bearer <admin-token>

{
  "serviceName": "Order Management Service",
  "clientId":    "order-service-prod"
}
```

The response contains the new raw key. The previous key is invalidated immediately, so the consuming service must update its stored key before making further publish requests.

### Managing Catalog Assignments

Event type permissions can be adjusted after onboarding without re-issuing credentials. `{id}` is the service client's internal ID:

```
# Add a new event type (restores a previously removed assignment if there is one)
POST /api/WebhookServiceClients/{id}/eventcatalogs?catalogName=OrderShipped

# Remove an event type
DELETE /api/WebhookServiceClients/{id}/eventcatalogs?catalogName=OrderCancelled
```

### CORS and Service-to-Service Calls

Browser-based callers (the web portal) are restricted by the CORS allowlist — only origins registered in `CorsPolicy:AllowedOrigins` can make cross-origin requests from a browser. Server-to-server callers (internal services publishing events) are not subject to CORS — the `X-Client-Id` / `X-Client-Key` credential pair is the authentication boundary for those callers.

---

## Delivery Pipeline

The delivery pipeline is split across multiple background workers communicating through an in-memory channel and the database.

### Statuses

| Event status | Meaning |
|---|---|
| `Pending` | Persisted; deliveries not yet created |
| `Processing` | Deliveries created for every active subscription (the event stays in this status) |
| `Processed` | Fanned out, but there were no active subscribers |

| Delivery status | Meaning |
|---|---|
| `Pending` | Waiting for its first attempt (or re-queued by an admin manual retry) |
| `Processing` | Claimed and locked by a worker; an HTTP attempt is in progress |
| `Delivered` | The subscriber returned a 2xx response |
| `Failed` | The last attempt failed; waiting for the retry worker |
| `DeadLetter` | Attempts for the current retry cycle exhausted |

### Step 1 — Event Publishing

An onboarded internal service calls `POST /api/WebhookEvent` with `X-Client-Id` and `X-Client-Key` headers alongside the event payload.

### Step 2 — Validation and Persistence

The system:
1. Validates the `ClientId` and `ClientKey` against the stored, hashed client key
2. Checks the event type is in the client's authorised catalog
3. Checks the correlation ID is unique for the event type
4. Validates the JSON payload against the catalog's declared field schema — returns `400` with each failing field named if validation fails
5. Persists the event with status `Pending`
6. Writes the new event ID to the `EventRaised` in-memory channel

### Step 3 — Fan-out (EventRaisedWorker)

The `EventRaisedWorker` reads the channel. For each event ID it:

1. Claims the event inside a transaction with `SELECT … FOR UPDATE SKIP LOCKED`, only while it is still `Pending`
2. Finds every **active** subscription linked to the event type
3. Creates a `WebhookDelivery` with status `Pending` for each one
4. Marks the event `Processing` (or `Processed` if there were no subscribers) and commits

Events that fail to process are written back to the channel.

### Event Recovery (PendingRaisedEventsWorker)

The in-memory channel is not durable — if the application restarts or the `EventRaisedWorker` crashes after an event was persisted but before it was read, the event ID is lost from the channel while the event remains `Pending` in the database.

The `PendingRaisedEventsWorker` guards against this. On a configurable interval (default 300 seconds) it finds events that are still `Pending` and older than the configured threshold (default 30 minutes), and writes their IDs back to the channel for the `EventRaisedWorker` to fan out.

### Step 4 — First Attempt (WebhookDeliveryProcessorWorker)

The `WebhookDeliveryProcessorWorker` polls on a configurable interval:

1. **Claim** — in a transaction, selects up to `TotalBatchSize` `Pending` deliveries with no retry scheduled and no live lock (`FOR UPDATE SKIP LOCKED`), marks them `Processing` with `LockedBy = workerId` and `LockedUntil = now + DeliveryLockDuration`, and commits
2. **Process** — for each claimed delivery, decrypts the subscription secret and sends an HTTP POST with the signature headers
3. **Record** — saves a `WebhookDeliveryAttempt` with the response body, HTTP status code, duration and timestamp

**On success (2xx):** marks the delivery `Delivered` and clears the lock.

**On failure (non-2xx, connection error, or timeout):** increments `RetryCount`, sets status `Failed`, computes `NextRetryAt` and clears the lock.

**Slow endpoint:** if the attempt took longer than `ThresholdDuration` (milliseconds), a slow-endpoint email is queued to the subscription contact.

### Step 5 — Retry Processing (RetryPendingDeliveriesWorker)

The `RetryPendingDeliveriesWorker` claims `Failed` deliveries whose `NextRetryAt` has passed (or is unset, as after a stale release) using the same two-phase claim, and makes another attempt.

- **Back-off** is linear: after the *n*-th failed attempt the next retry is scheduled *n + 1* minutes later.
- **Dead-lettering** — when a delivery is still failing and its `RetryCount` exceeds `MaximumAttendedCount × RetryCycle`, it is marked `DeadLetter`, a dead-letter entry is created, and an escalation email is queued to the subscription contact.

### Step 6 — Stale Claim Recovery (StaleClaimedDeliverReleaseWorker)

On a configurable interval this worker finds deliveries still `Processing` whose lock expired more than `DeliveryLockDuration` ago — indicating the claiming worker crashed — clears the lock, increments `RetryCount` and sets them to `Failed` so the retry worker picks them up.

### Dead Letter Manual Retry

An Admin may request a manual retry for a dead-lettered delivery (`POST /api/WebhookDelivery/{deliveryId}/deadLetters`):

1. The dead-letter entry must not have been retried already, and the delivery must still be `DeadLetter`
2. The delivery's retry cycle must be below `MaximumRetryCycle`
3. The justification, time and admin are recorded on the dead-letter entry
4. The retry cycle is incremented — granting another full set of automatic attempts — the lock and `NextRetryAt` are cleared, and the delivery is set back to `Pending` for the first-attempt worker

---

## Background Workers

| Worker | Trigger | Responsibility |
|---|---|---|
| `EventRaisedWorker` | Channel | Fans events out into delivery records |
| `PendingRaisedEventsWorker` | DB poll | Re-queues events still `Pending` beyond the threshold |
| `WebhookDeliveryProcessorWorker` | DB poll | Claims `Pending` deliveries and makes the first attempt |
| `RetryPendingDeliveriesWorker` | DB poll | Retries `Failed` deliveries and dead-letters those out of attempts |
| `StaleClaimedDeliverReleaseWorker` | DB poll | Releases deliveries whose claim has expired, returning them to `Failed` |
| `EmailProcessorWorker` | Channel | Sends queued emails — OTPs, slow-endpoint and dead-letter notifications. Remaining emails are flushed on shutdown |

### Two-Phase Claim Pattern

The delivery workers use a two-phase claim rather than optimistic locking. Optimistic locking detects conflicts after the fact but does not prevent multiple workers from attempting the same delivery concurrently. The two-phase approach:

1. **Claim** — `SELECT … FOR UPDATE SKIP LOCKED` inside a transaction, so concurrent workers skip rows already being claimed, then set `Processing`, `LockedBy` and `LockedUntil` and commit
2. **Process** — only the worker that holds the claim makes the HTTP call
3. **Release** — update status to `Delivered` or `Failed` and clear the lock fields

If a worker crashes between claim and release, `StaleClaimedDeliverReleaseWorker` detects the expired lock and returns the delivery to `Failed` for the retry worker to pick up.

---

## Security

| Mechanism | Implementation |
|---|---|
| JWT signing | HMAC-SHA256, key from the `webhook_secret_key` environment variable; the token audience is the caller's origin |
| Token revocation | The JTI of each user's current token is held in `IMemoryCache`; the filter rejects any request whose JTI is missing or mismatched. Changing a user's role evicts it |
| Refresh tokens | Random 32-byte values, rotated on every login and refresh |
| OTPs and operation tokens | OTPs are hashed; operation tokens are protected with ASP.NET Core Data Protection, hashed, purpose-bound and single-use |
| Service client credentials | `ClientKey` generated with a cryptographic RNG and stored as a PBKDF2 hash; validated on every publish request |
| Service client authorisation | Each client is restricted to publishing only its assigned event types |
| Subscription secrets | Encrypted at rest with AES-GCM (random nonce per value, tamper-detecting); legacy AES-CBC values are still readable |
| Webhook signatures | `X-Webhook-Signature: sha256=HMAC("{X-Webhook-Timestamp}.{payload}", subscriberSecretKey)` |
| Password hashing | ASP.NET Core Identity default (PBKDF2) |
| Account lockout | Configurable max failed attempts, then lockout until cleared by a password reset or reactivation; deactivated accounts are locked out |
| Data scoping | Non-admin users can only read their own subscriptions, deliveries, attempts and dead-letter entries; other users' records return `404` |
| Rate limiting | `request-otp-limit` — 3 per 10 minutes per email; `validate-otp-limit` — 5 per 10 minutes per email; `per-user-rating` — 10 per minute per user (or IP when anonymous) |

### Custom Authentication Filter

Every authenticated request passes through `CustomAuthenticationFilter`, an `IAsyncAuthorizationFilter` that runs globally:

1. Endpoints marked `[AllowAnonymous]` — filter exits immediately
2. `Authorization: Bearer <token>` header must be present
3. JTI claim is extracted from `HttpContext.User` (populated by the authentication middleware)
4. JTI is looked up in the cache — must be non-default and match the claim
5. Any failure returns `401 Unauthorized`

This ensures that revoking a user's session (by evicting their JTI from the cache) takes effect immediately — their token is rejected on the next request even if it has not yet expired.

### Deployment Notes

- The token cache, the in-memory channels and the default Data Protection key storage are all per process. Run a single API instance, or move them to shared infrastructure (for example Redis and a persisted Data Protection key ring) before scaling out.
- Keep `env_webhook_encrypt_key` outside the database — a database leak alone then reveals no subscription secrets.

---

## Health Checks

Two endpoints are available:

| Endpoint | Auth | Response |
|---|---|---|
| `GET /Admin/_health` | Public | Summary JSON; `503` when any check is `Unhealthy` |
| `GET /admin/_healths` | Admin | Detailed HealthChecks UI JSON |

| Check | What it monitors |
|---|---|
| `POSTGRES DB HEALTH CHECK` | Primary database connectivity |
| `IN MEMORY SYSTEM CACHE HEALTH CHECK` | Cache can store, read and remove a value |
| `SERVER DISK SPACE HEALTH CHECK` | Free space on the C: drive — `Degraded` when low |
| `QUEUED EMAIL HEALTH CHECK` | Email channel backlog — `Unhealthy` above 12 queued emails |
| `QUEUED RAISED EVENTS HEALTH CHECK` | Raised event channel backlog — `Unhealthy` above 10 queued events |
| `DEAD LETTER DELIVERIES HEALTH CHECK` | Dead-letter entries awaiting manual retry — `Unhealthy` above 10 |
| `PENDING DELIVERIES HEALTH CHECK` | Count of `Pending` deliveries (informational) |
| `WORKER LIVELINESS HEALTH CHECK` | Time since the last worker heartbeat — `Degraded` after 45 seconds |
| `STALE DELIVERY HEALTH CHECK` | `Processing` deliveries past their lock — `Degraded` when any exist |

The delivery, retry, stale-release and pending-event workers update a shared `WorkerLivenessTracker` singleton as they run, so the liveliness check stays healthy while any of them is beating.

Example response from `GET /Admin/_health`:

```json
{
  "status": "Healthy",
  "checks": [
    { "name": "POSTGRES DB HEALTH CHECK",            "status": "Healthy", "duration": 12.3, "exception": null },
    { "name": "IN MEMORY SYSTEM CACHE HEALTH CHECK", "status": "Healthy", "duration": 0.4,  "exception": null },
    { "name": "SERVER DISK SPACE HEALTH CHECK",      "status": "Healthy", "duration": 0.2,  "exception": null },
    { "name": "QUEUED EMAIL HEALTH CHECK",           "status": "Healthy", "duration": 0.1,  "exception": null },
    { "name": "QUEUED RAISED EVENTS HEALTH CHECK",   "status": "Healthy", "duration": 0.1,  "exception": null },
    { "name": "DEAD LETTER DELIVERIES HEALTH CHECK", "status": "Healthy", "duration": 1.2,  "exception": null },
    { "name": "PENDING DELIVERIES HEALTH CHECK",     "status": "Healthy", "duration": 1.1,  "exception": null },
    { "name": "WORKER LIVELINESS HEALTH CHECK",      "status": "Healthy", "duration": 0.0,  "exception": null },
    { "name": "STALE DELIVERY HEALTH CHECK",         "status": "Healthy", "duration": 0.9,  "exception": null }
  ],
  "totalDurationMs": 16.9
}
```

---

## API Reference

Full interactive documentation is available at `/scalar/v1` when running in Development, and at `/webhookapi/api-docs` (ReDoc).

### Authentication — `api/Authentication`

| Method | Route | Auth | Description |
|---|---|---|---|
| POST | `/login` | Public | Sign in — returns JWT access token and refresh token |
| POST | `/change-password` | Authenticated | Change account password |
| POST | `/request-otp` | Public | Request a one-time password via email |
| POST | `/reset-user-password` | Public + operation token | Reset a password using a `PasswordReset` operation token in `X-Operation-Token` |
| POST | `/refresh` | Public | Exchange an unexpired access token and refresh token for a new pair |
| POST | `/assign-new-role` | Admin | Replace a user's role |

### Users — `api/Users`

| Method | Route | Auth | Description |
|---|---|---|---|
| POST | `/register` | Public | Register a new user account |
| POST | `/deactivate` | Authenticated | Deactivate an account (`DeactivateProfile` operation token required for non-Admin) |
| POST | `/reactivate` | Admin | Reactivate a deactivated account |
| GET | `/` | Admin | List all users, including deactivated ones |
| GET | `/get-user-details` | Authenticated | Get the current user's details |

### OTP Operations — `api/OtpOperation`

| Method | Route | Auth | Description |
|---|---|---|---|
| POST | `/validate-otp` | Public | Validate a one-time password — returns a signed operation token |
| DELETE | `/revoke-otp/{userId}` | Admin | Revoke a user's unused OTPs |

### Event Catalog — `api/WebhookEventCatalog`

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/?includeDeactivated={bool}` | Public | List event catalog entries |
| GET | `/{id}` | Authenticated | Get an active event catalog entry |
| POST | `/` | Admin | Create a new event type |
| PUT | `/{id}?isDeactivate={bool}` | Admin | Activate or deactivate an event type |

### Service Clients — `api/WebhookServiceClients`

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/?includeDeactivated={bool}` | Admin | List onboarded service clients |
| GET | `/{clientId}` | Admin | Get a service client by ClientId |
| POST | `/` | Admin | Onboard a new internal service client — returns ClientKey once |
| DELETE | `/deactivate/{clientId}` | Admin | Deactivate a service client and remove its event type assignments |
| PUT | `/reactivate/{clientId}` | Admin | Reactivate a deactivated service client |
| PUT | `/request-new-key` | Admin | Rotate the ClientKey for a service client |

### Service Client Event Catalogs — `api/WebhookServiceClients/{id}/eventcatalogs`

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/?includeDeactivated={bool}` | Admin | List the event types assigned to a service client |
| POST | `/?catalogName={name}` | Admin | Assign an event type to a service client |
| DELETE | `/?catalogName={name}` | Admin | Remove an event type from a service client |

### Subscriptions — `api/WebhookSubscription`

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/?includeDeactivated={bool}` | Admin | List all subscriptions (secrets not included) |
| GET | `/{id}` | Authenticated | Get a subscription with its signing secret — users only get their own |
| GET | `/get-user-subscriptions?includeDeactivated={bool}` | Authenticated | Get the current user's subscriptions (secrets not included) |
| POST | `/` | User | Create a new subscription |
| PUT | `/{id}` | Authenticated | Reactivate a subscription and rotate its secret |
| DELETE | `/{id}` | Authenticated | Deactivate (soft-delete) a subscription |

### Subscription Events — `api/WebhookSubscription/{subscriptionId}/events`

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/` | Authenticated | List subscribed events for a subscription |
| PUT | `/?eventName={name}` | Authenticated | Subscribe to an event type |
| DELETE | `/?eventName={name}` | Authenticated | Unsubscribe from an event type |

### Webhook Events — `api/WebhookEvent`

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/{correlationId}` | Authenticated | Get events by correlation ID |
| GET | `/` | Admin | Query events with filters |
| POST | `/` | Service Client | Publish a new webhook event — requires `X-Client-Id` and `X-Client-Key` headers |

### Deliveries — `api/WebhookDeliveries`

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/` | User, Admin | Page through deliveries, newest first — filter by date range, status and subscription. Users see only their own |
| GET | `/{deliveryId}` | User, Admin | Get delivery details, including payload and attempt counts |

### Delivery Attempts — `api/WebhookDeliveryAttempts`

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/{deliveryId}` | Authenticated | List the HTTP attempts for a delivery, newest first |

### Dead Letter Queue — `api/WebhookDelivery/{deliveryId}/deadLetters`

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/` | Authenticated | Get dead letter entries for a delivery |
| POST | `/` | Admin | Request manual retry of a dead-lettered delivery |

### Reporting Analytics — `api/ReportingAnalytics`

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/most-recent-events` | Admin | The five most recent events that have deliveries, with delivery counts |
| GET | `/get-event-catalog-analytics` | Admin | The five most-raised active event types |
| GET | `/get-top-failing-subscriptions` | Admin | The five subscriptions with the most failed attempts |
| GET | `/get-admin-dashboard-summary` | Admin | Platform-wide summary counts and success rate |
| GET | `/get-user-delivery-dashboard` | User | The current user's five most recent deliveries |
| GET | `/get-user-top-failing-subscription` | User | The current user's subscriptions with the most failed attempts |
| GET | `/get-user-dashboard-summary?startDate=&endDate=` | User | The current user's summary for an inclusive date range |

### Admin — `Admin`

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/_health` | Public | API and dependency health status |

---

## Testing

All tests live in `tests/WebHook.Tests`. Tests that touch the database use a real PostgreSQL container through Testcontainers, so **Docker must be running**.

### Unit Tests (`UnitTests/`)

Service-level tests covering authentication, users, OTP flows, subscriptions, deliveries, dead letters, analytics, security helpers and filters:

- **xUnit** — test runner
- **Moq** — mocks for `IAuthenticatedUserDetails`, `IDataProtectionProvider`, `ICacheService` and others
- **Testcontainers** — real PostgreSQL container via `IClassFixture<PostgreSqlFixture>`, so EF Core queries, Identity and constraints behave as in production
- Ownership rules are tested for admins, owners and other users

### Integration Tests (`IntegrationTests/`)

- **Controller tests** — `WebApplicationFactory<Program>` with mocked services, covering routing, auth, status code mapping, request forwarding and exception handling. A `TestAuthHandler` carries both `User` and `Admin` roles and seeds the cache mock with a JTI so `CustomAuthenticationFilter` passes
- **Worker tests** — background workers run against a real PostgreSQL container, including concurrent workers competing for the same deliveries

### Running Tests

```bash
# All tests
dotnet test tests/WebHook.Tests

# Unit tests only
dotnet test tests/WebHook.Tests --filter "FullyQualifiedName~UnitTests"

# Integration tests only
dotnet test tests/WebHook.Tests --filter "FullyQualifiedName~IntegrationTests"
```

---

## Project Structure

```
Webhook/
├── Webhook.slnx
├── WebHook.Api/
│   ├── ApplicationFilters/
│   │   ├── ClientValidationFilter.cs
│   │   └── CustomAuthenticationFilter.cs
│   ├── ContextFactory/
│   │   └── RepositoryContextFactory.cs          — design-time DbContext for dotnet ef
│   ├── Controllers/
│   │   ├── AdminController.cs
│   │   ├── AuthenticationController.cs
│   │   ├── OtpOperationController.cs
│   │   ├── ReportingAnalyticsController.cs
│   │   ├── UsersController.cs
│   │   ├── WebhookDeadLetterQueueController.cs
│   │   ├── WebhookDeliveriesController.cs
│   │   ├── WebhookDeliveryAttemptsController.cs
│   │   ├── WebhookEventCatalogController.cs
│   │   ├── WebhookEventController.cs
│   │   ├── WebhookServiceClientEventCatalogsController.cs
│   │   ├── WebhookServiceClientsController.cs
│   │   ├── WebhookSubscriptionController.cs
│   │   └── WebhookSubscriptionEventController.cs
│   ├── ServiceExtensions/
│   │   ├── ApplicationServiceExtensions.cs
│   │   ├── CustomLogSourceEnricher.cs
│   │   └── CustomMiddleware/
│   │       ├── ReqeustOtpEmailExtractionMiddleware.cs
│   │       └── ValidateOtpEmailExtractionMiddleware.cs
│   ├── GlobalExceptionHandler.cs
│   └── Program.cs
│
├── WebHook.Core/
│   ├── Constants/
│   ├── CustomValidators/
│   ├── DataTransferObjects/
│   ├── Entities/
│   │   ├── ConfigurationModels/
│   │   └── CustomEntities/                      — keyless analytics entities
│   ├── EventContracts/
│   ├── Interfaces/
│   │   ├── Helpers/
│   │   └── Services/
│   ├── Mapper/
│   └── RequestParameter/
│
├── WebHook.Infrastructure/
│   ├── BackgroundWorkers/
│   │   ├── EmailProcessorWorker.cs
│   │   ├── EventRaisedWorker.cs
│   │   ├── PendingRaisedEventsWorker.cs
│   │   ├── RetryPendingDeliveriesWorker.cs
│   │   ├── StaleClaimedDeliverReleaseWorker.cs
│   │   └── WebhookDeliveryProcessorWorker.cs
│   ├── CustomHealthChecks/
│   │   ├── C_DriveHealthCheck.cs
│   │   ├── DeadLetterQueuedHealthCheck.cs
│   │   ├── InMemoryCacheHealthCheck.cs
│   │   ├── PendingDeliveriesHealthCheck.cs
│   │   ├── QueuedEmailHealthCheck.cs
│   │   ├── RaisedEventChannelHealthCheck.cs
│   │   ├── StaleDeliveryHealthCheck.cs
│   │   └── WorkerLivelinessHealthCheck.cs
│   ├── EventObjectGenerator/
│   │   └── RuntimeEventBuilder.cs               — builds payload types from catalog schemas
│   ├── EventPublishers/
│   │   └── ApplicationPublisher.cs
│   ├── Persistence/
│   │   ├── Configurations/
│   │   ├── CustomDbColumnConverters/
│   │   ├── Migrations/
│   │   └── RepositoryContext.cs
│   ├── Security/
│   │   ├── ApplicationHasher.cs
│   │   ├── EncryptionService.cs
│   │   ├── SecretKeyGeneratorService.cs
│   │   └── SignatureService.cs
│   ├── Services/
│   │   ├── AuthenticationService.cs
│   │   ├── DeadLetterQueueService.cs
│   │   ├── OtpService.cs
│   │   ├── ReportingAnalyticsService.cs
│   │   ├── RetryAfterPendingService.cs
│   │   ├── StaleClaimedDeliveryReleaseService.cs
│   │   ├── TrendAnalyticsService.cs             — in progress
│   │   ├── UserService.cs
│   │   ├── WebhookDeliveryAttemptService.cs
│   │   ├── WebhookDeliveryProcessorService.cs
│   │   ├── WebhookDeliveryService.cs
│   │   ├── WebhookEventCatalogService.cs
│   │   ├── WebhookEventService.cs
│   │   ├── WebhookServiceClientCatalogService.cs
│   │   ├── WebhookServiceClientService.cs
│   │   ├── WebhookSubscriptionEventService.cs
│   │   └── WebhookSubscriptionService.cs
│   └── Utilities/
│       ├── AuthenticatedUserDetails.cs
│       ├── CacheService.cs
│       ├── EmailContentFormatterHelper.cs
│       ├── EmailService.cs
│       ├── OtpGenerator.cs
│       ├── WebhookDeliveryRetryAfterService.cs
│       └── WorkerLivenessTracker.cs
│
├── src/
│   └── Webhook.Web/                             — Blazor WebAssembly portal
│       ├── Components/
│       ├── Handlers/                            — typed API calls per feature
│       ├── Layout/
│       ├── Pages/
│       └── wwwroot/environment/config.json
│
└── tests/
    └── WebHook.Tests/
        ├── IntegrationTests/
        ├── UnitTests/
        └── Utilities/
```

---

## Roadmap

WebhookHub is currently at **v1**. The core delivery pipeline, identity system, service client onboarding, administrative tooling, web portal and dashboards are in place.

### Already available

- Web portal for subscribers and admins
- Delivery history per user — paginated, filterable by status, date range and subscription
- Per-delivery attempt log — response body, HTTP status, duration and timestamp for each attempt
- Admin and user dashboards with aggregate metrics (success rate, failing subscriptions, dead letters)

### In progress

#### Trend Analytics
Time-series views of delivery health for subscribers and admins (the portal pages exist and are hidden until the API is complete):
- **Delivery volume** — events published and deliveries attempted over time
- **Success rate trend** — per subscription or globally
- **Failure breakdown** — by HTTP response code: 4xx vs 5xx vs timeouts
- **Retry heatmap** — which subscriptions retry most often
- **Dead letter rate** — dead letters over time, with drill-down
- **Delivery latency** — p50, p95 and p99 per subscription
- **Event type distribution** — published events by type over a time range

### Planned

#### Real-Time Monitoring
- **SignalR hub** — push delivery status updates, dead-letter transitions and worker heartbeats to the portal
- **Live delivery feed** — each attempt as it happens, with status, subscriber, event type and response code
- **Worker status panel** — running, idle or unhealthy, per worker

#### Developer Experience
- **Webhook testing sandbox** — send a test delivery with a sample payload to verify a callback URL before going live
- **Event replay** — admin-initiated re-raise of a previously processed event
- **Delivery diff view** — side-by-side request and response for a failed attempt
- **Subscriber activity log** — audit trail of subscription changes, secret rotations and manual retries

#### API and Security
- **Idempotency keys on `POST /api/WebhookEvent`** — prevent duplicate events on internal service retries
- **Subscriber endpoint verification** — challenge/response ping before a callback URL is activated, consistent with Stripe and GitHub webhook patterns
- **Self-service secret rotation** — regenerate a subscription's signing secret without deactivating it
- **OpenTelemetry distributed tracing** — end-to-end trace from publish through fan-out to delivery
- **Shared token cache and Data Protection key ring** — to run multiple API instances

#### Infrastructure
- **Docker Compose setup** — API, portal and PostgreSQL with a single command
- **Helm chart** — Kubernetes deployment manifests
- **GitHub Actions CI pipeline** — build, test and migration script generation on pull request

---

## Contributing

Contributions are welcome. If you find a bug or have a feature suggestion, open an issue first to discuss it. Pull requests should target the `main` branch and include tests for any changed behaviour.

---

## License

Licensed under the [MIT License](LICENSE).
