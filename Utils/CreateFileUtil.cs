using System.Text.Json;

public class FileUtil
{
    public static void CreateFile()
    {
        string outputFile = "output.ndjson";

        var options = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        using (StreamWriter file = File.CreateText(outputFile))
        {
            Random random = new Random();
            for (int i = 7179174; i < 8179174; i++)
            {
                var obj = new { Id = i, Status = GetRandomStatus(random) };
                string jsonLine = JsonSerializer.Serialize(obj, options);
                file.WriteLine(jsonLine);
            }
        }
    }

    static string GetRandomStatus(Random random)
    {
        string[] statuses = { "FAILED", "SUCCESS" };
        int index = random.Next(statuses.Length);
        return statuses[index];
    }

}