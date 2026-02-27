using CommunityManagement.Application.Common;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;
using System.Text.RegularExpressions;

namespace CommunityManagement.Application.Auth.Commands;

public record ChangeEmailCommand(string NewEmail) : IRequest;

public class ChangeEmailCommandHandler : IRequestHandler<ChangeEmailCommand>
{
    private readonly IProfileRepository _profiles;
    private readonly ISessionService _sessionService;
    private readonly ICurrentUserService _currentUser;

    public ChangeEmailCommandHandler(
        IProfileRepository profiles,
        ISessionService sessionService,
        ICurrentUserService currentUser)
    {
        _profiles = profiles;
        _sessionService = sessionService;
        _currentUser = currentUser;
    }

    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task Handle(ChangeEmailCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NewEmail) || !EmailRegex.IsMatch(request.NewEmail))
            throw AppException.UnprocessableEntity("Geçerli bir e-posta adresi giriniz.");

        var profile = await _profiles.GetByIdAsync(_currentUser.UserId, ct)
            ?? throw AppException.NotFound("Kullanıcı profili bulunamadı.");

        try
        {
            await _sessionService.ChangeEmailAsync(_currentUser.UserId, request.NewEmail, ct);
        }
        catch (HttpRequestException ex)
        {
            throw AppException.UnprocessableEntity($"E-posta değişikliği başarısız: {ex.Message}");
        }
    }
}
