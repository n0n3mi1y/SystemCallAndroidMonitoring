using System.Security.Cryptography.X509Certificates;
using Serilog;
using Serilog.Events;
using SystemCallAndroidMonitoring.Titanium;
using static System.Net.WebRequestMethods;


public class Program
{
    

    public static async Task Main(string[] args)
    {
        // Настройка Serilog перед созданием builder
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug() // Или Verbose для максимальной детализации
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .WriteTo.Console(
                restrictedToMinimumLevel: LogEventLevel.Debug, // Убедитесь, что уровень Debug включен
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File("logs/log-.txt",
                restrictedToMinimumLevel: LogEventLevel.Debug,
                rollingInterval: RollingInterval.Day)
            .CreateLogger();

        string host = "http://localhost:65002";

        try
        {
            Log.Information("Запуск API");

            var builder = WebApplication.CreateBuilder(args);

            // Заменяем стандартный логгер на Serilog
            builder.Logging.ClearProviders();
            builder.Host.UseSerilog(); // Используем Serilog вместо стандартного логгера

            // Add services to the container.
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseAuthorization();

            app.MapControllers();
            //Log.Information($"API развернуто на {host}");
            await app.RunAsync(host);
            
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application start-up failed");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
