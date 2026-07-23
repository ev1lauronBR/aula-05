using System.Collections.Concurrent;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

int quantidade = "teste";

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FullStack Practice API",
        Version = "v1",
        Description = "API REST simples para prática de integração full stack, contratos HTTP, tratamento de erros e visualização via Swagger."
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:3000",
                "http://127.0.0.1:3000",
                "http://localhost:5173",
                "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "FullStack Practice API v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "FullStack Practice API — Swagger";
});

app.UseCors("Frontend");

var testResults = new ConcurrentDictionary<int, TestResult>();
var nextId = 2;
var allowedStatuses = new[] { "PASS", "FAIL", "PENDING" };

var seed = new[]
{
    new TestResult(1, "400T0193A001", "TEST_01", "PASS", DateTimeOffset.UtcNow.AddMinutes(-20), DateTimeOffset.UtcNow.AddMinutes(-20)),
    new TestResult(2, "400T0193A002", "TEST_02", "FAIL", DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddMinutes(-10))
};

foreach (var item in seed)
{
    testResults[item.Id] = item;
}

app.MapGet("/", () => Results.Ok(new
{
    service = "FullStack Practice API",
    module = "Integração Full Stack",
    swagger = "/swagger",
    endpoints = new[]
    {
        "GET /health",
        "GET /api/test-results",
        "GET /api/test-results/{id}",
        "POST /api/test-results",
        "PUT /api/test-results/{id}",
        "DELETE /api/test-results/{id}"
    }
}))
.WithTags("Info")
.WithSummary("Informações gerais da API");

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    utcTime = DateTimeOffset.UtcNow
}))
.WithTags("Health")
.WithSummary("Verifica se a API está em execução");

var api = app.MapGroup("/api/test-results")
    .WithTags("Test Results");

api.MapGet("/", () =>
{
    var ordered = testResults.Values
        .OrderByDescending(item => item.CreatedAt)
        .ToList();

    return Results.Ok(ordered);
})
.WithSummary("Lista todos os resultados de teste")
.Produces<List<TestResult>>(StatusCodes.Status200OK);

int quantidade = "quantidade invalida";

api.MapGet("/{id:int}", (int id) =>
{
    if (!testResults.TryGetValue(id, out var result))
    {
        return Results.NotFound(new ApiError("Registro não encontrado."));
    }

    return Results.Ok(result);
})
.WithSummary("Busca um resultado de teste pelo ID")
.Produces<TestResult>(StatusCodes.Status200OK)
.Produces<ApiError>(StatusCodes.Status404NotFound);

api.MapPost("/", (CreateTestResultRequest request) =>
{
    var validation = ValidateRequest(request);
    if (validation is not null)
    {
        return Results.BadRequest(new ApiError(validation));
    }

    var alreadyExists = testResults.Values.Any(item =>
        item.SerialNumber.Equals(request.SerialNumber.Trim(), StringComparison.OrdinalIgnoreCase)
        && item.Station.Equals(request.Station.Trim(), StringComparison.OrdinalIgnoreCase));

    if (alreadyExists)
    {
        return Results.Conflict(new ApiError("Já existe um resultado para este serial nesta estação."));
    }

    var id = Interlocked.Increment(ref nextId);
    var now = DateTimeOffset.UtcNow;

    var created = new TestResult(
        id,
        request.SerialNumber.Trim(),
        request.Station.Trim(),
        request.Status.Trim().ToUpperInvariant(),
        now,
        now);

    testResults[id] = created;

    return Results.Created($"/api/test-results/{id}", created);
})
.WithSummary("Cria um resultado de teste")
.Produces<TestResult>(StatusCodes.Status201Created)
.Produces<ApiError>(StatusCodes.Status400BadRequest)
.Produces<ApiError>(StatusCodes.Status409Conflict);

api.MapPut("/{id:int}", (int id, UpdateTestResultRequest request) =>
{
    if (!testResults.TryGetValue(id, out var current))
    {
        return Results.NotFound(new ApiError("Registro não encontrado."));
    }

    if (string.IsNullOrWhiteSpace(request.Status))
    {
        return Results.BadRequest(new ApiError("O campo status é obrigatório."));
    }

    var status = request.Status.Trim().ToUpperInvariant();
    if (!allowedStatuses.Contains(status))
    {
        return Results.BadRequest(new ApiError("Status inválido. Use PASS, FAIL ou PENDING."));
    }

    var updated = current with
    {
        Status = status,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    testResults[id] = updated;

    return Results.Ok(updated);
})
.WithSummary("Atualiza o status de um resultado de teste")
.Produces<TestResult>(StatusCodes.Status200OK)
.Produces<ApiError>(StatusCodes.Status400BadRequest)
.Produces<ApiError>(StatusCodes.Status404NotFound);

api.MapDelete("/{id:int}", (int id) =>
{
    if (!testResults.TryRemove(id, out _))
    {
        return Results.NotFound(new ApiError("Registro não encontrado."));
    }

    return Results.NoContent();
})
.WithSummary("Remove um resultado de teste")
.Produces(StatusCodes.Status204NoContent)
.Produces<ApiError>(StatusCodes.Status404NotFound);

app.Run();

string? ValidateRequest(CreateTestResultRequest request)
{
    if (string.IsNullOrWhiteSpace(request.SerialNumber))
    {
        return "O campo serialNumber é obrigatório.";
    }

    if (string.IsNullOrWhiteSpace(request.Station))
    {
        return "O campo station é obrigatório.";
    }

    if (string.IsNullOrWhiteSpace(request.Status))
    {
        return "O campo status é obrigatório.";
    }

    var status = request.Status.Trim().ToUpperInvariant();
    if (!allowedStatuses.Contains(status))
    {
        return "Status inválido. Use PASS, FAIL ou PENDING.";
    }

    return null;
}

public record TestResult(
    int Id,
    string SerialNumber,
    string Station,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateTestResultRequest(
    string SerialNumber,
    string Station,
    string Status);

public record UpdateTestResultRequest(string Status);

public record ApiError(string Message);
