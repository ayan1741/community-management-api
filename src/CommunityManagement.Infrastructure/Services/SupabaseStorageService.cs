using CommunityManagement.Core.Services;

namespace CommunityManagement.Infrastructure.Services;

public class SupabaseStorageService : ISupabaseStorageService
{
    private readonly HttpClient _http;

    public SupabaseStorageService(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> UploadAsync(string bucket, string path, Stream fileStream, string contentType, CancellationToken ct = default)
    {
        using var content = new StreamContent(fileStream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        var response = await _http.PostAsync($"storage/v1/object/{bucket}/{path}", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Supabase Storage upload hatası: {response.StatusCode} — {error}");
        }

        // Public URL oluştur (private bucket — signed URL gerekebilir, ama service role ile erişilebilir)
        return $"{_http.BaseAddress}storage/v1/object/{bucket}/{path}";
    }

    public async Task DeleteAsync(string bucket, string path, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"storage/v1/object/{bucket}/{path}", ct);
        // Dosya yoksa bile hata fırlatma — idempotent silme
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Supabase Storage delete hatası: {response.StatusCode} — {error}");
        }
    }
}
