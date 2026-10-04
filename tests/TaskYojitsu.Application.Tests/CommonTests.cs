using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Tasks;
using TaskYojitsu.Domain.Codes;

namespace TaskYojitsu.Application.Tests;

/// <summary>PATCH の「送られなかった項目」と「null」の区別（詳細設計書 5.4.3）。</summary>
public class OptionalTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void 送られなかった項目はHasValueがfalse()
    {
        var request = JsonSerializer.Deserialize<UpdateTaskRequest>("""{"version":3,"title":"新しい名前"}""", Json)!;

        Assert.Equal(3, request.Version);
        Assert.True(request.Title.HasValue);
        Assert.Equal("新しい名前", request.Title.Value);
        Assert.False(request.AssigneeId.HasValue);
        Assert.False(request.PlannedStart.HasValue);
    }

    [Fact]
    public void nullを送った項目は空にする指定として受け取る()
    {
        var request = JsonSerializer.Deserialize<UpdateTaskRequest>("""{"version":1,"assigneeId":null,"plannedStart":"2026-10-05","plannedEnd":null}""", Json)!;

        Assert.True(request.AssigneeId.HasValue);
        Assert.Null(request.AssigneeId.Value);
        Assert.Equal(new DateOnly(2026, 10, 5), request.PlannedStart.Value);
        Assert.True(request.PlannedEnd.HasValue);
        Assert.Null(request.PlannedEnd.Value);
    }
}

/// <summary>業務上の「今日」（詳細設計書 4.3。日本時間の日付）。</summary>
public class BusinessClockTests
{
    [Fact]
    public void Today_日本時間の日付を返す()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 14, 59, 0, TimeSpan.Zero));
        var clock = new BusinessClock(time, Options.Create(new AppOptions()));

        Assert.Equal(new DateOnly(2026, 10, 4), clock.Today);

        // UTC の 15 時は、日本時間の翌日 0 時
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(new DateOnly(2026, 10, 5), clock.Today);
    }

    [Theory]
    [InlineData("2026-10-05", "2026-10-05")]
    [InlineData("2026-10-07", "2026-10-05")]
    [InlineData("2026-10-11", "2026-10-05")]
    [InlineData("2026-10-12", "2026-10-12")]
    public void MondayOf_週の始まりは月曜日(string date, string monday)
    {
        Assert.Equal(DateOnly.Parse(monday, System.Globalization.CultureInfo.InvariantCulture),
            BusinessClock.MondayOf(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture)));
    }
}

/// <summary>入力の検査（詳細設計書 4.6）。</summary>
public class ValidationTests
{
    [Fact]
    public void SingleLine_正規化した値を返し_長さはコードポイントで数える()
    {
        var v = new Validation();

        var value = v.SingleLine("title", "  " + new string('あ', 199) + "𠮷 ", 200, required: true);

        Assert.Equal(200, Domain.Rules.TextRules.Length(value!));
        Assert.False(v.HasErrors);
    }

    [Fact]
    public void SingleLine_必須で空なら指定のメッセージ()
    {
        var v = new Validation();

        v.SingleLine("title", " \t ", 200, required: true, requiredMessage: Msg.TskTitle);

        var error = Assert.Throws<ValidationException>(v.ThrowIfAny);
        Assert.Equal([Msg.TskTitle], error.Errors["title"]);
    }

    [Fact]
    public void 制御文字を含む値は受け付けない()
    {
        var v = new Validation();

        Assert.Null(v.Multiline("description", "説明\u0000", 4000));
        Assert.True(v.HasError("description"));
    }

    [Fact]
    public void TryCode_定義した区分値だけを受け付ける()
    {
        var v = new Validation();

        Assert.True(v.TryCode<Priority>("priority", "high", out var priority));
        Assert.Equal(Priority.High, priority);
        Assert.False(v.TryCode<Priority>("priority", "urgent", out _));
        Assert.True(v.HasError("priority"));
    }
}

/// <summary>招待・再設定のトークン（詳細設計書 6.6）。</summary>
public class SecureTokensTests
{
    [Fact]
    public void Create_推測できない43文字のトークン()
    {
        var a = SecureTokens.Create();
        var b = SecureTokens.Create();

        Assert.NotEqual(a, b);
        Assert.True(SecureTokens.LooksValid(a));
        Assert.Equal(32, SecureTokens.Hash(a).Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("0123456789012345678901234567890123456789+/=")]
    public void LooksValid_形式が違うものを弾く(string? token)
    {
        Assert.False(SecureTokens.LooksValid(token));
    }
}
