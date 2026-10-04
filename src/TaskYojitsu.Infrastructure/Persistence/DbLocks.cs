using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;

namespace TaskYojitsu.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL のロック（pg_advisory_xact_lock）。トランザクションの中で取り、トランザクションの終わりに自動で外れる。
/// 鍵の名前は hashtextextended で 64 ビットの数にする。
/// </summary>
public sealed class DbLocks(AppDbContext db) : IDbLocks
{
    public Task LockTeamHierarchyAsync(Guid teamId, CancellationToken cancellationToken) =>
        LockAsync($"team-hierarchy:{teamId}", cancellationToken);

    public Task LockUserDayAsync(Guid userId, DateOnly workDate, CancellationToken cancellationToken) =>
        LockAsync($"user-day:{userId}:{workDate:yyyy-MM-dd}", cancellationToken);

    public async Task LockAsync(string name, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("ロックはトランザクションの中で取ってください。");
        }

        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({name}, 0))", cancellationToken);
    }
}
