using System.Security.Cryptography;

namespace CP6.Platform.Messaging.NativeContracts;

/// <summary>
/// An owned snapshot of strict JSON bytes. Capture proves JSON syntax only, not an Owner command,
/// authorization, persistence or successful replay. Never reconstruct replay bytes from a JSON object.
/// </summary>
public sealed class Cp6OriginalJson
{
    private readonly byte[] bytes;

    private Cp6OriginalJson(byte[] bytes)
    {
        this.bytes = bytes;
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public string Sha256 { get; }

    public static Cp6OriginalJson Capture(ReadOnlyMemory<byte> utf8)
    {
        var owned = utf8.ToArray();
        using var document = Cp6NativeJson.Parse(owned);
        return new Cp6OriginalJson(owned);
    }

    public byte[] GetBytes() => bytes.ToArray();

    public bool Matches(ReadOnlySpan<byte> utf8) => utf8.SequenceEqual(bytes);
}
