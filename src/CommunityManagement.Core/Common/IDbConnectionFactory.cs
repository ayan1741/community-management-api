using System.Data;
using System.Data.Common;

namespace CommunityManagement.Core.Common;

/// <summary>
/// Veritabanı bağlantısı oluşturmak için soyutlama.
/// IDbConnection (okuma) ve DbConnection (transactional yazma) döndürür.
/// </summary>
public interface IDbConnectionFactory
{
    IDbConnection CreateUserConnection();
    IDbConnection CreateServiceRoleConnection();
    // Async transaction için (BeginTransactionAsync/CommitAsync/RollbackAsync)
    DbConnection CreateServiceRoleDbConnection();
}
