using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);

// CORS for React dev server (Vite)
builder.Services.AddCors(options =>
{
    options.AddPolicy("dev", p =>
        p.WithOrigins("http://localhost:5173")
         .AllowAnyHeader()
         .AllowAnyMethod());
});

// HttpClient for OpenRouter
builder.Services.AddHttpClient("openrouter", client =>
{
    //client.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

var app = builder.Build();

app.UseCors("dev");

app.MapGet("/", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/generate-sql", async (
    GenerateSqlRequest req,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Prompt))
        return Results.BadRequest("Prompt is required.");

    var apiKey = config["OpenRouter:ApiKey"];
    var model = config["OpenRouter:Model"] ?? "mistralai/devstral-small-2505:free";

    if (string.IsNullOrWhiteSpace(apiKey))
        return Results.Problem("Missing OpenRouter API key. Set user-secrets OpenRouter:ApiKey.");

    var systemPrompt = """
You generate ONLY Microsoft SQL Server T-SQL.
Return ONLY the SQL query text (no markdown, no backticks, no explanation).
Rules:
- SELECT statements only. No INSERT/UPDATE/DELETE/MERGE/DROP/ALTER/CREATE/EXEC.
- Use TOP (100) by default unless the user asks for a different limit.
- If the request is ambiguous, make a reasonable assumption and still return a SELECT query.
""";

    var payload = new
    {
        model,
        messages = new object[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = req.Prompt }
        },
        temperature = 0.2
    };

    var http = httpClientFactory.CreateClient("openrouter");
    // Change this line
    using var httpReq = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
    //using var httpReq = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
    httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    // Optional attribution headers
    httpReq.Headers.TryAddWithoutValidation("HTTP-Referer", "http://localhost:5173");
    httpReq.Headers.TryAddWithoutValidation("X-Title", "Text2SQL MVP");

    httpReq.Content = new StringContent(
        JsonSerializer.Serialize(payload),
        Encoding.UTF8,
        "application/json"
    );

    using var res = await http.SendAsync(httpReq, ct);
    var body = await res.Content.ReadAsStringAsync(ct);

    if (!res.IsSuccessStatusCode)
        return Results.Problem($"OpenRouter error ({(int)res.StatusCode}): {body}");

    using var doc = JsonDocument.Parse(body);

    // OpenRouter returns OpenAI-style response: choices[0].message.content
    var content = doc.RootElement
        .GetProperty("choices")[0]
        .GetProperty("message")
        .GetProperty("content")
        .GetString() ?? "";

    var sql = PostProcessSql(content);

    if (ContainsForbidden(sql))
        return Results.BadRequest("Model returned unsafe SQL (non-SELECT). Try rephrasing.");

    sql = EnsureTop(sql, 100);

    return Results.Ok(new { sql });
});

app.MapGet("/api/test-db", async (IConfiguration config) =>
{
    var connStr = config.GetConnectionString("SqlServer");
    if (string.IsNullOrWhiteSpace(connStr))
        return Results.Problem("Missing ConnectionStrings:SqlServer");

    await using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();

    // simple proof that DB works
    await using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.Orders;", conn);
    var count = (int)await cmd.ExecuteScalarAsync();

    return Results.Ok(new { ok = true, ordersCount = count });
});

