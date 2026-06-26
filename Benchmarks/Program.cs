using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;

const string ApiBaseUrl = "http://you_SymFSM_ip:8088";
const string DatasetPath = "gpqa_main.csv";

const int Workers = 30;
var pollInterval = TimeSpan.FromSeconds(5);
var maxWaitPerQuestion = TimeSpan.FromMinutes(30);

// Live statistics
int _totalProcessed = 0;
int _totalCorrect = 0;
int _totalIncorrect = 0;
int _totalErrors = 0;
object _statsLock = new object();



Directory.CreateDirectory("Results");

using var client = new HttpClient
{
    Timeout = TimeSpan.FromMinutes(2)
};

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("Loading GPQA dataset...");

List<GpqaQuestion> questions;

using (var reader = new StreamReader(DatasetPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
{
    Delimiter = ",",
    HeaderValidated = null,
    MissingFieldFound = null,
    BadDataFound = null,
    TrimOptions = TrimOptions.Trim
}))
{
    csv.Context.RegisterClassMap<GpqaCsvMap>();

    questions = csv
        .GetRecords<GpqaCsvRow>()
        .Select((row, index) => row.ToQuestion(index + 1))
        .ToList();
}

Console.WriteLine($"Loaded: {questions.Count} questions");

ValidateQuestions(questions);

var semaphore = new SemaphoreSlim(Workers);

var tasks = questions
    .Select((question, index) => RunQuestionAsync(index, question, semaphore))
    .ToList();

var finished = await Task.WhenAll(tasks);

var results = finished
    .OrderBy(x => x.Index)
    .Select(x => x.Row)
    .ToList();

int total = results.Count;
int correct = results.Count(x => x.Correct);
int errors = results.Count(x => !string.IsNullOrWhiteSpace(x.Error));

double accuracy = total == 0
    ? 0
    : (double)correct / total * 100.0;

Console.WriteLine();
Console.WriteLine($"Total: {total}");
Console.WriteLine($"Correct: {correct}");
Console.WriteLine($"Incorrect: {total - correct}");
Console.WriteLine($"Errors: {errors}");
Console.WriteLine($"Accuracy: {accuracy:F2}%");

await SaveResults(results, accuracy);

Console.WriteLine();
Console.WriteLine("Done.");


async Task<(int Index, ResultRow Row)> RunQuestionAsync(
    int index,
    GpqaQuestion q,
    SemaphoreSlim semaphore)
{
    await semaphore.WaitAsync();

    try
    {
        Console.WriteLine($"[{index + 1}/{questions.Count}] START");

        string prompt = BuildPrompt(q);

        try
        {
            string taskId = await SubmitPrompt(client, prompt);

            string raw = await WaitForResult(
                client,
                taskId,
                pollInterval,
                maxWaitPerQuestion);

            string answer = ExtractAnswer(raw);

            bool ok = answer == q.Answer;

            // Обновляем статистику
            lock (_statsLock)
            {
                _totalProcessed++;
                if (ok)
                    _totalCorrect++;
                else
                    _totalIncorrect++;

                double currentAccuracy = _totalProcessed == 0
                    ? 0
                    : (double)_totalCorrect / _totalProcessed * 100.0;

                Console.WriteLine($"[{index + 1}] expected={q.Answer}, actual={answer}, correct={ok}");
                Console.WriteLine($"[STATS] Processed: {_totalProcessed}, Correct: {_totalCorrect}, " +
                                  $"Incorrect: {_totalIncorrect}, Accuracy: {currentAccuracy:F2}%");
            }

            return (
                index,
                new ResultRow
                {
                    Index = index + 1,
                    Question = q.Question,
                    A = q.A,
                    B = q.B,
                    C = q.C,
                    D = q.D,
                    Expected = q.Answer,
                    Actual = answer,
                    Correct = ok,
                    RawResponse = raw,
                    Error = ""
                });
        }
        catch (Exception ex)
        {
            // Обновляем статистику с ошибкой
            lock (_statsLock)
            {
                _totalProcessed++;
                _totalErrors++;

                double currentAccuracy = _totalProcessed == 0
                    ? 0
                    : (double)_totalCorrect / _totalProcessed * 100.0;

                Console.WriteLine($"[{index + 1}] ERROR: {ex.Message}");
                Console.WriteLine($"[STATS] Processed: {_totalProcessed}, Correct: {_totalCorrect}, " +
                                  $"Incorrect: {_totalIncorrect}, Errors: {_totalErrors}, " +
                                  $"Accuracy: {currentAccuracy:F2}%");
            }

            return (
                index,
                new ResultRow
                {
                    Index = index + 1,
                    Question = q.Question,
                    A = q.A,
                    B = q.B,
                    C = q.C,
                    D = q.D,
                    Expected = q.Answer,
                    Actual = "",
                    Correct = false,
                    RawResponse = "",
                    Error = ex.Message
                });
        }
    }
    finally
    {
        semaphore.Release();
    }
}


