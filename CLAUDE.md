# community-management-api

.NET 9 Clean Architecture API. Supabase (PostgreSQL + Auth) backend.

## Komutlar

```bash
dotnet build                                     # Tüm solution
dotnet run --project src/CommunityManagement.Api # API başlat (localhost:5000)
dotnet test                                      # Testler
```

## Mimari

```
src/
  CommunityManagement.Core/           # Entities, Interfaces (bağımlılık yok)
  CommunityManagement.Application/    # MediatR Commands/Queries, AppException
  CommunityManagement.Infrastructure/ # Dapper repos, Supabase services, DI
  CommunityManagement.Api/            # Minimal API endpoints, JWT middleware
```

## Ortam Kurulumu

```bash
cp src/CommunityManagement.Api/appsettings.Development.json.example \
   src/CommunityManagement.Api/appsettings.Development.json
```

Doldurulacak alanlar: `DB_PASSWORD`, `ServiceRoleKey`, `JwtSecret`

## Migration Sırası (Supabase SQL Editor)

1. `phases/phase-3a/migration.sql` — `BEGIN…COMMIT` bloğu
2. `phases/phase-3a/migration.sql` — `CONCURRENTLY` index'leri (ayrı query olarak çalıştır)
3. `phases/phase-3b/migration.sql` — `BEGIN…COMMIT` bloğu
4. `phases/phase-3b/migration.sql` — `CONCURRENTLY` index'leri (ayrı query olarak çalıştır)

## Kritik Gotcha'lar

- **`ApplicationEntity` alias**: `Application` entity adı namespace ile çakışıyor.
  `using ApplicationEntity = CommunityManagement.Core.Entities.Application;`
- **Infrastructure.csproj**: `<FrameworkReference Include="Microsoft.AspNetCore.App" />` gerekli
  (`BackgroundService`, `IConfiguration`, `IHttpContextAccessor` vb. için)
- **MediatR 12.x**: DI entegrasyonu built-in — `MediatR.Extensions.Microsoft.DependencyInjection` EKLEME
- **JWT**: issuer = `{SupabaseUrl}/auth/v1`, audience = `authenticated`
- **İki connection string**: `Supabase` (okuma) + `SupabaseServiceRole` (yazma)

## Deferred (Sprint Sonu)

- **#1 Transaction**: `SubmitApplicationCommand` + `ApproveApplicationCommand` henüz transactional değil.
  Fix: raw Dapper + `NpgsqlTransaction` in handler (B option).
