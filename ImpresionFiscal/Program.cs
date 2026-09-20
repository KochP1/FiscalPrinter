using ImpresionFiscal.Services;
using Microsoft.Extensions.Hosting;

Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(args);

// --- CONFIGURACIÓN DE KESTREL PARA HTTPS ---
builder.WebHost.ConfigureKestrel(options =>
{
    string certPath = Path.Combine(AppContext.BaseDirectory, "localhost.pfx");

    options.ListenAnyIP(7249, listenOptions =>
    {
        if (File.Exists(certPath))
        {
            listenOptions.UseHttps(certPath, "1234");
        }
        else
        {
            listenOptions.UseHttps(); // Fallback
        }
    });
});

builder.Host.UseWindowsService();
builder.Services.AddControllers();
builder.Services.AddSingleton<PrinterService>();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info = new()
        {
            Title = "API de Impresora Fiscal",
            Version = "v1.26.0914",
            Description = "API para gestionar impresión de facturas fiscales",
            Contact = new()
            {
                Name = "Soporte ELZYRA",
            }
        };
        return Task.CompletedTask;
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/{documentName}.json", "API Impresion v1.26.0903");
    });
}

// 1. Middleware de Private Network Access (PNA)
app.Use(async (context, next) =>
{
    // Detectar si el navegador está solicitando acceso a red privada
    if (context.Request.Headers.ContainsKey("Access-Control-Request-Private-Network"))
    {
        context.Response.Headers.Append("Access-Control-Allow-Private-Network", "true");
    }

    // Responder 200 OK inmediatamente a las peticiones OPTIONS preflight si no son interceptadas
    if (context.Request.Method == "OPTIONS")
    {
        context.Response.StatusCode = 200;
    }

    await next();
});

// 2. Middleware de CORS (Aplica después de inyectar las cabeceras PNA)
app.UseCors("AllowAll");

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();