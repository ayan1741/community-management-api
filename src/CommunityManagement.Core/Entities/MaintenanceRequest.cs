namespace CommunityManagement.Core.Entities;

public class MaintenanceRequest
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Title { get; set; } = default!;
    public string Description { get; set; } = default!;
    public string Category { get; set; } = default!;       // elektrik|su_tesisati|asansor|ortak_alan|boya_badana|isitma_sogutma|guvenlik|diger
    public string Priority { get; set; } = default!;       // dusuk|normal|yuksek|acil
    public string Status { get; set; } = default!;         // reported|in_review|assigned|in_progress|resolved|closed|cancelled
    public string LocationType { get; set; } = default!;   // unit|common_area
    public Guid? UnitId { get; set; }
    public string? LocationNote { get; set; }

    // Atama
    public string? AssigneeName { get; set; }
    public string? AssigneePhone { get; set; }
    public string? AssigneeNote { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }

    // Maliyet
    public decimal TotalCost { get; set; }

    // Tekrar
    public bool IsRecurring { get; set; }

    // Memnuniyet
    public short? SatisfactionRating { get; set; }
    public string? SatisfactionComment { get; set; }
    public DateTimeOffset? RatedAt { get; set; }

    // SLA
    public DateTimeOffset? SlaDeadlineAt { get; set; }
    public bool SlaBreached { get; set; }

    // Fotograflar
    public string? PhotoUrls { get; set; }                 // JSON string — ["url1","url2"]

    // Durum zamanlari
    public Guid ReportedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public Guid? CancelledBy { get; set; }

    // Metadata
    public Guid CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