static string BuildPrompt(GpqaQuestion q)
{
    return
$"""
You are an expert reasoning system solving a multiple-choice question using computational thinking.

FIRST, generate 10 distinct approaches to solving this problem:
- For each approach, explain the core idea and why it might work
- Each approach should use a DIFFERENT reasoning strategy (deduction, induction, analogy, decomposition, counterexample, formal logic, probabilistic, constraint satisfaction, causal reasoning, synthesis)
- Show how each approach would analyze the options

SECOND, for each approach, analyze all four options (A, B, C, D):
- Which option(s) would this approach select?
- Why would it reject the others?
- What are the strengths and weaknesses of this approach?

THIRD, evaluate and rank your 10 approaches:
- Which approach is most reliable? Why?
- Which approach has the strongest evidence?
- Which approach best handles edge cases or ambiguities?

FOURTH, select the BEST approach and use it to determine your final answer.

FIFTH, output a valid JSON object with your final answer.

Format your response like this:

## 10 APPROACHES TO SOLVING THIS PROBLEM

### Approach 1: [Name of approach]
**Core idea:** ...
**How it analyzes options:** ...
**Would select:** ...
**Why others rejected:** ...
**Strength:** ...
**Weakness:** ...

### Approach 2: [Name of approach]
... (repeat for all 10 approaches)

## EVALUATION AND RANKING

[Rank the 10 approaches from most to least reliable, with clear justification]

## FINAL REASONING (using the BEST approach)

[Detailed step-by-step reasoning using the selected best approach]

## FINAL ANSWER

[Valid JSON]
(Return exactly one JSON object with one field: "answer". The value of "answer" must be one of: "A", "B", "C", "D".)


Question:
{q.Question}

Options:
A) {q.A}
B) {q.B}
C) {q.C}
D) {q.D}
""";
}


async Task<string> SubmitPrompt(HttpClient client, string prompt)
{
    var body = JsonSerializer.Serialize(new { prompt });

    using var response = await client.PostAsync(
        $"{ApiBaseUrl}/submit",
        new StringContent(body, Encoding.UTF8, "application/json"));

    string responseText = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        throw new Exception(
            $"Submit failed: HTTP {(int)response.StatusCode} {response.StatusCode}. Body: {responseText}");
    }

    var parsed = JsonSerializer.Deserialize<SubmitResponse>(
        responseText,
        JsonOptions.CaseInsensitive);

    if (string.IsNullOrWhiteSpace(parsed?.id))
    {
        throw new Exception($"Submit response does not contain id. Body: {responseText}");
    }

    return parsed.id;
}


async Task<string> WaitForResult(
    HttpClient client,
    string id,
    TimeSpan pollInterval,
    TimeSpan maxWait)
{
    var started = DateTimeOffset.UtcNow;

    while (true)
    {
        if (DateTimeOffset.UtcNow - started > maxWait)
        {
            throw new TimeoutException($"Timed out waiting for result. Task id: {id}");
        }

        await Task.Delay(pollInterval);

        using var response = await client.GetAsync(
            $"{ApiBaseUrl}/result?id={Uri.EscapeDataString(id)}");

        string responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(
                $"Result failed: HTTP {(int)response.StatusCode} {response.StatusCode}. Body: {responseText}");
        }

        var result = JsonSerializer.Deserialize<ResultResponse>(
            responseText,
            JsonOptions.CaseInsensitive);

        if (result == null)
        {
            throw new Exception($"Cannot parse result response. Body: {responseText}");
        }

        string status = result.status?.Trim().ToLowerInvariant() ?? "";

        if (status == "done")
        {
            return result.result ?? "";
        }

        if (status == "error")
        {
            string apiError = result.error ?? "";
            throw new Exception(
                $"API returned error for task {id}. Error: {apiError}. Body: {responseText}");
        }

        if (status is "pending"
            or "queued"
            or "running"
            or "processing"
            or "in_progress"
            or "started"
            or "")
        {
            continue;
        }

        throw new Exception($"Unknown task status '{result.status}'. Body: {responseText}");
    }
}


