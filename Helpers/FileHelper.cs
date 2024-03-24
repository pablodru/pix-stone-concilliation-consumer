using System.Text.Json;

public class FileHelper
{
    public static List<Transaction> ReadFile(string filePath)
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
}