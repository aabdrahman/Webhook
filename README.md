# Webhook
# WebhookHub

A production-grade webhook notification service built with .NET 10. WebhookHub allows internal business applications to publish events that are reliably delivered as signed HTTP POST requests to registered subscriber callback URLs — with automatic retry, dead-lettering, escalation notifications, and full audit trails.

Open source and free to use.

---

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Features](#features)
- [Tech Stack](#tech-stack)
- [Getting Started](#getting-started)
- [Delivery Pipeline](#delivery-pipeline)
- [Background Workers](#background-workers)
- [Security](#security)
- [Health Checks](#health-checks)
- [API Reference](#api-reference)
- [Authentication](#authentication)
- [Testing](#testing)
- [Project Structure](#project-structure)
- [Contributing](#contributing)
- [License](#license)

---

## Overview

WebhookHub sits between your internal services and your subscribers. An internal application raises an event — WebhookHub validates it, fans it out to every subscriber registered for that event type, and handles the full delivery lifecycle: retry on failure, escalation emails when an endpoint is slow, dead-lettering when all retries are exhausted, and admin-initiated manual retry for dead-lettered deliveries.

```
Internal Service
      │
      ▼
POST /api/webhookevent
      │
      ▼
Validation → Persist → Raise to Channel
                              │
                              ▼
                    EventRaisedWorker
                              │
                              ▼
                   Create Delivery Records
                              │
                              ▼
                    DeliveryWorker (two-phase claim)
                              │
              ┌───────────────┴───────────────┐
              ▼                               ▼
         Delivered                         Failed
                                              │
                                    ┌─────────┴──────────┐
                                    ▼                     ▼
                               Retry Queue         Max Retries Exceeded
                                                          │
                                                          ▼
                                                   Dead Letter Queue
                                                          │
                                                          ▼
                                               Escalation Email → Admin Manual Retry
```

---

## Architecture

WebhookHub uses a clean three-project architecture:

```
WebhookHub.Api             — ASP.NET Core 10 Web API
                             Controllers, filters, middleware, health checks
WebhookHub.Core            — Domain entities, DTOs, interfaces, constants
WebhookHub.Infrastructure  — EF Core, Identity, background workers, services, security
```

Test projects:

```
WebHook.UnitTests          — Service-level tests with xUnit, Moq, and Testcontainers PostgreSQL
WebHook.IntegrationTests   — HTTP-level tests with WebApplicationFactory and Moq
```

---

## Features

### Event Publishing
- Internal services raise events via a simple POST endpoint
- Events are validated against a catalog of known event types with declared field schemas
- JSON payloads are validated against the catalog schema at publish time — missing or invalid fields are rejected with a descriptive error identifying each failing field
- Correlation IDs group related events from a single business transaction

### Reliable Delivery
- Automatic fan-out to every subscriber registered for the raised event type
- **Two-phase claim/process pattern** — deliveries are locked with a worker identity and expiry timestamp before any HTTP attempt, preventing duplicate delivery when multiple worker instances run concurrently
- Configurable retry with computed `nextRetryAt` timestamps
- Slow endpoint detection — if delivery duration exceeds the configured threshold an email notification is sent to the subscriber
- Dead-lettering when a delivery exhausts its maximum retry count
- Escalation email to the subscriber contact on dead-letter transition
- Admin-initiated manual retry for dead-lettered deliveries with justification

### Stale Delivery Recovery
- A dedicated background worker monitors deliveries locked beyond their `LockedUntil` timestamp — indicating a crashed worker — and releases them back to the failed queue for reprocessing

### Webhook Signatures
- Every delivery includes an `X-Webhook-Signature: sha256=<hmac>` header
- Subscribers can verify the payload has not been tampered with using their subscription secret key

### Identity and Access
- JWT authentication with refresh token support
- OTP flow for sensitive self-service operations — users must complete OTP verification to obtain an operation token before deactivating their own account
- Admin bypass — Admins can perform account operations directly without an OTP token
- Role-based access — `USER` and `Admin` roles enforced per endpoint
- Custom JTI cache validation on every authenticated request — revoked tokens are rejected immediately without waiting for expiry

### Event Catalog Management
- Admins define subscribable event types with their available field schemas
- Event types can be activated or deactivated without deletion
- Subscribers choose which events to subscribe to per subscription

---

## Tech Stack

| Concern | Technology |
|---|---|
| Runtime | .NET 10 |
| Web framework | ASP.NET Core 10 |
| ORM | EF Core 10 with Npgsql |
| Database | PostgreSQL |
| Identity | ASP.NET Core Identity |
| Authentication | JWT Bearer + custom `IAsyncAuthorizationFilter` |
| Background workers | `BackgroundService` / `IHostedService` |
| In-process messaging | `System.Threading.Channels` |
| Payload signing | HMAC-SHA256 |
| Data protection | ASP.NET Core Data Protection |
| Logging | Serilog |
| API documentation | Scalar (`/scalar/v1`) |
| Unit tests | xUnit, Moq, Testcontainers PostgreSQL |
| Integration tests | xUnit, `WebApplicationFactory`, Moq |

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [PostgreSQL 14+](https://www.postgresql.org/download/)

### Environment Variables

```bash
webhook_secret_key=<your-JWT-signing-key-minimum-32-characters>
```

### Configuration (`appsettings.json`)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=webhookhub;Username=...;Password=..."
  },
  "JwtSettings": {
    "ValidIssuer":                          "webhook_service",
    "ValidAudiences":                       "audience1;audience2",
    "TokenExpirationAfterInSeconds":        1800,
    "RefreshTokenExpirationAfterInSeconds": 3600
  },
  "SmtpSettings": {
    "Host":        "smtp.example.com",
    "Port":        587,
    "SenderEmail": "noreply@example.com",
    "SenderName":  "WebhookHub"
  },
  "DeliverySettings": {
    "MaxRetryCount":          5,
    "MaxProcessingCount":     10,
    "MaxDeliveryDurationMs":  5000,
    "WorkerLeaseSeconds":     30
  },
  "WorkerSettings": {
    "LivenessTimeoutSeconds": 60
  }
}
```

### Apply Migrations

WebhookHub does not run `Database.Migrate()` at startup. Migrations are scripted and applied before deployment:

```powershell
dotnet ef migrations script <LastAppliedMigration> <NewMigration> `
    --idempotent `
    --output ./scripts/delta.sql
```

Run the generated script against your PostgreSQL database, then start the application.

### Run the API

```bash
dotnet run --project WebhookHub.Api
```

API documentation available at:

```
https://localhost:<port>/scalar/v1
```

---

## Delivery Pipeline

The delivery pipeline is split across two background workers communicating through an in-memory channel and the database.

### Step 1 — Event Publishing

An internal business application calls:

```
POST /api/webhookevent
```

with a payload containing the event type, JSON body, source identifier, and an optional correlation ID.

### Step 2 — Validation and Persistence

The system:
1. Checks the correlation ID is unique for the event type
2. Validates the event type exists in the Event Catalog
3. Validates the JSON payload against the catalog's declared field schema — returns `400` with each failing field named if validation fails
4. Persists the event with status `Pending`
5. Publishes the new event ID to the `EventRaised` channel

### Step 3–7 — Fan-out (EventRaisedWorker)

The `EventRaisedWorker` listens to the channel. On receiving an event ID:

1. Confirms the event is in `Pending` status in the database
2. Queries the subscription-to-event-catalog join table to find all subscriptions registered for the event type
3. Creates a `WebhookDelivery` record with status `Pending` for each matching subscription
4. Saves all delivery records
5. Marks the event as `Processed`

### Step 8–11 — Delivery (DeliveryWorker)

The `DeliveryWorker` polls the delivery table for `Pending` records:

1. Selects up to the configured `MaxProcessingCount` pending deliveries
2. **Two-phase claim** — marks selected deliveries as `Processing` with `LockedBy = workerInstanceId` and `LockedUntil = now + leaseTime` before any HTTP attempt, preventing duplicate delivery across worker instances
3. For each claimed delivery, retrieves the subscriber callback URL and secret key
4. Sends an HTTP POST with the event payload and an `X-Webhook-Signature` HMAC header
5. Records a `DeliveryAttempt` with request payload, response body, status, and `DeliveredAt` timestamp

**On success:**
- Marks delivery as `Delivered`
- Checks delivery duration against the configured `MaxDeliveryDurationMs` — if exceeded, sends a slow endpoint notification email to the subscriber

**On failure:**
- Increments `RetryCount`
- Computes the next `RetryAt` timestamp
- Sets status back to `Failed`

**On max retries reached:**
- Sets delivery status to `DeadLetter`
- Creates a `DeadLetterQueue` record linked to the delivery
- Sends an escalation notification email to the subscription contact

### Step 12 — Retry Processing

A background worker monitors `Failed` deliveries where `DateTimeOffset.UtcNow >= NextRetryAt` and reprocesses them through the same delivery flow.

### Step 13 — Stale Claim Recovery

A dedicated `StaleClaimedDeliveryReleaseWorker` monitors deliveries in `Processing` status where `LockedUntil` has been exceeded — indicating the processing worker crashed before releasing the lock. These deliveries are released and their status reset to `Failed` for reprocessing.

### Dead Letter Manual Retry

An Admin may request a manual retry for any dead-lettered delivery:

1. The system checks the dead letter's retry cycle count against the configured maximum
2. If within the limit, the delivery is set back to `Processing`, the retry cycle incremented, and the delivery re-enters the worker pipeline
3. The request requires a justification stored against the dead letter record for audit

---

## Background Workers

| Worker | Trigger | Responsibility |
|---|---|---|
| `EventRaisedWorker` | Channel message | Fans out events to delivery records |
| `DeliveryWorker` | DB poll | Claims and delivers pending webhooks |
| `RetryWorker` | DB poll | Reprocesses failed deliveries past their `NextRetryAt` |
| `StaleClaimedDeliveryReleaseWorker` | DB poll | Reclaims deliveries locked by crashed workers |

### Two-Phase Claim Pattern

The delivery worker uses a two-phase claim rather than optimistic locking. Optimistic locking detects conflicts after the fact but does not prevent multiple workers from attempting the same delivery concurrently. The two-phase approach:

1. **Claim** — `UPDATE deliveries SET status = 'Processing', locked_by = @workerId, locked_until = @expiry WHERE status = 'Pending'`
2. **Process** — only the worker that holds the lock makes the HTTP call
3. **Release** — update status to `Delivered` or `Failed` and clear the lock fields

If a worker crashes between steps 1 and 2, `StaleClaimedDeliveryReleaseWorker` detects that `locked_until` has passed and releases the lock, returning the delivery to `Failed` for the retry worker to pick up.

---

## Security

| Mechanism | Implementation |
|---|---|
| JWT signing | HMAC-SHA256, secret from environment variable |
| Token revocation | JTI stored in distributed cache on login; filter rejects any request whose JTI is missing or mismatched |
| OTP tokens | Short-lived signed operation tokens using ASP.NET Core Data Protection, hash-validated on use |
| Operation token purpose | Tokens are issued for a specific purpose (e.g. `DeactivateProfile`) and rejected if presented for any other operation |
| Webhook signatures | `X-Webhook-Signature: sha256=HMAC(payload, subscriberSecretKey)` |
| Password hashing | ASP.NET Core Identity default (PBKDF2) |
| Account lockout | Configurable max failed attempts; indefinite lockout on deactivation |

### Custom Authentication Filter

Every authenticated request passes through `CustomAuthenticationFilter`, an `IAsyncAuthorizationFilter` that runs globally:

1. Endpoints marked `[AllowAnonymous]` — filter exits immediately
2. `Authorization: Bearer <token>` header must be present
3. JTI claim is extracted from `HttpContext.User` (populated by the authentication middleware)
4. JTI is looked up in the distributed cache — must be non-default and match the claim
5. Any failure returns `401 Unauthorized`

This ensures that signing out a user (by evicting their JTI from the cache) takes effect immediately — their token is rejected on the next request even if it has not yet expired.

---

## Health Checks

Available at `GET /Admin/_health`.

| Check | What it monitors |
|---|---|
| `postgresql` | Primary database connectivity |
| `email-queue` | Email channel depth — unhealthy above configured threshold |
| `event-raised-worker` | Heartbeat liveness of the EventRaisedWorker |
| `delivery-worker` | Heartbeat liveness of the DeliveryWorker |
| `stale-claim-worker` | Heartbeat liveness of the StaleClaimedDeliveryReleaseWorker |
| `dead-letter-queue` | Count of unretried dead letter items |
| `pending-deliveries` | Count of pending deliveries — detects worker backlog |
| `stale-processing` | Count of Processing deliveries past their lease — detects crashed workers |

Each worker updates a `WorkerLivenessTracker` singleton at the top of every loop iteration. If the heartbeat goes stale beyond the configured timeout the check turns `Unhealthy`.

Example response:

```json
{
  "status": "Healthy",
  "checks": [
    { "name": "postgresql",       "status": "Healthy",   "duration": 12.3,  "exception": null },
    { "name": "email-queue",      "status": "Healthy",   "duration": 0.1,   "exception": null },
    { "name": "event-raised-worker", "status": "Healthy","duration": 0.0,   "exception": null }
  ],
  "totalDurationMs": 12.9
}
```

---

## API Reference

Full interactive documentation available at `/scalar/v1` when running locally.

### Authentication — `api/Authentication`

| Method | Route | Auth | Description |
|---|---|---|---|
| POST | `/login` | Public | Sign in — returns JWT access token and refresh token |
| POST | `/change-password` | Authenticated | Change account password |
| POST | `/request-otp` | Public | Request a one-time password via email |
| POST | `/refresh` | Public | Refresh an authenticated session |

### Users — `api/Users`

| Method | Route | Auth | Description |
|---|---|---|---|
| POST | `/register` | Public | Register a new user account |
| POST | `/deactivate` | Authenticated | Deactivate an account (OTP operation token required for non-Admin) |
| POST | `/reactivate` | Admin | Reactivate a deactivated account |

### OTP Operations — `api/OtpOperation`

| Method | Route | Auth | Description |
|---|---|---|---|
| POST | `/validate-otp` | Public | Validate a one-time password — returns a signed operation token |
| DELETE | `/revoke-otp/{userId}` | Admin | Revoke an active OTP for a user |

### Event Catalog — `api/WebhookEventCatalog`

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/` | Public | List all event catalog entries |
| GET | `/{id}` | Authenticated | Get a specific event catalog entry |
| POST | `/` | Admin | Create a new event type |
| PUT | `/{id}?isDeactivate={bool}` | Admin | Activate or deactivate an event type |

### Subscriptions — `api/WebhookSubscription`

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/` | Admin | List all subscriptions |
| GET | `/{id}` | Admin | Get a subscription by ID |
| GET | `/get-user-subscriptions` | Authenticated | Get the current user's subscriptions |
| POST | `/` | Authenticated | Create a new subscription |
| PUT | `/{id}` | Authenticated | Activate a subscription |
| DELETE | `/{id}` | Authenticated | Delete a subscription |

### Subscription Events — `api/WebhookSubscription/{subscriptionId}/events`

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/` | Authenticated | List subscribed events for a subscription |
| PUT | `/?eventName={name}` | Authenticated | Subscribe to an event type |
| DELETE | `/?eventName={name}` | Authenticated | Unsubscribe from an event type |

### Webhook Events — `api/webhookevent`

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/{correlationId}` | Authenticated | Get events by correlation ID |
| GET | `/` | Admin | Query events with filters |
| POST | `/` | Public | Publish a new webhook event (internal services) |

### Dead Letter Queue — `api/WebhookDelivery/{deliveryId}/deadLetters`

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/` | Authenticated | Get dead letter entries for a delivery |
| POST | `/` | Admin | Request manual retry of a dead-lettered delivery |

### Admin — `Admin`

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/_health` | Public | API and dependency health status |

---

## Testing

### Unit Tests

Service-level tests covering authentication, user management, OTP flows, and the delivery service:

- **xUnit** — test runner
- **Moq** — mocks for `IAuthenticatedUserDetails`, `IDataProtectionProvider`, `ICacheService`
- **Testcontainers** — real PostgreSQL container via `IClassFixture<PostgreSqlFixture>` for full Identity pipeline testing
- Each test method gets a fresh scoped `DbContext` to prevent EF Core change tracker contamination across tests

### Integration Tests

HTTP-level tests covering all controllers through the full ASP.NET Core pipeline:

- **`WebApplicationFactory<Program>`** — one factory per controller group, each with its own `TestAuthHandler` and cache mock
- **`TestAuthHandler`** — a custom `AuthenticationHandler` that carries both `USER` and `Admin` roles and seeds the cache mock with a non-default JTI so `CustomAuthenticationFilter` passes
- **Moq** — service layer is fully mocked so tests focus on routing, auth, status code mapping, request forwarding, and exception handling
- **`IAsyncLifetime`** — fresh `HttpClient` and mock reset before every test method

### Running Tests

```bash
# All tests
dotnet test

# Unit tests only
dotnet test WebHook.UnitTests

# Integration tests only
dotnet test WebHook.IntegrationTests
```

---

## Project Structure

```
WebhookHub/
├── WebhookHub.Api/
│   ├── Controllers/
│   │   ├── AdminController.cs
│   │   ├── AuthenticationController.cs
│   │   ├── OtpOperationController.cs
│   │   ├── UsersController.cs
│   │   ├── WebhookDeadLetterQueueController.cs
│   │   ├── WebhookEventCatalogController.cs
│   │   ├── WebhookEventController.cs
│   │   ├── WebhookSubscriptionController.cs
│   │   └── WebhookSubscriptionEventController.cs
│   └── Filters/
│       └── CustomAuthenticationFilter.cs
│
├── WebhookHub.Core/
│   ├── Constants/
│   ├── DataTransferObjects/
│   ├── Entities/
│   └── Interfaces/
│       └── Services/
│
├── WebhookHub.Infrastructure/
│   ├── BackgroundWorkers/
│   │   ├── EventRaisedWorker.cs
│   │   ├── DeliveryWorker.cs
│   │   ├── RetryWorker.cs
│   │   └── StaleClaimedDeliveryReleaseWorker.cs
│   ├── CustomHealthChecks/
│   │   ├── QueuedEmailHealthCheck.cs
│   │   ├── WorkerLivenessHealthCheck.cs
│   │   ├── DeadLetterQueueHealthCheck.cs
│   │   ├── PendingDeliveryHealthCheck.cs
│   │   └── StaleProcessingHealthCheck.cs
│   ├── Data_Persistence/
│   │   └── RepositoryContext.cs
│   ├── Security/
│   │   ├── ApplicationHasher.cs
│   │   └── AuthenticatedUserDetails.cs
│   └── Services/
│       ├── AuthenticationService.cs
│       ├── DeadLetterQueueService.cs
│       ├── UserService.cs
│       ├── WebhookEventService.cs
│       ├── WebhookEventCatalogService.cs
│       ├── WebhookSubscriptionService.cs
│       └── WebhookSubscriptionEventService.cs
│
├── WebHook.UnitTests/
└── WebHook.IntegrationTests/
```

---

## Contributing

Contributions are welcome. If you find a bug or have a feature suggestion, open an issue first to discuss it. Pull requests should target the `main` branch and include tests for any changed behaviour.

---

## License

Dual licensed under the [MIT License](LICENSE-MIT) and the [Apache License 2.0](LICENSE-APACHE). You may choose either licence depending on your use case.