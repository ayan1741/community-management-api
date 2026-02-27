using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CommunityManagement.Infrastructure.Services;

public class AccountDeletionCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AccountDeletionCleanupService> _logger;

    public AccountDeletionCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<AccountDeletionCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = CalculateDelayUntilNextRun();
            await Task.Delay(delay, stoppingToken);

            if (stoppingToken.IsCancellationRequested) break;

            await RunCleanupAsync(stoppingToken);
        }
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
            var profileRepository = scope.ServiceProvider.GetRequiredService<IProfileRepository>();

            // Only delete users who explicitly requested deletion (30+ days ago)
            // This avoids accidentally deleting users banned for other reasons (security, admin action)
            var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
            var expiredUsers = await profileRepository.GetDeletionRequestedExpiredAsync(cutoff, ct);
            _logger.LogInformation("AccountDeletion cleanup: {Count} kullanıcı işlenecek.", expiredUsers.Count);

            foreach (var userId in expiredUsers)
            {
                try
                {
                    await profileRepository.SoftDeleteAsync(userId, ct);
                    await sessionService.DeleteUserAsync(userId, ct);
                    _logger.LogInformation("Kullanıcı silindi: {UserId}", userId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Kullanıcı silinirken hata: {UserId}", userId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AccountDeletion cleanup başarısız.");
        }
    }

    private static TimeSpan CalculateDelayUntilNextRun()
    {
        var now = DateTimeOffset.UtcNow;
        var next = now.Date.AddHours(3); // 03:00 UTC
        if (next <= now)
            next = next.AddDays(1);
        return next - now;
    }
}
