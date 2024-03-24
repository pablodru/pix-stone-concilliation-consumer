using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
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

//FileUtil.CreateFile();

var databaseHelper = new DatabaseHelper();

var consumer = new EventingBasicConsumer(channel);
consumer.Received += async (model, ea) =>
{
    var start = DateTime.Now;

    var body = ea.Body.ToArray();
    var jsonMessage = Encoding.UTF8.GetString(body);
    var concilliation = JsonSerializer.Deserialize<ConcilliationMessage>(jsonMessage);

    if (concilliation == null) channel.BasicReject(ea.DeliveryTag, requeue: false);

    Console.WriteLine("Received concilliation message: {0}", jsonMessage);

    try
    {
        var fileTransactions = FileHelper.ReadFile(concilliation.File);
        var databasePayments = await databaseHelper.GetAllPayments(concilliation.Date, concilliation.BankId);

        var databasePaymentsDictionary = databasePayments.ToDictionary(p => p.Id);

        Console.WriteLine($"Arquivo com: {fileTransactions.Count} entidades.");
        Console.WriteLine($"Banco com: {databasePayments.Count} entidades.");

        var fileTransactionsSet = new HashSet<int>(fileTransactions.Select(t => t.Id));
        var databasePaymentsSet = new HashSet<int>(databasePayments.Select(p => p.Id));
        var fileTransactionsStatusMap = fileTransactions.ToDictionary(t => t.Id, t => t.Status);

        var databaseToFile = databasePayments
            .Where(p => !fileTransactionsSet.Contains(p.Id))
            .ToList();
        Console.WriteLine($"Total DatabaseToFile: {databaseToFile.Count()}");

        var fileToDatabase = fileTransactions
            .Where(t => !databasePaymentsSet.Contains(t.Id))
            .ToList();
        Console.WriteLine($"Total FileToDatabase: {fileToDatabase.Count()}");

        var differentStatus = new List<DifferentStatusIds>();

        foreach (var transaction in fileTransactions)
        {
            if (databasePaymentsDictionary.TryGetValue(transaction.Id, out var databaseTransaction))
            {
                if (transaction.Status != databaseTransaction.Status)
                {
                    differentStatus.Add(new DifferentStatusIds { Id = transaction.Id });
                }
            }
        }
        Console.WriteLine($"Total DifferentStatus: {differentStatus.Count()}");

        var comparisonResult = new
        {
            databaseToFile,
            fileToDatabase,
            differentStatus
        };

        await client.PostAsJsonAsync(concilliation.Postback, comparisonResult);
        Console.WriteLine($"Concilliation done with the result: {comparisonResult}");

        var end = DateTime.Now;
        Console.WriteLine($"Execution time: {(end - start).TotalMilliseconds}ms");
        channel.BasicAck(ea.DeliveryTag, false);
    }
    catch (Exception e)
    {
        Console.WriteLine($"Concilliation failed with error: {e.Message}");
        channel.BasicReject(ea.DeliveryTag, requeue: false);

        return;
    }
};

channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);

Console.WriteLine("Waiting for concilliation messages...");
Console.ReadLine();

// Start the web application
app.Run();