static string ExtractAnswer(string text)
{
    if (string.IsNullOrWhiteSpace(text))
        return "";

    string original = text;
    text = text.Trim();

    text = StripCodeFence(text);

    string? jsonObject = TryExtractFirstJsonObject(text);

    if (!string.IsNullOrWhiteSpace(jsonObject))
    {
        string answerFromJson = TryReadAnswerFromJson(jsonObject);

        if (answerFromJson is "A" or "B" or "C" or "D")
            return answerFromJson;
    }

    string answerFromWholeTextJson = TryReadAnswerFromJson(text);

    if (answerFromWholeTextJson is "A" or "B" or "C" or "D")
        return answerFromWholeTextJson;

    var direct = text.Trim().Trim('"', '\'', '.', ' ', '\r', '\n', '\t').ToUpperInvariant();

    if (direct is "A" or "B" or "C" or "D")
        return direct;

    var match = Regex.Match(
        original,
        @"[""']?answer[""']?\s*[:=]\s*[""']?([ABCD])[""']?",
        RegexOptions.IgnoreCase);

    if (match.Success)
        return match.Groups[1].Value.ToUpperInvariant();

    return "";
}


static string StripCodeFence(string text)
{
    text = text.Trim();

    if (!text.StartsWith("```"))
        return text;

    text = Regex.Replace(
        text,
        @"^```[a-zA-Z0-9_-]*\s*",
        "",
        RegexOptions.Singleline);

    text = Regex.Replace(
        text,
        @"\s*```$",
        "",
        RegexOptions.Singleline);

    return text.Trim();
}


static string TryReadAnswerFromJson(string json)
{
    try
    {
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return "";

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!string.Equals(prop.Name, "answer", StringComparison.OrdinalIgnoreCase))
                continue;

            if (prop.Value.ValueKind != JsonValueKind.String)
                return "";

            string answer = prop.Value
                .GetString()?
                .Trim()
                .ToUpperInvariant() ?? "";

            return answer is "A" or "B" or "C" or "D"
                ? answer
                : "";
        }

        return "";
    }
    catch
    {
        return "";
    }
}


static string? TryExtractFirstJsonObject(string text)
{
    int start = text.IndexOf('{');

    if (start < 0)
        return null;

    bool insideString = false;
    bool escaped = false;
    int depth = 0;

    for (int i = start; i < text.Length; i++)
    {
        char c = text[i];

        if (escaped)
        {
            escaped = false;
            continue;
        }

        if (c == '\\' && insideString)
        {
            escaped = true;
            continue;
        }

        if (c == '"')
        {
            insideString = !insideString;
            continue;
        }

        if (insideString)
            continue;

        if (c == '{')
        {
            depth++;
        }
        else if (c == '}')
        {
            depth--;

            if (depth == 0)
            {
                return text.Substring(start, i - start + 1);
            }
        }
    }

    return null;
}


static void ValidateQuestions(List<GpqaQuestion> questions)
{
    if (questions.Count == 0)
    {
        throw new Exception("Dataset is empty.");
    }

    var badQuestions = questions
        .Where(q =>
            string.IsNullOrWhiteSpace(q.Question) ||
            string.IsNullOrWhiteSpace(q.A) ||
            string.IsNullOrWhiteSpace(q.B) ||
            string.IsNullOrWhiteSpace(q.C) ||
            string.IsNullOrWhiteSpace(q.D) ||
            q.Answer is not ("A" or "B" or "C" or "D"))
        .Take(10)
        .ToList();

    if (badQuestions.Count > 0)
    {
        Console.WriteLine("Bad loaded questions examples:");

        foreach (var q in badQuestions)
        {
            Console.WriteLine();
            Console.WriteLine($"Index: {q.Index}");
            Console.WriteLine($"Question: {q.Question}");
            Console.WriteLine($"A: {q.A}");
            Console.WriteLine($"B: {q.B}");
            Console.WriteLine($"C: {q.C}");
            Console.WriteLine($"D: {q.D}");
            Console.WriteLine($"Answer: {q.Answer}");
        }

        throw new Exception(
            "Some questions were loaded incorrectly. Check CSV headers and mapping.");
    }
}


