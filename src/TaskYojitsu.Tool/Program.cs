using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskYojitsu.Application;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Infrastructure;
using TaskYojitsu.Infrastructure.Identity;
using TaskYojitsu.Tool;

// 運用コマンド（基本設計書 6.4）。使い方: taskyojitsu-tool <コマンド> [--名前 値 ...]
if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Commands.Usage);
    return 0;
}

var builder = Host.CreateApplicationBuilder(args[1..]);
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);
builder.Configuration.AddEnvironmentVariables();

var config = builder.Configuration;
var isDevelopment = builder.Environment.IsDevelopment()
    || string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);

builder.Services.Configure<AppOptions>(config.GetSection(AppOptions.Section));
builder.Services.Configure<SecurityOptions>(config.GetSection(SecurityOptions.Section));
builder.Services.Configure<BusinessOptions>(_ => { });
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(config, isDevelopment);
builder.Services.AddAppIdentityCore(config);
builder.Services.AddSingleton<CliRequestContext>();
builder.Services.AddSingleton<IRequestContext>(sp => sp.GetRequiredService<CliRequestContext>());

using var host = builder.Build();
var options = CommandOptions.Parse(args[1..]);
try
{
    return await Commands.RunAsync(args[0], options, host.Services, config, isDevelopment);
}
catch (CommandException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
catch (AppException ex)
{
    var detail = ex is ValidationException v
        ? string.Join(", ", v.Errors.Select(e => $"{e.Key}: {string.Join("/", e.Value)}"))
        : ex is RuleViolationException r ? string.Join(", ", r.MessageIds) : ex.Code;
    Console.Error.WriteLine($"処理できませんでした（{ex.Code}: {detail}）");
    return 1;
}