app.MapPost("/api/generate-and-run", async (
    GenerateAndRunRequest req,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Prompt))
        return Results.BadRequest("Prompt is required.");

    var apiKey = config["OpenRouter:ApiKey"];
    var model = config["OpenRouter:Model"] ?? "mistralai/devstral-small-2505:free";
    if (string.IsNullOrWhiteSpace(apiKey))
        return Results.Problem("Missing OpenRouter API key.");

    var connStr = config.GetConnectionString("SqlServer");
    if (string.IsNullOrWhiteSpace(connStr))
        return Results.Problem("Missing ConnectionStrings:SqlServer");

    // IMPORTANT: give the model the schema so it generates correct SQL
    //var schema = await GetDynamicSchema(connectionString);
    var schema = """
Database: Text2SqlDemo
Table: dbo.Orders(Id int, CustomerName nvarchar(100), OrderDate date, TotalAmount decimal(10,2))
""";

    var systemPrompt = """
You generate ONLY Microsoft SQL Server T-SQL.
Return ONLY the SQL query text (no markdown, no backticks, no explanation).
Rules:
- Use only this database and table: Text2SqlDemo.dbo.Orders
- SELECT statements only. No INSERT/UPDATE/DELETE/MERGE/DROP/ALTER/CREATE/EXEC.
- Single statement only (no semicolons).
- Use TOP (50) by default unless the user asks for a different limit.
""";

    var payload = new
    {
        model,
        messages = new object[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = $"User request:\n{req.Prompt}\n\nSchema:\n{schema}" }
        },
        temperature = 0.2
    };

    // 1) Call OpenRouter
    var http = httpClientFactory.CreateClient("openrouter");
    using var httpReq = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
    httpReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    httpReq.Headers.TryAddWithoutValidation("HTTP-Referer", "http://localhost:5173");
    httpReq.Headers.TryAddWithoutValidation("X-Title", "Text2SQL MVP");
    httpReq.Content = new StringContent(
        System.Text.Json.JsonSerializer.Serialize(payload),
        System.Text.Encoding.UTF8,
        "application/json"
    );

    using var res = await http.SendAsync(httpReq, ct);
    var body = await res.Content.ReadAsStringAsync(ct);

    if (!res.IsSuccessStatusCode)
        return Results.Problem($"OpenRouter error ({(int)res.StatusCode}): {body}");

    using var doc = System.Text.Json.JsonDocument.Parse(body);
    var content = doc.RootElement
        .GetProperty("choices")[0]
        .GetProperty("message")
        .GetProperty("content")
        .GetString() ?? "";

    var sql = PostProcessSql(content);

    // 2) Guardrails (MVP)
    if (!IsSingleSelect(sql))
        return Results.BadRequest("Model returned unsafe SQL. Only single SELECT is allowed.");

    sql = EnsureTop(sql, 50);

    // 3) Execute
    try
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(connStr);
        await conn.OpenAsync(ct);

        await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn)
        {
            CommandTimeout = 15
        };

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var columns = Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToArray();

        var rows = new List<object?[]>();
        const int maxRows = 50;

        while (await reader.ReadAsync(ct) && rows.Count < maxRows)
        {
            var row = new object?[reader.FieldCount];
            reader.GetValues(row);
            rows.Add(row);
        }

        return Results.Ok(new { sql, columns, rows });
    }
    catch (Exception ex)
    {
        return Results.Problem($"SQL execution failed: {ex.Message}");
    }
});
//It ensures that if you add new tables in your DB tomorrow, 
//you don't have to touch your C# code at all—the AI will "see" the new tables immediately.
// async Task<string> GetDynamicSchema(string connectionString)
// {
//     using var conn = new SqlConnection(connectionString);
//     await conn.OpenAsync();

//     // This query pulls every table and column name in your DB
//     var query = @"
//         SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE 
//         FROM INFORMATION_SCHEMA.COLUMNS 
//         WHERE TABLE_SCHEMA = 'dbo'
//         ORDER BY TABLE_NAME, ORDINAL_POSITION";

//     using var cmd = new SqlCommand(query, conn);
//     using var reader = await cmd.ExecuteReaderAsync();

//     var sb = new StringBuilder("Database: Text2SqlDemo\n");
//     string currentTable = "";

//     while (await reader.ReadAsync())
//     {
//         string tableName = reader["TABLE_NAME"].ToString();
//         if (tableName != currentTable)
//         {
//             if (currentTable != "") sb.Append(")\n");
//             sb.Append($"Table: dbo.{tableName}(");
//             currentTable = tableName;
//         }
//         else { sb.Append(", "); }

//         sb.Append($"{reader["COLUMN_NAME"]} {reader["DATA_TYPE"]}");
//     }
//     sb.Append(")");
//     return sb.ToString();
// }

// Request type


app.Run();

static string PostProcessSql(string text)
{
    var t = text.Trim();

    // Strip markdown fences if model includes them
    t = Regex.Replace(t, @"^\s*```[a-zA-Z]*\s*", "", RegexOptions.Multiline);
    t = Regex.Replace(t, @"\s*```\s*$", "", RegexOptions.Multiline);

    return t.Trim();
}

static bool ContainsForbidden(string sql)
{
    var s = sql.ToUpperInvariant();
    string[] forbidden =
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "DROP", "ALTER", "CREATE",
        "EXEC", "EXECUTE", "TRUNCATE", "GRANT", "REVOKE"
    };
    return forbidden.Any(s.Contains);
}

static bool IsSingleSelect(string sql)
{
    var s = sql.Trim();

    // quick block multi-statement
    if (s.Contains(';')) return false;

    // allow SELECT or WITH ... SELECT
    var okStart = System.Text.RegularExpressions.Regex.IsMatch(s, @"^\s*(WITH\b[\s\S]+?\bSELECT\b|SELECT\b)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (!okStart) return false;

    // block obvious dangerous keywords
    string[] forbidden =
    {
        "INSERT","UPDATE","DELETE","MERGE","DROP","ALTER","CREATE",
        "EXEC","EXECUTE","TRUNCATE","GRANT","REVOKE"
    };

    var up = s.ToUpperInvariant();
    return !forbidden.Any(up.Contains);
}


static string EnsureTop(string sql, int top)
{
    var t = sql.Trim();

    // If it already has TOP, leave it
    if (Regex.IsMatch(t, @"\bTOP\s*\(", RegexOptions.IgnoreCase))
        return t;

    // Insert TOP after SELECT or SELECT DISTINCT
    var m = Regex.Match(t, @"^\s*SELECT\s+(DISTINCT\s+)?", RegexOptions.IgnoreCase);
    if (!m.Success) return t;

    var insertPos = m.Index + m.Length;
    return t.Insert(insertPos, $"TOP ({top}) ");
}

public sealed record GenerateSqlRequest(string Prompt);
public sealed record GenerateAndRunRequest(string Prompt);
