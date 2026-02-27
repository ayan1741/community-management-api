using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;
using UnitEntity = CommunityManagement.Core.Entities.Unit;

namespace CommunityManagement.Application.Units.Commands;

public record CreateUnitsBulkCommand(
    Guid OrgId,
    Guid BlockId,
    int StartFloor,
    int EndFloor,
    int UnitsPerFloor,
    string NumberFormat  // "sequential" | "floor-unit" | "floor-letter"
) : IRequest<BulkCreateResult>;

public record BulkCreateResult(int Created, int Skipped, IReadOnlyList<string> SkippedNumbers);

public class CreateUnitsBulkCommandHandler : IRequestHandler<CreateUnitsBulkCommand, BulkCreateResult>
{
    private readonly IUnitRepository _units;
    private readonly IBlockRepository _blocks;
    private readonly ICurrentUserService _currentUser;

    public CreateUnitsBulkCommandHandler(
        IUnitRepository units,
        IBlockRepository blocks,
        ICurrentUserService currentUser)
    {
        _units = units;
        _blocks = blocks;
        _currentUser = currentUser;
    }

    public async Task<BulkCreateResult> Handle(CreateUnitsBulkCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var block = await _blocks.GetByIdAsync(request.BlockId, ct)
            ?? throw AppException.NotFound("Blok bulunamadı.");

        if (block.OrganizationId != request.OrgId)
            throw AppException.NotFound("Blok bulunamadı.");

        var totalFloors = request.EndFloor - request.StartFloor + 1;
        var totalCount = totalFloors * request.UnitsPerFloor;

        if (totalCount > 100)
            throw AppException.UnprocessableEntity("En fazla 100 daire aynı anda oluşturulabilir.");

        var generatedNumbers = GenerateUnitNumbers(request);
        var existing = await _units.GetExistingNumbersAsync(request.BlockId, generatedNumbers, ct);
        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var toCreate = generatedNumbers
            .Where(n => !existingSet.Contains(n))
            .Select(n => new UnitEntity
            {
                Id = Guid.NewGuid(),
                OrganizationId = request.OrgId,
                BlockId = request.BlockId,
                UnitNumber = n,
                UnitType = "residential",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            })
            .ToList();

        var skippedNumbers = generatedNumbers.Where(n => existingSet.Contains(n)).ToList();

        if (toCreate.Count > 0)
            await _units.CreateBulkAsync(toCreate, ct);

        return new BulkCreateResult(toCreate.Count, skippedNumbers.Count, skippedNumbers);
    }

    private static IReadOnlyList<string> GenerateUnitNumbers(CreateUnitsBulkCommand req)
    {
        var numbers = new List<string>();
        var seq = 1;

        for (var floor = req.StartFloor; floor <= req.EndFloor; floor++)
        {
            for (var unit = 1; unit <= req.UnitsPerFloor; unit++)
            {
                var number = req.NumberFormat switch
                {
                    "floor-unit" => $"{floor}{unit:D2}",
                    "floor-letter" => $"{floor}{(char)('A' + unit - 1)}",
                    _ => seq.ToString()  // "sequential"
                };
                numbers.Add(number);
                seq++;
            }
        }

        return numbers;
    }
}
