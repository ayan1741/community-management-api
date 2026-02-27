using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using CommunityManagement.Infrastructure.Common;
using CommunityManagement.Infrastructure.Repositories;
using CommunityManagement.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommunityManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var userConnStr = configuration.GetConnectionString("Supabase")
            ?? throw new InvalidOperationException("ConnectionStrings:Supabase yapılandırması eksik.");
        var serviceRoleConnStr = configuration.GetConnectionString("SupabaseServiceRole")
            ?? throw new InvalidOperationException("ConnectionStrings:SupabaseServiceRole yapılandırması eksik.");

        services.AddSingleton<IDbConnectionFactory>(
            _ => new DbConnectionFactory(userConnStr, serviceRoleConnStr));

        // Repositories
        services.AddScoped<IProfileRepository, ProfileRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IApplicationRepository, ApplicationRepository>();
        services.AddScoped<IMemberRepository, MemberRepository>();

        // Services
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        var supabaseUrl = configuration["Supabase:Url"]
            ?? throw new InvalidOperationException("Supabase:Url yapılandırması eksik.");
        var serviceRoleKey = configuration["Supabase:ServiceRoleKey"]
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
}
