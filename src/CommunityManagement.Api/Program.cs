using CommunityManagement.Api.Endpoints;
using CommunityManagement.Api.Middleware;
using CommunityManagement.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

// Dapper: snake_case kolonları PascalCase property'lere eşleştir (organization_id → OrganizationId)
DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI
builder.Services.AddOpenApi();

// Authentication — Supabase JWT
var supabaseUrl = builder.Configuration["Supabase:Url"]
    ?? builder.Configuration["SUPABASE_URL"]
    ?? throw new InvalidOperationException("Supabase:Url yapılandırması eksik.");
var jwtSecret = builder.Configuration["Supabase:JwtSecret"]
    ?? builder.Configuration["SUPABASE_JWT_SECRET"]
    ?? throw new InvalidOperationException("Supabase:JwtSecret yapılandırması eksik.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"{supabaseUrl.TrimEnd('/')}/auth/v1";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = $"{supabaseUrl.TrimEnd('/')}/auth/v1",
            ValidateAudience = true,
            ValidAudience = "authenticated",
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

// MediatR
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(
        typeof(CommunityManagement.Application.Auth.Queries.GetMyContextQuery).Assembly));

// Infrastructure (repositories, services, background workers)
builder.Services.AddInfrastructure(builder.Configuration);

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:5173" };
        policy.SetIsOriginAllowed(origin =>
              {
                  // Sabit izinli origin'ler
                  if (origins.Contains(origin)) return true;
                  // Vercel preview URL'leri: https://community-management-web-sya3-*.vercel.app
                  var uri = new Uri(origin);
                  return uri.Host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase)
                      && uri.Host.StartsWith("community-management-web-", StringComparison.OrdinalIgnoreCase);
              })
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Endpoints
app.MapAuthEndpoints();
app.MapOrganizationEndpoints();
app.MapInvitationEndpoints();
app.MapApplicationEndpoints();
app.MapMemberEndpoints();
app.MapBlockEndpoints();
app.MapUnitEndpoints();
app.MapDueTypeEndpoints();
app.MapDuesPeriodEndpoints();
app.MapAccrualEndpoints();
app.MapPaymentEndpoints();
app.MapLateFeeEndpoints();
app.MapDuesSummaryEndpoints();

app.Run();
