using CommunityManagement.Core.Services;
using System.Net.Http.Json;
using System.Text.Json;

namespace CommunityManagement.Infrastructure.Services;

public class SupabaseSessionService : ISessionService
{
    private readonly HttpClient _http;

    public SupabaseSessionService(HttpClient http)
    {
        _http = http;
    }

    public async Task RevokeAllAsync(Guid userId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync(
            $"auth/v1/admin/users/{userId}/logout",
            null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task BanUserAsync(Guid userId, int days, CancellationToken ct = default)
    {
        var body = new { ban_duration = $"{days * 24}h" };
        var response = await _http.PutAsJsonAsync($"auth/v1/admin/users/{userId}", body, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ChangeEmailAsync(Guid userId, string newEmail, CancellationToken ct = default)
    {
        var body = new { email = newEmail };
        var response = await _http.PutAsJsonAsync($"auth/v1/admin/users/{userId}", body, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteUserAsync(Guid userId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"auth/v1/admin/users/{userId}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<Guid>> GetBannedExpiredUsersAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("auth/v1/admin/users?filter=banned", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var now = DateTimeOffset.UtcNow;

        var users = new List<Guid>();
        if (json.TryGetProperty("users", out var usersArray))
        {
            foreach (var user in usersArray.EnumerateArray())
            {
                if (user.TryGetProperty("banned_until", out var bannedUntilProp)
                    && DateTimeOffset.TryParse(bannedUntilProp.GetString(), out var bannedUntil)
                    && bannedUntil <= now
                    && user.TryGetProperty("id", out var idProp)
                    && Guid.TryParse(idProp.GetString(), out var id))
                {
                    users.Add(id);
                }
            }
        }

        return users;
    }
}
