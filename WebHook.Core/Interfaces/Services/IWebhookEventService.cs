using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookEvent;

namespace WebHook.Core.Interfaces.Services;

/// <summary>
/// Defines the contract for managing webhook events — the runtime occurrences
/// of catalogued event types raised by internal business services.
/// </summary>
/// <remarks>
/// <para>
/// A webhook event represents a specific occurrence of a business activity
/// (e.g. a customer being created, a payment being completed). It is distinct
/// from the <c>EventCatalog</c>, which defines the available event types and
/// their field schemas. The catalog defines <em>what can happen</em>;
/// a webhook event records <em>what did happen</em>.
/// </para>
/// <para>
/// Implementations are responsible for:
/// <list type="bullet">
///   <item><description>Validating the event type against the catalog before persisting.</description></item>
///   <item><description>Validating the payload structure against the catalog's declared fields.</description></item>
///   <item><description>Enforcing uniqueness of the <c>CorrelationId</c> + <c>EventType</c> combination to prevent duplicate event processing.</description></item>
///   <item><description>Providing filtered retrieval of events for monitoring and audit purposes.</description></item>
/// </list>
/// </para>
/// </remarks>
public interface IWebhookEventService
{
    /// <summary>
    /// Creates and persists a new webhook event occurrence raised by an
    /// internal business service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The creation pipeline performs three validation steps before any data
    /// is written to the database:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///       <strong>Correlation ID uniqueness</strong> — rejects the event if a record with
    ///       the same <c>CorrelationId</c> and <c>EventType</c> combination already exists,
    ///       preventing duplicate processing of the same business transaction.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <strong>Event type validation</strong> — verifies the raised event type exists
    ///       in the <c>EventCatalog</c>. Unknown event types are rejected with
    ///       <see cref="System.Net.HttpStatusCode.BadRequest"/>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <strong>Payload validation</strong> — uses <see cref="WebHook.Infrastructure.EventObjectGenerator.RuntimeEventBuilder"/>
    ///       to dynamically construct the expected payload type from the catalog's
    ///       <c>AvailableFields</c>, then deserializes and inspects the submitted
    ///       payload against it. Missing required fields are reported individually
    ///       in the failure response.
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    /// <param name="createWebhookEvent">
    /// The details of the event to raise, including the event type, payload
    /// JSON, source service, and optional correlation ID.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the asynchronous
    /// operation before it completes.
    /// </param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/> where the
    /// data field contains the newly created event's ID as a string. Possible
    /// status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.Created"/> — event created and persisted successfully.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.Conflict"/> — a duplicate CorrelationId + EventType combination already exists.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.BadRequest"/> — invalid event type, malformed payload JSON, or missing required payload fields.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> CreateEventAsync(CreateWebhookEventDto createWebhookEvent, CancellationToken ct = default);
    /// <summary>
    /// Retrieves all webhook events associated with a given correlation ID.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A correlation ID ties one or more webhook events back to the same
    /// originating business transaction. For example, a single customer
    /// onboarding request may raise both <c>CustomerCreated</c> and
    /// <c>AccountApproved</c> events, both sharing the same correlation ID.
    /// This method retrieves all of them in a single call.
    /// </para>
    /// <para>
    /// The query uses <c>AsNoTracking</c> for read performance since the
    /// returned DTOs are not tracked or modified by EF Core.
    /// </para>
    /// </remarks>
    /// <param name="correlationId">
    /// The correlation ID of the originating business transaction to look up.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the asynchronous
    /// operation before it completes.
    /// </param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of
    /// <see cref="IReadOnlyList{WebhookEventDto}"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — one or more events found for the correlation ID.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — no events exist for the provided correlation ID.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<IReadOnlyList<WebhookEventDto>>> GetWebhookEventsAsync(GetWebhookEventParameters parameters, CancellationToken ct = default);
    /// <summary>
    /// Retrieves a filtered, pageable list of webhook events using the
    /// provided query parameters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The date range defined by <see cref="GetWebhookEventParameters.CreatedAtFrom"/>
    /// and <see cref="GetWebhookEventParameters.CreatedAtTo"/> is always applied
    /// as a mandatory base filter. All other parameters are optional and stack
    /// on top of the date range filter when provided:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <see cref="GetWebhookEventParameters.Source"/> — filters by the
    ///       originating service (exact match). Ignored if null or whitespace.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="GetWebhookEventParameters.EventType"/> — filters by event
    ///       type (normalised to uppercase before comparison). Ignored if null or whitespace.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="GetWebhookEventParameters.Status"/> — filters by delivery
    ///       status. Parsed case-insensitively via <see cref="Enum.TryParse{TEnum}"/>;
    ///       invalid or unrecognised status strings are silently ignored rather
    ///       than causing an error.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="GetWebhookEventParameters.CorrelationId"/> — filters by
    ///       correlation ID. Ignored if null.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// Unlike <see cref="GetWebhookEventAsync"/>, this method always returns
    /// <see cref="System.Net.HttpStatusCode.OK"/> even when the result set is
    /// empty — an empty list is a valid filtered result, not an error condition.
    /// </para>
    /// </remarks>
    /// <param name="parameters">
    /// The query parameters controlling which events are returned. The date
    /// range fields (<c>CreatedAtFrom</c> and <c>CreatedAtTo</c>) are required;
    /// all other fields are optional filters.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the asynchronous
    /// operation before it completes.
    /// </param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of
    /// <see cref="IReadOnlyList{WebhookEventDto}"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — query executed successfully; data may be an empty list.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<IReadOnlyList<WebhookEventDto>>> GetWebhookEventAsync(Guid correlationId, CancellationToken ct = default);
}
