using GV23_Notice.Models.Security;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Data;

namespace GV23_Notice.Services.Security
{
    public sealed class UserAccessService : IUserAccessService
    {
        private readonly string _connectionString;
        private readonly UserManagementAuthOptions _authOptions;
        private readonly ILogger<UserAccessService> _logger;

        public UserAccessService(
            IConfiguration configuration,
            IOptions<UserManagementAuthOptions> authOptions,
            ILogger<UserAccessService> logger)
        {
            _connectionString = configuration.GetConnectionString("UserManagementConnection")
                                ?? throw new InvalidOperationException("Missing connection string: UserManagementConnection");

            _authOptions = authOptions.Value;
            _logger = logger;
        }

        public async Task<AppUserProfile?> GetUserProfileAsync(string username, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(username))
                return null;

            await using var conn = new SqlConnection(_connectionString);
            await using var cmd = new SqlCommand(_authOptions.StoredProcedure, conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.AddWithValue("@Username", username);
            cmd.Parameters.AddWithValue("@System", _authOptions.SystemId);

            await conn.OpenAsync(ct);

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            if (!await reader.ReadAsync(ct))
            {
                _logger.LogWarning("User {Username} was authenticated by Windows but not found in UserManagement.", username);
                return null;
            }

            return new AppUserProfile
            {
                UserId = GetNullableInt(reader, "UserID"),
                Username = GetString(reader, "Username") ?? username,
                FullName = BuildFullName(reader),
                Role = GetString(reader, "Role") ?? "",
                HasAccess = true
            };
        }

        private static string BuildFullName(SqlDataReader reader)
        {
            var fullName =
                GetString(reader, "FullName") ??
                GetString(reader, "FullNames") ??
                GetString(reader, "Name");

            if (!string.IsNullOrWhiteSpace(fullName))
                return fullName.Trim();

            var firstName =
                GetString(reader, "FirstName") ??
                GetString(reader, "Firstname") ??
                GetString(reader, "Names") ?? "";

            var surname =
                GetString(reader, "Surname") ??
                GetString(reader, "LastName") ?? "";

            return $"{firstName} {surname}".Trim();
        }

        private static string? GetString(SqlDataReader reader, string columnName)
        {
            var ordinal = GetOrdinalSafe(reader, columnName);
            if (ordinal < 0 || reader.IsDBNull(ordinal))
                return null;

            return Convert.ToString(reader.GetValue(ordinal));
        }

        private static int? GetNullableInt(SqlDataReader reader, string columnName)
        {
            var ordinal = GetOrdinalSafe(reader, columnName);
            if (ordinal < 0 || reader.IsDBNull(ordinal))
                return null;

            return Convert.ToInt32(reader.GetValue(ordinal));
        }

        private static int GetOrdinalSafe(SqlDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }
    }
}
