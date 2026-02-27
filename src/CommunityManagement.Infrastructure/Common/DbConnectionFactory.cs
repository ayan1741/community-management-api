using Npgsql;
using System.Data;

namespace CommunityManagement.Infrastructure.Common;

public interface IDbConnectionFactory
{
    IDbConnection CreateUserConnection();
    IDbConnection CreateServiceRoleConnection();
}

public class DbConnectionFactory : IDbConnectionFactory
{
    private readonly string _userConnectionString;
    private readonly string _serviceRoleConnectionString;

    public DbConnectionFactory(string userConnectionString, string serviceRoleConnectionString)
    {
        _userConnectionString = userConnectionString;
        _serviceRoleConnectionString = serviceRoleConnectionString;
    }

    public IDbConnection CreateUserConnection() => new NpgsqlConnection(_userConnectionString);
    public IDbConnection CreateServiceRoleConnection() => new NpgsqlConnection(_serviceRoleConnectionString);
}
