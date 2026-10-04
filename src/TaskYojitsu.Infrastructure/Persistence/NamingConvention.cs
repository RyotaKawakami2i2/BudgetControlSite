using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace TaskYojitsu.Infrastructure.Persistence;

/// <summary>
/// テーブル名・列名を snake_case にし、主キー・外部キー・索引に決まった形の名前を付ける（要件定義書 7.6）。
/// </summary>
internal static class NamingConvention
{
    public static string Snake(string name) => JsonNamingPolicy.SnakeCaseLower.ConvertName(name);

    public static void ApplySnakeCase(ModelBuilder builder)
    {
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            if (entity.IsMappedToJson())
            {
                // JSON の列に入れる型（パスキーの情報）は、入れ物の列の名前だけを変える
                if (entity.GetContainerColumnName() is { } container)
                {
                    entity.SetContainerColumnName(Snake(container));
                }

                continue;
            }

            var table = entity.GetTableName();
            if (table is null)
            {
                continue;
            }

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(Snake(property.Name));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(key.IsPrimaryKey()
                    ? $"pk_{table}"
                    : $"ak_{table}_{string.Join("_", key.Properties.Select(p => Snake(p.Name)))}");
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                var principal = foreignKey.PrincipalEntityType.GetTableName();
                foreignKey.SetConstraintName(
                    $"fk_{table}_{principal}_{string.Join("_", foreignKey.Properties.Select(p => Snake(p.Name)))}");
            }

            foreach (var index in entity.GetIndexes())
            {
                // 明示的に ix_／ux_ で始まる名前を付けた索引はそのままにする
                if (index.GetDatabaseName() is { } existing && (existing.StartsWith("ix_", StringComparison.Ordinal) || existing.StartsWith("ux_", StringComparison.Ordinal)))
                {
                    continue;
                }

                index.SetDatabaseName(
                    $"{(index.IsUnique ? "ux" : "ix")}_{table}_{string.Join("_", index.Properties.Select(p => Snake(p.Name)))}");
            }
        }
    }
}
