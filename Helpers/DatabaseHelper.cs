using System.Data;
using System.Globalization;
using Npgsql;

public class DatabaseHelper
{
    public async Task<List<PaymentInfo>> GetAllPayments(string date, int bankId)
    {
        var connectionString = "Host=localhost:5433;Username=postgres;Password=postgres;Database=pix-dotnet-dockerized";
        using var postgresConnection = new NpgsqlConnection(connectionString);
        await postgresConnection.OpenAsync();

        DateTime parsedDate = DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var commandText = @"
                            SELECT 
                        p.""Id"" AS ""PaymentId"",
                        p.""Status"",
                        a.""BankId"" AS ""OriginBankId"",
                        (
                            SELECT 
                                a.""BankId""
                            FROM 
                                ""Keys"" k 
                            INNER JOIN 
                                ""Accounts"" a ON k.""AccountId"" = a.""Id""
                            WHERE 
                                k.""Id"" = p.""KeyId""
                            AND
                                a.""BankId""=@bankId
                        ) AS ""DestinyBankId""
                        FROM 
                            ""Payments"" p
                        LEFT JOIN 
                            ""Accounts"" a ON p.""AccountId"" = a.""Id""
                        LEFT JOIN 
                            ""Keys"" k ON p.""KeyId"" = k.""Id""
                        WHERE
                            DATE_TRUNC('day', p.""CreatedAt"") = @date
                        AND a.""BankId""=@bankId";

        using var command = new NpgsqlCommand(commandText, postgresConnection);
        command.Parameters.AddWithValue("@date", parsedDate);
        command.Parameters.AddWithValue("@bankId", bankId);
        var payments = new List<PaymentInfo>();

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var paymentInfo = new PaymentInfo
            {
                Id = reader.GetInt32("PaymentId"),
                Status = reader.GetString("Status"),
                OriginBankId = reader.IsDBNull(reader.GetOrdinal("OriginBankId"))
                ? -1
                : reader.GetInt32("OriginBankId"),
                DestinyBankId = reader.IsDBNull(reader.GetOrdinal("DestinyBankId"))
                ? -1
                : reader.GetInt32("DestinyBankId")
            };

            payments.Add(paymentInfo);
        }
        return payments;
    }
}