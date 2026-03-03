using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;
using System.Text.RegularExpressions;

namespace CommunityManagement.Application.Finance.Commands;

public record UploadFinanceDocumentCommand(
    Guid OrgId, Guid RecordId, Stream FileStream,
    string FileName, string ContentType, long FileSize
) : IRequest<string>;

public class UploadFinanceDocumentCommandHandler : IRequestHandler<UploadFinanceDocumentCommand, string>
{
    private readonly IFinanceRecordRepository _records;
    private readonly ICurrentUserService _currentUser;
    private readonly ISupabaseStorageService _storage;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "application/pdf"
    };

    private const long MaxFileSize = 5 * 1024 * 1024; // 5 MB

    public UploadFinanceDocumentCommandHandler(
        IFinanceRecordRepository records,
        ICurrentUserService currentUser,
        ISupabaseStorageService storage)
    {
        _records = records;
        _currentUser = currentUser;
        _storage = storage;
    }

    public async Task<string> Handle(UploadFinanceDocumentCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        var record = await _records.GetByIdAsync(request.RecordId, ct)
            ?? throw AppException.NotFound("Kayıt bulunamadı.");

        if (record.OrganizationId != request.OrgId)
            throw AppException.NotFound("Kayıt bulunamadı.");

        if (record.DeletedAt is not null)
            throw AppException.NotFound("Kayıt bulunamadı.");

        if (request.FileSize > MaxFileSize)
            throw AppException.UnprocessableEntity("Dosya boyutu 5 MB'dan büyük olamaz.");

        if (!AllowedContentTypes.Contains(request.ContentType))
            throw AppException.UnprocessableEntity("Desteklenmeyen dosya formatı. PDF, JPEG veya PNG yükleyebilirsiniz.");

        var sanitizedFileName = Regex.Replace(request.FileName, @"[^a-zA-Z0-9._-]", "_");
        var storagePath = $"{request.OrgId}/{request.RecordId}/{sanitizedFileName}";

        // Mevcut belge varsa sil
        if (!string.IsNullOrEmpty(record.DocumentUrl))
        {
            var oldPath = ExtractPathFromUrl(record.DocumentUrl);
            if (!string.IsNullOrEmpty(oldPath))
                await _storage.DeleteAsync("finance-documents", oldPath, ct);
        }

        var url = await _storage.UploadAsync("finance-documents", storagePath, request.FileStream, request.ContentType, ct);

        record.DocumentUrl = url;
        record.UpdatedBy = _currentUser.UserId;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await _records.UpdateAsync(record, ct);

        return url;
    }

    private static string? ExtractPathFromUrl(string url)
    {
        const string marker = "/storage/v1/object/finance-documents/";
        var idx = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? url[(idx + marker.Length)..] : null;
    }
}
