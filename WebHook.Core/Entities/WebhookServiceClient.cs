using System;
using System.Collections.Generic;
using System.Text;

namespace WebHook.Core.Entities;

/// <summary>
/// An internal service onboarded by an admin to publish events, authenticated by
/// the <c>X-Client-Id</c> and <c>X-Client-Key</c> headers.
/// </summary>
public class WebhookServiceClient
{
    /// <summary>The primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>When the client was onboarded.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>The service's display name, stored in lower case.</summary>
    public string ServiceClientName { get; set; }
    /// <summary>The unique client ID sent in the <c>X-Client-Id</c> header.</summary>
    public string ClientId { get; set; }
    /// <summary>The hash of the client key. The raw key is shown only once, when issued.</summary>
    public string ClientKey { get; set; }
    /// <summary>Whether the client may publish events.</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>When the client was deactivated, or <see langword="null"/> if it is active.</summary>
    public DateTimeOffset? DeactivatedAt { get; set; }
    /// <summary>The ID of the admin who deactivated the client.</summary>
    public string? DeactivatedBy { get; set; }
    /// <summary>The ID of the admin who onboarded the client.</summary>
    public string CreatedBy { get; set; }

    //Relationship with the join table with event catalog
    /// <summary>The event types the client is allowed to publish.</summary>
    public ICollection<WebhookServiceClientEventCatalog> EventCatalogs { get; set; } = [];
}
