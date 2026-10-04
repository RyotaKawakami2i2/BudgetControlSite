using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Infrastructure.Persistence;

namespace TaskYojitsu.Web.Jobs;

/// <summary>実行の時刻（日本時間）。</summary>
public abstract record JobSchedule
{
    public abstract DateTime NextAfter(DateTime localNow);

    /// <summary>毎日 hour:minute。</summary>
    public sealed record Daily(int Hour, int Minute) : JobSchedule
    {
        public override DateTime NextAfter(DateTime localNow)
        {
            var today = localNow.Date.AddHours(Hour).AddMinutes(Minute);
            return today > localNow ? today : today.AddDays(1);
        }
    }

    /// <summary>一定の間隔ごと。</summary>
    public sealed record Every(TimeSpan Interval) : JobSchedule
    {
        public override DateTime NextAfter(DateTime localNow)
        {
            var ticks = Interval.Ticks;
            return new DateTime((localNow.Ticks / ticks + 1) * ticks, localNow.Kind);
        }
    }
}

/// <summary>定期処理（詳細設計書 9章）。</summary>
public interface IScheduledJob
{
    string Id { get; }

    JobSchedule Schedule { get; }

    Task RunAsync(IServiceProvider services, CancellationToken cancellationToken);
}

/// <summary>
/// 定期処理をアプリの中で動かす。同じ処理が同時に 2 つ動かないよう、DB のロック（pg_try_advisory_lock）を取る。
/// 失敗しても次の回に処理する。
/// </summary>
public sealed class JobScheduler(
    IServiceScopeFactory scopes,
    IEnumerable<IScheduledJob> jobs,
    BusinessClock clock,
    IConfiguration config,
    ILogger<JobScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Jobs:Enabled", true))
        {
            return;
        }

        var next = jobs.ToDictionary(j => j.Id, j => j.Schedule.NextAfter(clock.ToLocal(clock.UtcNow)));
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = clock.ToLocal(clock.UtcNow);
            foreach (var job in jobs)
            {
                if (now < next[job.Id])
                {
                    continue;
                }

                next[job.Id] = job.Schedule.NextAfter(now);
                await RunOnceAsync(job, stoppingToken);
            }
        }
    }

    /// <summary>1 回動かす（運用コマンドやテストからも使う）。</summary>
    public async Task<bool> RunOnceAsync(IScheduledJob job, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        try
        {
            await using var tryLock = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended(@key, 0))", connection);
            tryLock.Parameters.AddWithValue("key", $"job:{job.Id}");
            if (await tryLock.ExecuteScalarAsync(ct) is not true)
            {
                return false;
            }

            try
            {
                logger.LogInformation("定期処理 {Job} を開始します", job.Id);
                await job.RunAsync(scope.ServiceProvider, ct);
                logger.LogInformation("定期処理 {Job} が終わりました", job.Id);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "定期処理 {Job} が失敗しました", job.Id);
                return false;
            }
            finally
            {
                await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@key, 0))", connection);
                unlock.Parameters.AddWithValue("key", $"job:{job.Id}");
                await unlock.ExecuteScalarAsync(CancellationToken.None);
            }
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}

public static class JobRegistration
{
    public static IServiceCollection AddJobs(this IServiceCollection services)
    {
        services.AddSingleton<IScheduledJob, DueNotificationJob>();
        services.AddSingleton<IScheduledJob, PurgeDeletedTasksJob>();
        services.AddSingleton<IScheduledJob, CleanupExpiredJob>();
        services.AddSingleton<IScheduledJob, AuditVerifyJob>();
        services.AddSingleton<IScheduledJob, SecurityDetectionJob>();
        services.AddSingleton<IScheduledJob, NotificationCleanupJob>();
        return services;
    }
}

internal static class JobOptions
{
    public static BusinessOptions Business(IServiceProvider services) => services.GetRequiredService<IOptions<BusinessOptions>>().Value;
}
