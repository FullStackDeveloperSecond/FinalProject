using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DoSelect.Application.Common.Cursors;

/// <summary>
/// A small, feature-agnostic codec for the opaque cursors used by keyset-paginated admin
/// queries (support SLA queue, unified case workbench). The cursor is positioning data only —
/// it is never treated as an authorization boundary, so every query re-applies its own
/// authorization predicates regardless of what a decoded cursor contains.
///
/// The payload is versioned and carries a caller-supplied "fingerprint": a canonical hash of
/// the current request's filters (and, for workbench, the caller's authorized case-type scope).
/// If the fingerprint on a supplied cursor does not match the fingerprint computed for the
/// current request, the cursor is rejected as a mismatch rather than silently reinterpreted —
/// pagination must restart from the first page whenever filters or the authorized scope change.
/// </summary>
public static class OpaqueCursorCodec
{
    private const int CurrentVersion = 1;

    /// <summary>Matches the 512-character bound documented for cursor fields in the API DTO contract.</summary>
    public const int MaxEncodedLength = 512;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
    };

    public static string Encode<TPayload>(TPayload payload, string fingerprint)
    {
        var envelope = new CursorEnvelope<TPayload>(CurrentVersion, fingerprint, payload);
        var json = JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
        return Base64Url.EncodeToString(json);
    }

    /// <summary>
    /// Attempts to decode <paramref name="cursor"/> as a cursor produced by <see cref="Encode{TPayload}"/>
    /// for the same <typeparamref name="TPayload"/> and whose fingerprint matches
    /// <paramref name="expectedFingerprint"/>. Returns false for a null/empty cursor (the caller
    /// should treat that as "start from the first page"), and false for anything malformed,
    /// wrong-versioned, or fingerprint-mismatched.
    /// </summary>
    public static bool TryDecode<TPayload>(string? cursor, string expectedFingerprint, out TPayload? payload)
    {
        payload = default;

        if (string.IsNullOrEmpty(cursor) || cursor.Length > MaxEncodedLength)
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(cursor);
        }
        catch (FormatException)
        {
            return false;
        }

        CursorEnvelope<TPayload>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<CursorEnvelope<TPayload>>(bytes, SerializerOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (envelope is null || envelope.Version != CurrentVersion || envelope.Payload is null)
        {
            return false;
        }

        if (!string.Equals(envelope.Fingerprint, expectedFingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        payload = envelope.Payload;
        return true;
    }

    /// <summary>
    /// Builds a canonical, order-sensitive fingerprint from the supplied parts (a feature tag,
    /// then each filter/scope value in a fixed order). Parts are joined with the ASCII Unit
    /// Separator (0x1F) so e.g. ["a","bc"] cannot collide with ["ab","c"]. Callers must always
    /// pass parts in the same order for the same query shape so equal filters produce equal
    /// fingerprints.
    /// </summary>
    public static string ComputeFingerprint(params string?[] parts)
    {
        var canonical = string.Join("", parts.Select(static p => p ?? string.Empty));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }

    private sealed record CursorEnvelope<TPayload>(int Version, string Fingerprint, TPayload Payload);
}
