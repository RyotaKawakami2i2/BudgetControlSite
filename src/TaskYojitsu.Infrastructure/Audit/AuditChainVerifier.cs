using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Infrastructure.Persistence;

namespace TaskYojitsu.Infrastructure.Audit;

/// <summary>検証の結果。firstBrokenId は最初に食い違った記録の ID。</summary>
public sealed record AuditVerifyResult(long Checked, long? FirstBrokenId, string? Reason)
{
    public bool Ok => FirstBrokenId is null;
}

/// <summary>
/// 監査ログのハッシュ連鎖の検証（詳細設計書 3.5、JOB-04、verify-audit-log）。
/// ID の順に読み、prev_hash が直前の hash と一致すること、hash = SHA-256(prev_hash ∥ 本文) であることを確かめる。
/// 本文の文字列は、トリガーと同じ関数（tyj.audit_log_body）で DB に作らせ、ハッシュはアプリで計算し直す。
/// </summary>
public sealed class AuditChainVerifier(AppDbContext db)
{
    private const int BatchSize = 2000;

    /// <summary>fromId より後の記録を検証する。fromId が null なら、保存期間で削除した後の起点から全体を検証する。</summary>
    public async Task<AuditVerifyResult> VerifyAsync(long? fromId, CancellationToken ct)
    {
        byte[] expectedPrev;
        long cursor;
        if (fromId is { } start && start > 0)
        {
            var row = await db.Database
                .SqlQuery<HashRow>($"SELECT id AS \"Id\", hash AS \"Hash\" FROM tyj.audit_logs WHERE id <= {start} ORDER BY id DESC LIMIT 1")
                .SingleOrDefaultAsync(ct);
            if (row is null)
            {
                return await VerifyAsync(null, ct);
            }

            expectedPrev = row.Hash;
            cursor = row.Id;
        }
        else
        {
            var anchor = await db.Database
                .SqlQuery<HashRow>($"SELECT anchor_id AS \"Id\", anchor_hash AS \"Hash\" FROM tyj.audit_chain_anchor()")
                .SingleAsync(ct);
            expectedPrev = anchor.Hash;
            cursor = anchor.Id;
        }

        long checkedCount = 0;
        while (true)
        {
            var batch = await db.Database
                .SqlQuery<BodyRow>($"""
                    SELECT a.id AS "Id", a.prev_hash AS "PrevHash", a.hash AS "Hash", tyj.audit_log_body(a) AS "Body"
                    FROM tyj.audit_logs a
                    WHERE a.id > {cursor}
                    ORDER BY a.id
                    LIMIT {BatchSize}
                    """)
                .ToListAsync(ct);
            if (batch.Count == 0)
            {
                return new AuditVerifyResult(checkedCount, null, null);
            }

            foreach (var row in batch)
            {
                if (!CryptographicOperations.FixedTimeEquals(row.PrevHash, expectedPrev))
                {
                    return new AuditVerifyResult(checkedCount, row.Id, "prev_hash が直前の記録のハッシュ値と一致しません");
                }

                var computed = ComputeHash(row.PrevHash, row.Body);
                if (!CryptographicOperations.FixedTimeEquals(row.Hash, computed))
                {
                    return new AuditVerifyResult(checkedCount, row.Id, "記録の内容とハッシュ値が一致しません");
                }

                expectedPrev = row.Hash;
                cursor = row.Id;
                checkedCount++;
            }
        }
    }

    public static byte[] ComputeHash(byte[] prevHash, string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var buffer = new byte[prevHash.Length + bodyBytes.Length];
        prevHash.CopyTo(buffer, 0);
        bodyBytes.CopyTo(buffer, prevHash.Length);
        return SHA256.HashData(buffer);
    }

    private sealed record HashRow(long Id, byte[] Hash);

    private sealed record BodyRow(long Id, byte[] PrevHash, byte[] Hash, string Body);
}