static async Task SaveResults(List<ResultRow> rows, double accuracy)
{
    string time = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

    string csvPath = $"Results/gpqa_{time}.csv";
    string jsonPath = $"Results/gpqa_{time}.json";
    string summaryPath = $"Results/summary_{time}.txt";

    await using (var writer = new StreamWriter(csvPath, false, Encoding.UTF8))
    await using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
    {
        await csv.WriteRecordsAsync(rows);
    }

    await File.WriteAllTextAsync(
        jsonPath,
        JsonSerializer.Serialize(
            rows,
            new JsonSerializerOptions
            {
                WriteIndented = true
            }),
        Encoding.UTF8);

    int total = rows.Count;
    int correct = rows.Count(x => x.Correct);
    int incorrect = rows.Count(x => !x.Correct);
    int errors = rows.Count(x => !string.IsNullOrWhiteSpace(x.Error));

    await File.WriteAllTextAsync(
        summaryPath,
$"""
Dataset: GPQA Main

Questions: {total}
Correct: {correct}
Incorrect: {incorrect}
Errors: {errors}

Accuracy: {accuracy:F2}%
""",
        Encoding.UTF8);

    Console.WriteLine();
    Console.WriteLine($"Saved CSV: {csvPath}");
    Console.WriteLine($"Saved JSON: {jsonPath}");
    Console.WriteLine($"Saved summary: {summaryPath}");
}


public static class JsonOptions
{
    public static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };
}


public sealed class GpqaCsvMap : ClassMap<GpqaCsvRow>
{
    public GpqaCsvMap()
    {
        Map(m => m.Question)
            .Name("Question", "question");

        Map(m => m.CorrectAnswer)
            .Name("Correct Answer", "CorrectAnswer", "correct_answer", "correct answer");

        Map(m => m.IncorrectAnswer1)
            .Name("Incorrect Answer 1", "IncorrectAnswer1", "incorrect_answer_1", "incorrect answer 1");

        Map(m => m.IncorrectAnswer2)
            .Name("Incorrect Answer 2", "IncorrectAnswer2", "incorrect_answer_2", "incorrect answer 2");

        Map(m => m.IncorrectAnswer3)
            .Name("Incorrect Answer 3", "IncorrectAnswer3", "incorrect_answer_3", "incorrect answer 3");
    }
}


public class GpqaCsvRow
{
    public string Question { get; set; } = "";

    public string CorrectAnswer { get; set; } = "";

    public string IncorrectAnswer1 { get; set; } = "";

    public string IncorrectAnswer2 { get; set; } = "";

    public string IncorrectAnswer3 { get; set; } = "";

    public GpqaQuestion ToQuestion(int index)
    {
        var answers = new List<AnswerOption>
        {
            new(CorrectAnswer.Trim(), true),
            new(IncorrectAnswer1.Trim(), false),
            new(IncorrectAnswer2.Trim(), false),
            new(IncorrectAnswer3.Trim(), false)
        };

        Shuffle(answers);

        int correctIndex = answers.FindIndex(x => x.IsCorrect);

        string correctLetter = correctIndex switch
        {
            0 => "A",
            1 => "B",
            2 => "C",
            3 => "D",
            _ => ""
        };

        return new GpqaQuestion
        {
            Index = index,
            Question = Question.Trim(),
            A = answers[0].Text,
            B = answers[1].Text,
            C = answers[2].Text,
            D = answers[3].Text,
            Answer = correctLetter
        };
    }

    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);

            if (i != j)
            {
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}


public sealed record AnswerOption(string Text, bool IsCorrect);


public class GpqaQuestion
{
    public int Index { get; set; }

    public string Question { get; set; } = "";

    public string A { get; set; } = "";

    public string B { get; set; } = "";

    public string C { get; set; } = "";

    public string D { get; set; } = "";

    public string Answer { get; set; } = "";
}


public class ResultRow
{
    public int Index { get; set; }

    public string Question { get; set; } = "";

    public string A { get; set; } = "";

    public string B { get; set; } = "";

    public string C { get; set; } = "";

    public string D { get; set; } = "";

    public string Expected { get; set; } = "";

    public string Actual { get; set; } = "";

    public bool Correct { get; set; }

    public string RawResponse { get; set; } = "";

    public string Error { get; set; } = "";
}


public class SubmitResponse
{
    public string? id { get; set; }
}


public class ResultResponse
{
    public string? status { get; set; }

    public string? result { get; set; }

    public string? error { get; set; }
}
