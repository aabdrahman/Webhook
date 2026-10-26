using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookEventCatalog;

namespace WebHook.Core.Interfaces.Services;

/// <summary>
/// Defines operations for managing webhook event catalogs.
/// Provides functionality to create, retrieve, and activate/deactivate event catalogs
/// which represent the list of subscribable webhook event types.
/// </summary>
/// <remarks>
/// Deactivated catalog entries are hidden by a global query filter unless explicitly included.
/// </remarks>
public interface IWebhookEventCatalogService
{
    /// <summary>
    /// Registers a new event type that service clients can publish and users can subscribe to.
    /// </summary>
    /// <remarks>
    /// Each value in <see cref="CreateEventCatalogDto.AvailableFields"/> must be one of
    /// <c>string</c>, <c>int</c>, <c>bool</c>, <c>guid</c>, <c>datetime</c>, <c>double</c>,
    /// <c>decimal</c> or <c>float</c> (case-insensitive). The fields are then compiled into a
    /// runtime type to confirm a payload class can be built from them. Names are unique
    /// case-insensitively.
    /// </remarks>
    /// <param name="createEventCatalogDto">The event type name, description and payload fields.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.Created"/> — the event type was registered.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.BadRequest"/> — a field type is not supported, or a payload type could not be built from the fields.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.Conflict"/> — an event type with the same name exists.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> CreateNewEventCatalogAsync(CreateEventCatalogDto createEventCatalogDto, CancellationToken ct = default);
    /// <summary>
    /// Activates or deactivates an event type.
    /// </summary>
    /// <remarks>
    /// Deactivated event types are hidden from catalog queries, so they cannot be published or
    /// subscribed to. Existing subscriptions and deliveries are not changed.
    /// </remarks>
    /// <param name="EventCatalogId">The event catalog ID.</param>
    /// <param name="isDeactivate"><see langword="true"/> to deactivate; <see langword="false"/> to activate.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.NoContent"/> — the status was updated.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — no event type with that ID.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> EventCatalogActivationAsync(Guid EventCatalogId, bool isDeactivate = true, CancellationToken ct = default);
    /// <summary>
    /// Gets an active event type by ID.
    /// </summary>
    /// <param name="EventCatalogId">The event catalog ID.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="EventCatalogDto"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — the event type was found.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — no active event type with that ID.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<EventCatalogDto>> GetEventCatalogByIdAsync(Guid EventCatalogId, CancellationToken ct = default);
    /// <summary>
    /// Lists event types.
    /// </summary>
    /// <param name="includeDeactivated">Whether to include deactivated event types.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="IReadOnlyList{EventCatalogDto}"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — one or more event types found.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — there are no matching event types.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<IReadOnlyList<EventCatalogDto>>> GetAllEventCatalogAsync(bool includeDeactivated = false, CancellationToken ct = default);
}
