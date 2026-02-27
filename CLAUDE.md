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

### Npgsql 9.x + Dapper Tip Mapping (Runtime 500 riski)

- **`timestamptz` → `DateTime`**: Npgsql 9.x, `timestamp with time zone` kolonlarını `DateTime` (UTC)
  olarak döndürür — `DateTimeOffset` **değil**. Private `*Row` record'larında `DateTime` kullan,
  public DTO'ya dönüştürürken `new DateTimeOffset(dt, TimeSpan.Zero)` uygula.
- **`COUNT(*)` → `long`**: Dapper, PostgreSQL `COUNT()` sonucunu `long` (`Int64`) döndürür — `int` değil.
  `TotalCount` alanı `long` tanımla, dönüştürürken `(int)row.TotalCount` kullan.
- **Dapper positional record**: Constructor parametre tipleri DB'den dönen Npgsql tipleriyle EXACT
  eşleşmeli. Tip uyuşmazlığı compile-time hatası değil, **runtime 500** verir — erken yakalamak zor.
- **SQL kolon adları**: Her yeni query yazılmadan önce migration SQL ile karşılaştır.
  `audit_logs` şeması: `actor_id` (user_id değil), `table_name` / `record_id` (entity_type / entity_id değil).

### Minimal API Endpoint Kuralları

- **Opsiyonel parametreler sıralandığında CS1737**: `IMediator mediator` (required DI param) opsiyonel
  `[FromQuery]` parametrelerden ÖNCE gelmeli. Aksi hâlde `CS1737` derleme hatası.
- **`[FromQuery] int page` default olmadan**: Default değer verilmezse query param gönderilmediğinde
  ASP.NET Core **400** döner. Her pagination parametresine `= 1` / `= 20` default ekle.

### Local Test Kuralı

- `dotnet run --no-build` — eski binary'yi çalıştırır, kod değişikliği yansımaz → **YanlIş**.
- `dotnet run --project src/CommunityManagement.Api` — her seferinde yeniden derler → **Doğru**.
- Port meşgulse: `Get-Process -Name dotnet | Stop-Process` ile önceki process'i öldür.

### Railway Ortam Değişkeni Kuralları

- **`#` yorum karakteri**: Railway raw editor'da `DB_PASSWORD=abc#123` yazılırsa `abc` olarak kırpılır.
  Fix: GUI Key/Value alanlarını kullan (raw mode değil).
- **Connection string özel karakter**: URL formatı (`postgresql://...`) yerine
  `NpgsqlConnectionStringBuilder` + ayrı env var'lar (DB_HOST, DB_PASSWORD) kullan.
- **IPv4 kısıtı**: Railway yalnızca IPv4 destekler. Supabase direct bağlantısı IPv6 kullanır.
  Fix: Supabase **Session Pooler** kullan: `aws-1-eu-central-1.pooler.supabase.com:5432`,
  username formatı `postgres.[project-ref]`.

## Deferred (Sprint Sonu)

- **#1 Transaction**: `SubmitApplicationCommand` + `ApproveApplicationCommand` henüz transactional değil.
  Fix: raw Dapper + `NpgsqlTransaction` in handler (B option).
