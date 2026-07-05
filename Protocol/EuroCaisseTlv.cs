namespace CaisseApIpTester.Protocol;

/// <summary>
/// Optional A4 TLV fields: Type (1 char) + Length (2 digits) + Value.
/// e.g. DELAI = A01X, AUTO = B01X.
/// </summary>
public sealed class EuroCaisseTlv
{
    private readonly List<(char Type, string Value)> _fields = [];

    public void Set(char type, string value)
    {
        _fields.RemoveAll(f => f.Type == type);
        _fields.Add((type, value));
    }

    public string? Get(char type)
    {
        var hit = _fields.Find(f => f.Type == type);
        return hit == default ? null : hit.Value;
    }

    /// <summary>All decoded TLV fields, in the order received.</summary>
    public IReadOnlyList<(char Type, string Value)> Fields => _fields;

    public string Encode()
    {
        return string.Concat(_fields
            .OrderBy(f => f.Type)
            .Select(f => $"{f.Type}{f.Value.Length:D2}{f.Value}"));
    }

    public static EuroCaisseTlv Decode(string tlvSection)
    {
        var tlv = new EuroCaisseTlv();
        int pos = 0;
        while (pos < tlvSection.Length)
        {
            if (pos + 3 > tlvSection.Length)
                throw new FormatException($"Truncated TLV at position {pos}.");

            char type = tlvSection[pos];
            if (!int.TryParse(tlvSection.AsSpan(pos + 1, 2), out int len) || len < 0)
                throw new FormatException($"Bad TLV length for type '{type}'.");

            int valueStart = pos + 3;
            if (valueStart + len > tlvSection.Length)
                throw new FormatException($"TLV type '{type}' value truncated.");

            tlv.Set(type, tlvSection.Substring(valueStart, len));
            pos = valueStart + len;
        }

        return tlv;
    }
}
