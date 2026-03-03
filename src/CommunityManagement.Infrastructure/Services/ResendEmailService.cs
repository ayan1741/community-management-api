using CommunityManagement.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace CommunityManagement.Infrastructure.Services;

public class EmailOptions
{
    public string Mode { get; set; } = "log";
    public string? ApiKey { get; set; }
    public string FromAddress { get; set; } = "noreply@komsunyet.com";
    public string FromName { get; set; } = "KomşuNet";
}

public class ResendEmailService : IEmailService
{
    private readonly HttpClient _http;
    private readonly EmailOptions _options;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(
        HttpClient http,
        IOptions<EmailOptions> options,
        ILogger<ResendEmailService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (_options.Mode == "log")
        {
            _logger.LogInformation("[EMAIL STUB] To={To} Subject={Subject}", to, subject);
            return;
        }

        var body = new
        {
            from = $"{_options.FromName} <{_options.FromAddress}>",
            to = new[] { to },
            subject,
            html = htmlBody,
        };

        var response = await _http.PostAsJsonAsync("emails", body, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Resend API error: {Status} — {Error}", response.StatusCode, errorContent);
            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("[EMAIL SENT] To={To} Subject={Subject}", to, subject);
    }
}
