using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    // API server IP address (change here later to signature/domain)
    private static string apiIp = "ip SymFSM Server https://principium.pro/symfsm/";

    private static string baseUrl = $"http://{apiIp}:8088";

    static async Task Main()
    {
        using var client = new HttpClient();

        // English prompt example:
        // "Generate 10 business ideas for a specific industry"
        string prompt = "Generate 10 business ideas for the artificial intelligence industry";

        Console.WriteLine("Sending request...");

        var submitData = new
        {
            prompt = prompt
        };

        string json = JsonSerializer.Serialize(submitData);

        var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json"
        );

        var submitResponse = await client.PostAsync(
            $"{baseUrl}/submit",
            content
        );

        string submitText = await submitResponse.Content.ReadAsStringAsync();

        Console.WriteLine("Submit response:");
        Console.WriteLine(submitText);

        var submitResult = JsonSerializer.Deserialize<SubmitResponse>(submitText);

        if (submitResult == null || string.IsNullOrEmpty(submitResult.id))
        {
            Console.WriteLine("Failed to get task id");
            return;
        }

        string id = submitResult.id;

        Console.WriteLine($"Task ID: {id}");
        Console.WriteLine("Waiting for result...");

        while (true)
        {
            await Task.Delay(30000);

            var resultResponse = await client.GetAsync(
                $"{baseUrl}/result?id={id}"
            );

            string resultText = await resultResponse.Content.ReadAsStringAsync();

            var result = JsonSerializer.Deserialize<ResultResponse>(resultText);

            Console.WriteLine($"Status: {result?.status}");

            if (result?.status == "done")
            {
                Console.WriteLine("\nRESULT:");
                Console.WriteLine(result.result);
                break;
            }

            if (result?.status == "error")
            {
                Console.WriteLine("\nERROR:");
                Console.WriteLine(result.error);
                break;
            }
        }

        Console.WriteLine("\nFinished.");
        Console.ReadLine();
    }


    class SubmitResponse
    {
        public string? id { get; set; }
        public string? status { get; set; }
    }


    class ResultResponse
    {
        public string? id { get; set; }
        public string? status { get; set; }
        public string? result { get; set; }
        public string? error { get; set; }
    }
}
