namespace WebHook.Core.RequestParameter;

public class PagedListItem<T> : List<T>
{
    public RequestMetaData metaData { get; set; }

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

    public static PagedListItem<T> ToPagedListItems(IEnumerable<T> items, int totalCount, int pageSize, int pageNumber)
    {
        return new PagedListItem<T>(items.ToList(), pageSize, pageNumber, totalCount);
    }
}