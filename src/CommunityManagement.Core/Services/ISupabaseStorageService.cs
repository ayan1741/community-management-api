namespace CommunityManagement.Core.Services;

public interface ISupabaseStorageService
{
    Task<string> UploadAsync(string bucket, string path, Stream fileStream, string contentType, CancellationToken ct = default);
    Task DeleteAsync(string bucket, string path, CancellationToken ct = default);
}
