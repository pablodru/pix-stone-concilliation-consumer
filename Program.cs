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

Console.WriteLine("[*] Waiting for messages...");

var consumer = new EventingBasicConsumer(channel);
consumer.Received += async (model, ea) =>
{
    var body = ea.Body.ToArray();
    var jsonMessage = Encoding.UTF8.GetString(body);
    var concilliation = JsonSerializer.Deserialize<ConcilliationMessage>(jsonMessage);

    Console.WriteLine("Received concilliation message: {0}", jsonMessage);

    var fileTransactions = ReadFile(concilliation.File);
    var databasePayments = await GetAllPayments(concilliation.Date);

    var databaseTransactionsSet = new HashSet<int>(databasePayments.Select(p => p.Id));
    var comparisonResult = new
{
    databaseToFile = databasePayments
        .Where(p => !fileTransactions.Any(t => t.Id == p.Id) &&
                    (p.OriginBankId == concilliation.BankId || p.DestinyBankId == concilliation.BankId))
        .ToList(),
    fileToDatabase = fileTransactions
        .Where(t => !databaseTransactionsSet.Contains(t.Id) &&
                    databasePayments.Any(p => p.OriginBankId == concilliation.BankId || p.DestinyBankId == concilliation.BankId))
        .ToList(),
    differentStatus = fileTransactions
        .Where(t => databaseTransactionsSet.Contains(t.Id) &&
                    databasePayments.Any(p => p.OriginBankId == concilliation.BankId || p.DestinyBankId == concilliation.BankId))
        .Select(t => new DifferentStatusIds { Id = t.Id })
        .Where(ds => fileTransactions.Any(t => t.Id == ds.Id && t.Status != databasePayments.First(p => p.Id == ds.Id).Status))
        .ToList()
};

    await client.PostAsJsonAsync(concilliation.Postback, comparisonResult);
    Console.WriteLine($"Concilliation done with the result: {comparisonResult}");

    channel.BasicAck(ea.DeliveryTag, false);
};

async Task<List<PaymentInfo>> GetAllPayments(string date)
{
    var connectionString = "Host=localhost;Username=postgres;Password=151099;Database=pix-dotnet";
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
                        ) AS ""DestinyBankId""
                        FROM 
                            ""Payments"" p
                        LEFT JOIN 
                            ""Accounts"" a ON p.""AccountId"" = a.""Id""
                        LEFT JOIN 
                            ""Keys"" k ON p.""KeyId"" = k.""Id""
                        WHERE
                            DATE_TRUNC('day', p.""CreatedAt"") = @date";

    using var command = new NpgsqlCommand(commandText, postgresConnection);
    command.Parameters.AddWithValue("@date", parsedDate);
    var payments = new List<PaymentInfo>();

    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        var paymentInfo = new PaymentInfo
        {
            Id = reader.GetInt32("PaymentId"),
            Status = reader.GetString("Status"),
            OriginBankId = reader.GetInt32("OriginBankId"),
            DestinyBankId = reader.GetInt32("DestinyBankId")
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
