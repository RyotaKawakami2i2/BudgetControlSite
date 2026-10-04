using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Infrastructure.Persistence;
using TaskYojitsu.Web.Security;

namespace TaskYojitsu.Web.Endpoints;

/// <summary>業務の画面（SPA）の配信と、監視用の状態確認。</summary>
public static class AppEndpoints
{
    public static void MapAppShell(this IEndpointRouteBuilder app, IWebHostEnvironment environment)
    {
        app.MapGet("/", () => Results.Redirect("/app")).AllowAnonymous();

        // /app 以下のすべての URL に同じ index.html を返す（ログインが必要。未ログインならログインの画面へ移る）
        var indexPath = Path.Combine(environment.WebRootPath ?? "wwwroot", "app", "index.html");
        app.MapGet("/app/{**path}", (string? path, HttpContext http) =>
        {
            if (path is not null && Path.HasExtension(path))
            {
                // 見つからない静的ファイル
                return Results.NotFound();
            }

            if (!File.Exists(indexPath))
            {
                return Results.Content(
                    "<!doctype html><html lang=\"ja\"><title>タスク予実管理</title><p>画面のファイルがありません。web/ で pnpm build を実行してください。</p></html>",
                    "text/html; charset=utf-8");
            }

            XsrfCookie.Issue(http);
            return Results.File(indexPath, "text/html; charset=utf-8");
        }).RequireAuthorization();

        // 監視用（nginx で監視サーバーからの要求だけを通す。詳細設計書 5.3）
        app.MapGet("/health/live", () => Results.Text("ok")).AllowAnonymous();
        app.MapGet("/health/ready", [AllowAnonymous] async (AppDbContext db, CancellationToken ct) =>
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
                return Results.Text("ok");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Text("db unavailable", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
    }
}
