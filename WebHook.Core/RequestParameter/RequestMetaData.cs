namespace WebHook.Core.RequestParameter;

/// <summary>
/// Paging metadata returned with a <see cref="PagedListItem{T}"/>.
/// </summary>
public record class RequestMetaData
{
    /// <summary>The 1-based page number.</summary>
    public int currentPage { get; set; }
    /// <summary>The maximum number of items per page.</summary>
    public int pageSize { get; set; }
    /// <summary>The total number of pages.</summary>
    public int totalPages { get; set; }
    /// <summary>The total number of items across all pages.</summary>
    public int totalCount { get; set; }
    /// <summary>Whether there is a page after this one.</summary>
    public bool hasNext => currentPage < totalPages;
    /// <summary>Whether there is a page before this one.</summary>
    public bool hasPrevious => currentPage > 1;
}
