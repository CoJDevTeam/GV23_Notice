using GV23_Notice.Domain.Rolls;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace GV23_Notice.Services.Rolls
{
    public interface IRollDbConnectionFactory
    {
        SqlConnection Create(string sourceDb);
    }

    public sealed class RollDbConnectionFactory
        : IRollDbConnectionFactory
    {
        private readonly RollDbOptions _options;

        public RollDbConnectionFactory(
            IOptions<RollDbOptions> options)
        {
            _options = options.Value;
        }

        public SqlConnection Create(
            string sourceDb)
        {
            if (string.IsNullOrWhiteSpace(sourceDb))
            {
                throw new InvalidOperationException(
                    "Source database is required.");
            }

            if (string.IsNullOrWhiteSpace(
                    _options.BaseSqlConnection))
            {
                throw new InvalidOperationException(
                    "RollDb:BaseSqlConnection is missing.");
            }

            /*
             * Validate that the source database actually exists
             * in our application configuration.
             *
             * This prevents somebody passing an arbitrary
             * database name into the connection factory.
             */
            _options.GetSource(sourceDb);

            var csb =
                new SqlConnectionStringBuilder(
                    _options.BaseSqlConnection)
                {
                    InitialCatalog =
                        sourceDb.Trim(),

                    MultipleActiveResultSets =
                        true,

                    TrustServerCertificate =
                        true
                };

            return new SqlConnection(
                csb.ConnectionString);
        }
    }
}