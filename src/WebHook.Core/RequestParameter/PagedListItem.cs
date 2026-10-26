namespace WebHook.Core.RequestParameter;

/// <summary>
/// One page of results together with paging metadata.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public class PagedListItem<T> : List<T>
{
    /// <summary>The paging metadata for this page.</summary>
    public RequestMetaData metaData { get; set; }

    /// <summary>
    /// Initialises a page from items that have already been fetched.
    /// </summary>
    /// <param name="listItems">The items on this page.</param>
    /// <param name="pageSize">The maximum number of items per page.</param>
    /// <param name="currentPage">The 1-based page number.</param>
    /// <param name="totalCount">The total number of items across all pages.</param>
    public PagedListItem(List<T> listItems, int pageSize, int currentPage, int totalCount)
    {
        metaData = new RequestMetaData()
        {
            currentPage = currentPage,
            pageSize = pageSize,
            totalCount = totalCount,
            totalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };
        AddRange(listItems);
    }

    /// <summary>
    /// Creates a page from items that have already been fetched.
    /// </summary>
    /// <param name="items">The items on this page.</param>
    /// <param name="totalCount">The total number of items across all pages.</param>
    /// <param name="pageSize">The maximum number of items per page.</param>
    /// <param name="pageNumber">The 1-based page number.</param>
    /// <returns>The page.</returns>
    public static PagedListItem<T> ToPagedListItems(IEnumerable<T> items, int totalCount, int pageSize, int pageNumber)
    {
        return new PagedListItem<T>(items.ToList(), pageSize, pageNumber, totalCount);
    }
}