namespace WebHook.Core.RequestParameter;

public record class RequestMetaData
{
    public int currentPage { get; set; }
    public int pageSize { get; set; }
    public int totalPages { get; set; }
    public int totalCount { get; set; }
    public bool hasNext => currentPage < totalPages;
    public bool hasPrevious => currentPage > 1;
}
