using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Threading.Tasks;
using Npgsql;
using System.Globalization;
using System.Data;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var connectionFactory = new ConnectionFactory()
{
    HostName = "localhost",
    UserName = "admin",
    Password = "admin"
};

var queueName = "Concilliations";

using var connection = connectionFactory.CreateConnection();
using var channel = connection.CreateModel();
using var client = new HttpClient();

channel.QueueDeclare(
    queue: queueName,
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: null
);

static void CreateFile()
{
    // Nome do arquivo de saída
    string outputFile = "output.ndjson";

    // Configurar as opções para ignorar indentações e escrever apenas uma linha
    var options = new JsonSerializerOptions
    {
        WriteIndented = false
    };

    // Escrever as linhas no arquivo
    using (StreamWriter file = File.CreateText(outputFile))
    {
        Random random = new Random();
        for (int i = 1005890; i < 1006889; i++)
        {
            var obj = new { Id = i, Status = GetRandomStatus(random) };
            string jsonLine = JsonSerializer.Serialize(obj, options);
            file.WriteLine(jsonLine);
        }
    }
}

static string GetRandomStatus(Random random)
{
    string[] statuses = { "FAILED", "SUCCESS", "PROCESSING" };
    int index = random.Next(statuses.Length);
    return statuses[index];
}

CreateFile();

Console.WriteLine("[*] Waiting for messages...");

var consumer = new EventingBasicConsumer(channel);
consumer.Received += async (model, ea) =>
{
    var body = ea.Body.ToArray();
    var jsonMessage = Encoding.UTF8.GetString(body);
    var concilliation = JsonSerializer.Deserialize<ConcilliationMessage>(jsonMessage);

    Console.WriteLine("Received concilliation message: {0}", jsonMessage);

    var fileTransactions = ReadFile(concilliation.File);
    var databasePayments = await GetAllPayments(concilliation.Date, concilliation.BankId);

    Console.WriteLine($"Arquivo com: {fileTransactions.Count} entidades.");
    Console.WriteLine($"Banco com: {databasePayments.Count} entidades.");

    var fileTransactionsSet = new HashSet<int>(fileTransactions.Select(t => t.Id));
    var databasePaymentsSet = new HashSet<int>(databasePayments.Select(p => p.Id));
    var fileTransactionsStatusMap = fileTransactions.ToDictionary(t => t.Id, t => t.Status);

    var databaseToFile = databasePayments
        .Where(p => !fileTransactionsSet.Contains(p.Id))
        .ToList();

    var fileToDatabase = fileTransactions
        .Where(t => !databasePaymentsSet.Contains(t.Id))
        .ToList();

    var differentStatus = fileTransactions
        .Where(t => databasePaymentsSet.Contains(t.Id))
        .Where(t => fileTransactionsStatusMap[t.Id] != databasePayments.First(p => p.Id == t.Id).Status)
        .Select(t => new DifferentStatusIds { Id = t.Id })
        .ToList();

    var comparisonResult = new
    {
        databaseToFile,
        fileToDatabase,
        differentStatus
    };

    await client.PostAsJsonAsync(concilliation.Postback, comparisonResult);
    Console.WriteLine($"Concilliation done with the result: {comparisonResult}");

    channel.BasicAck(ea.DeliveryTag, false);
};

async Task<List<PaymentInfo>> GetAllPayments(string date, int bankId)
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
            ? -1 // ou outro valor padrão apropriado
            : reader.GetInt32("OriginBankId"),
            DestinyBankId = reader.IsDBNull(reader.GetOrdinal("DestinyBankId")) 
            ? -1 // ou outro valor padrão apropriado
            : reader.GetInt32("DestinyBankId")
        };

        payments.Add(paymentInfo);
    }
    return payments;
}

static List<Transaction> ReadFile(string filePath)
{
    List<Transaction> transactions = [];
    if (File.Exists(filePath))
    {
        using StreamReader fileReader = new(filePath);
        string? line;
        while ((line = fileReader.ReadLine()) != null)
        {
            Transaction? transaction = JsonSerializer.Deserialize<Transaction>(line);
            if (transaction != null)
            {
                transactions.Add(transaction);
            }
            else
            {
                Console.WriteLine("Erro ao deserializar a linha.");
            }
        }
        return transactions;
    }
    else
    {
        Console.WriteLine("O arquivo não existe.");
        throw new Exception();
    }
}

channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);

Console.WriteLine("Waiting for concilliation messages...");
Console.ReadLine();

// Start the web application
app.Run();
