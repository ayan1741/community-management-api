using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using CommunityManagement.Infrastructure.Common;
using CommunityManagement.Infrastructure.Repositories;
using CommunityManagement.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CommunityManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var userConnStr = configuration.GetConnectionString("Supabase")
            ?? BuildDbConnectionString(configuration)
            ?? throw new InvalidOperationException("ConnectionStrings:Supabase veya DB_HOST+DB_PASSWORD yapılandırması eksik.");
        var serviceRoleConnStr = configuration.GetConnectionString("SupabaseServiceRole")
            ?? BuildDbConnectionString(configuration)
            ?? throw new InvalidOperationException("ConnectionStrings:SupabaseServiceRole veya DB_HOST+DB_PASSWORD yapılandırması eksik.");

        services.AddSingleton<IDbConnectionFactory>(
            _ => new DbConnectionFactory(userConnStr, serviceRoleConnStr));

        // Repositories
        services.AddScoped<IProfileRepository, ProfileRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IApplicationRepository, ApplicationRepository>();
        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<IBlockRepository, BlockRepository>();
        services.AddScoped<IUnitRepository, UnitRepository>();

        // Services
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        var supabaseUrl = configuration["Supabase:Url"]
            ?? configuration["SUPABASE_URL"]
            ?? throw new InvalidOperationException("Supabase:Url yapılandırması eksik.");
        var serviceRoleKey = configuration["Supabase:ServiceRoleKey"]
            ?? configuration["SUPABASE_SERVICE_ROLE_KEY"]
            ?? throw new InvalidOperationException("Supabase:ServiceRoleKey yapılandırması eksik.");

        services.AddHttpClient<ISessionService, SupabaseSessionService>(client =>
        {
            client.BaseAddress = new Uri(supabaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add("apikey", serviceRoleKey);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {serviceRoleKey}");
        });

        // Background services
        services.AddHostedService<AccountDeletionCleanupService>();

        return services;
    }

    private static string? BuildDbConnectionString(IConfiguration configuration)
    {
        var host = configuration["DB_HOST"];
        var password = configuration["DB_PASSWORD"];
        if (host == null || password == null) return null;

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(configuration["DB_PORT"], out var port) ? port : 5432,
            Database = configuration["DB_NAME"] ?? "postgres",
            Username = configuration["DB_USER"] ?? "postgres",
            Password = password,
            SslMode = SslMode.Require
        }.ConnectionString;
    }
}
