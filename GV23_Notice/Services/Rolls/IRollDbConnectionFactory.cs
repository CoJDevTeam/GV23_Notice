using Microsoft.Data.SqlClient;

namespace GV23_Notice.Services.Rolls
{
    public interface IRollDbConnectionFactory
    {
        SqlConnection Create(string sourceDb);
    }

    public sealed class RollDbConnectionFactory : IRollDbConnectionFactory
    {
        private readonly IConfiguration _cfg;

        public RollDbConnectionFactory(IConfiguration cfg) => _cfg = cfg;

        public SqlConnection Create(string sourceDb)
        {
            var baseConn = _cfg["RollDb:BaseSqlConnection"];
            if (string.IsNullOrWhiteSpace(baseConn))
                throw new InvalidOperationException("RollDb:BaseSqlConnection is missing in appsettings.");

            // baseConn has Server=...;User=...;Password=...; etc
            // Add Database=sourceDb dynamically:
            var csb = new SqlConnectionStringBuilder(baseConn)
            {
                InitialCatalog = sourceDb,
                MultipleActiveResultSets = true,
                TrustServerCertificate = true
            };

            return new SqlConnection(csb.ToString());
        }
    }
}
