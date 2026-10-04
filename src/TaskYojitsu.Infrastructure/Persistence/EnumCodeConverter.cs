using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TaskYojitsu.Domain.Codes;

namespace TaskYojitsu.Infrastructure.Persistence;

/// <summary>区分値を英字のコードで保存する（例: NotStarted → not_started）。</summary>
public sealed class EnumCodeConverter<T>() : ValueConverter<T, string>(
    value => EnumCodes.ToCode(value),
    code => EnumCodes.Parse<T>(code))
    where T : struct, Enum;
