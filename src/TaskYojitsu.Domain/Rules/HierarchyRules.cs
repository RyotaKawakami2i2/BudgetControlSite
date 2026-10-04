namespace TaskYojitsu.Domain.Rules;

/// <summary>タスクの親子の決まり（要件定義書 FR-TSK-04、詳細設計書 4.8）。</summary>
public static class HierarchyRules
{
    /// <summary>階層の深さの上限。</summary>
    public const int MaxDepth = 4;

    /// <summary>並び順の刻み。</summary>
    public const int SortStep = 1024;

    /// <summary>
    /// 深さ parentDepth の親の下（最上位なら 0）に、高さ subtreeHeight の部分木（子を持たないタスクは 1）を置けるか。
    /// </summary>
    public static bool CanPlace(int parentDepth, int subtreeHeight) => parentDepth + subtreeHeight <= MaxDepth;

    /// <summary>
    /// 前後の並び順の間に置く値を求める。間が 2 未満なら null を返す（同じ親の子を振り直す）。
    /// </summary>
    public static int? Between(int? before, int? after)
    {
        if (before is null && after is null)
        {
            return SortStep;
        }

        if (before is null)
        {
            return after!.Value - SortStep;
        }

        if (after is null)
        {
            return before.Value + SortStep;
        }

        var gap = (long)after.Value - before.Value;
        return gap < 2 ? null : (int)(before.Value + gap / 2);
    }
}
