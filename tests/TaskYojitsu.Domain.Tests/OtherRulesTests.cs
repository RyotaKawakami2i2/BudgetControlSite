using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Domain.Tests;

/// <summary>階層と並び順（詳細設計書 4.8）。</summary>
public class HierarchyRulesTests
{
    [Theory]
    [InlineData(0, 4, true)]
    [InlineData(1, 3, true)]
    [InlineData(1, 4, false)]
    [InlineData(3, 1, true)]
    [InlineData(4, 1, false)]
    public void CanPlace_親の深さと部分木の高さの合計が4まで(int parentDepth, int height, bool expected)
    {
        Assert.Equal(expected, HierarchyRules.CanPlace(parentDepth, height));
    }

    [Theory]
    [InlineData(null, null, 1024)]
    [InlineData(null, 1024, 0)]
    [InlineData(1024, null, 2048)]
    [InlineData(1024, 2048, 1536)]
    [InlineData(1024, 1026, 1025)]
    [InlineData(int.MaxValue - 2, int.MaxValue, int.MaxValue - 1)]
    public void Between_前後の中間の値(int? before, int? after, int? expected)
    {
        Assert.Equal(expected, HierarchyRules.Between(before, after));
    }

    [Theory]
    [InlineData(1024, 1025)]
    [InlineData(1024, 1024)]
    public void Between_間が2未満なら振り直しを求める(int before, int after)
    {
        Assert.Null(HierarchyRules.Between(before, after));
    }
}

/// <summary>工数の按分と差（詳細設計書 7.3.7）。2026-10-05 は月曜日。</summary>
public class EffortRulesTests
{
    private static readonly DateOnly Mon = new(2026, 10, 5);

    [Fact]
    public void ProratedMinutes_期間と重なる稼働日数で按分する()
    {
        // 月〜金の 5 日で 600 分。水〜日と重なるのは 3 日
        var minutes = EffortRules.ProratedMinutes(WorkingCalendar.Empty, Mon, Mon.AddDays(4), 600, Mon.AddDays(2), Mon.AddDays(6));

        Assert.Equal(360, minutes, precision: 6);
    }

    [Fact]
    public void ProratedMinutes_重ならない_または予定がなければ0()
    {
        Assert.Equal(0, EffortRules.ProratedMinutes(WorkingCalendar.Empty, Mon, Mon.AddDays(4), 600, Mon.AddDays(7), Mon.AddDays(8)));
        Assert.Equal(0, EffortRules.ProratedMinutes(WorkingCalendar.Empty, null, null, 600, Mon, Mon.AddDays(8)));
        Assert.Equal(0, EffortRules.ProratedMinutes(WorkingCalendar.Empty, Mon, Mon, null, Mon, Mon));
    }

    [Fact]
    public void ProratedMinutes_予定期間がすべて休日なら暦日で按分する()
    {
        var saturday = Mon.AddDays(5);

        Assert.Equal(60, EffortRules.ProratedMinutes(WorkingCalendar.Empty, saturday, saturday.AddDays(1), 120, saturday.AddDays(1), saturday.AddDays(9)), precision: 6);
    }

    [Fact]
    public void Variance_実績から予定を引く_予定がなければ空()
    {
        Assert.Equal(30, EffortRules.Variance(60, 90));
        Assert.Equal(-60, EffortRules.Variance(120, 60));
        Assert.Null(EffortRules.Variance(null, 60));
    }
}

/// <summary>文字列の正規化と検査（詳細設計書 3.1、4.6）。</summary>
public class TextRulesTests
{
    [Fact]
    public void NormalizeSingleLine_NFCにして前後の空白を除き_改行とタブは空白にする()
    {
        // 「カ」と結合用の濁点 → 「ガ」
        Assert.Equal("ガ 設計 書", TextRules.NormalizeSingleLine("  ガ\t設計\n書 　"));
        Assert.Null(TextRules.NormalizeSingleLine(" \t\n "));
        Assert.Null(TextRules.NormalizeSingleLine(null));
    }

    [Fact]
    public void NormalizeMultiline_改行をLFにそろえる()
    {
        Assert.Equal("1 行目\n2 行目\n3 行目", TextRules.NormalizeMultiline("1 行目\r\n2 行目\r3 行目\r\n"));
        Assert.Null(TextRules.NormalizeMultiline("\r\n"));
    }

    [Theory]
    [InlineData("ふつうの文字", false)]
    [InlineData("改行\nとタブ\t", false)]
    [InlineData("ベル\u0007", true)]
    [InlineData("NUL\u0000", true)]
    [InlineData("C1\u0085", true)]
    public void HasForbiddenControlChars_改行とタブ以外の制御文字を見つける(string value, bool expected)
    {
        Assert.Equal(expected, TextRules.HasForbiddenControlChars(value));
    }

    [Fact]
    public void Length_コードポイントの数で数える()
    {
        Assert.Equal(3, TextRules.Length("𠮷野家"));
        Assert.Equal(2, TextRules.Length("ab"));
    }
}

/// <summary>区分値と API・DB のコード（基本設計書 5.3）。</summary>
public class EnumCodesTests
{
    [Fact]
    public void コードはsnake_caseで_相互に変換できる()
    {
        Assert.Equal("not_started", TaskItemStatus.NotStarted.ToCode());
        Assert.Equal("effort_overrun", DelayFlag.EffortOverrun.ToCode());
        Assert.Equal(TaskItemStatus.InProgress, EnumCodes.Parse<TaskItemStatus>("in_progress"));
        Assert.False(EnumCodes.TryParse<TaskItemStatus>("InProgress", out _));
        Assert.False(EnumCodes.TryParse<TaskItemStatus>(null, out _));
    }

    [Fact]
    public void すべての値にコードがある()
    {
        Assert.Equal(Enum.GetValues<TaskItemStatus>().Length, EnumCodes.AllCodes<TaskItemStatus>().Count);
        Assert.Equal(Enum.GetValues<TagColor>().Length, EnumCodes.AllCodes<TagColor>().Count);
    }
}
