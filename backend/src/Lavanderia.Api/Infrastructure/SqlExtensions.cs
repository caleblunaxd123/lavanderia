using Microsoft.Data.SqlClient;
using System.Data;

namespace Lavanderia.Api.Infrastructure;

/// <summary>
/// Envoltorios finos sobre ADO.NET puro para reducir boilerplate.
/// No es un ORM: sigue siendo el desarrollador quien escribe el SQL.
/// </summary>
public static class SqlExtensions
{
    public static SqlCommand AddParam(this SqlCommand cmd, string name, object? value)
    {
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    public static async Task<T?> ReadScalarAsync<T>(this SqlCommand cmd, CancellationToken ct = default)
    {
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null or DBNull) return default;
        return (T)Convert.ChangeType(result, typeof(T));
    }

    public static async Task<List<T>> ReadListAsync<T>(
        this SqlCommand cmd,
        Func<SqlDataReader, T> map,
        CancellationToken ct = default)
    {
        var list = new List<T>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(map(reader));
        }
        return list;
    }

    public static async Task<T?> ReadFirstOrDefaultAsync<T>(
        this SqlCommand cmd,
        Func<SqlDataReader, T> map,
        CancellationToken ct = default) where T : class
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            return map(reader);
        return null;
    }

    public static string? GetNullableString(this SqlDataReader r, string col)
        => r.IsDBNull(r.GetOrdinal(col)) ? null : r.GetString(r.GetOrdinal(col));

    public static int? GetNullableInt(this SqlDataReader r, string col)
        => r.IsDBNull(r.GetOrdinal(col)) ? null : r.GetInt32(r.GetOrdinal(col));

    public static DateTime? GetNullableDateTime(this SqlDataReader r, string col)
        => r.IsDBNull(r.GetOrdinal(col)) ? null : r.GetDateTime(r.GetOrdinal(col));

    public static decimal? GetNullableDecimal(this SqlDataReader r, string col)
        => r.IsDBNull(r.GetOrdinal(col)) ? null : r.GetDecimal(r.GetOrdinal(col));
}
