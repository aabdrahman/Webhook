using System;
using System.Collections.Generic;
using System.Text;

namespace WebHook.Core.Entities;

public class WebhookServiceClient
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ServiceClientName { get; set; }
    public string ClientId { get; set; }
    public string ClientKey { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? DeactivatedAt { get; set; }
    public string? DeactivatedBy { get; set; }
    public string CreatedBy { get; set; }

    //Relationship with the join table with event catalog
    public ICollection<WebhookServiceClientEventCatalog> EventCatalogs { get; set; } = [];
}
