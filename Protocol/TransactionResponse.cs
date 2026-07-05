using System.Globalization;

namespace CaisseApIpTester.Protocol;

public sealed class TransactionResponse
{
    public string RawPayload { get; init; } = "";
    public string CashRegisterNumber { get; init; } = "";
    public char Status { get; init; }
    public string AmountUnits { get; init; } = "";
    public char PaymentMode { get; init; }
    public string? ResponseData { get; init; }
    public string CurrencyCode { get; init; } = "";
    public string PrivateData { get; init; } = "";
    public EuroCaisseTlv? Tlv { get; init; }

    public bool IsAccepted => Status == '0';
    public bool IsNotCompleted => Status == '7';
    public bool IsRequestAccepted => Status == '9';

    public static TransactionResponse Parse(string payload) =>
        EuroCaisseMessage.DecodeResponse(payload);

    public void Print()
    {
        Console.WriteLine();
        Console.WriteLine("--- Response (CONCERT A4) ---");
        Console.WriteLine($"Register (ECR#): {CashRegisterNumber}");
        Console.WriteLine($"Status   (STAT): {Status} — {DescribeStatus(Status)}");
        Console.WriteLine($"Amount   (MNT) : {FormatAmount(AmountUnits)}");
        Console.WriteLine($"Mode     (MODE): {PaymentMode} — {DescribeIssuer(PaymentMode)}");

        if (!string.IsNullOrEmpty(ResponseData))
            Console.WriteLine($"REP (55)      : '{ResponseData.TrimEnd()}'");

        Console.WriteLine($"Currency (DEV) : {CurrencyCode}");
        Console.WriteLine($"Private  (PRIV): '{PrivateData.TrimEnd()}'");

        if (Tlv != null && Tlv.Fields.Count > 0)
        {
            Console.WriteLine($"TLV raw       : {Tlv.Encode()}");
            Console.WriteLine("TLV fields    :");
            foreach (var (type, value) in Tlv.Fields)
                Console.WriteLine($"  [{type}] {DescribeTlv(type),-22}: '{value.TrimEnd()}'");
        }

        Console.WriteLine($"Raw payload   : {RawPayload}");
    }

    // Best-effort labels per the repo's reading of CONCERT EURO/CAISSE ref. 095.
    // Unknown types are still printed above with their raw letter + value.
    // Standard tags per CONCERT ref. 095 §3.2 (response). A–G are spec-defined;
    // anything else is a terminal/vendor extension (the spec list ends with "…").
    private static string DescribeTlv(char type) => type switch
    {
        'A' => "PAN (card number)",
        'B' => "Card expiry (YYMM)",
        'C' => "Authorization number",
        'D' => "CMC7 track (cheque)",
        'E' => "FNCI response (cheque)",
        'F' => "Guarantor response (cheque)",
        'G' => "ISO2 track",
        // Vendor tag confirmed empirically on this terminal (value matched the
        // wall-clock time of an online attempt): YYMMDDHHMMSS.
        'T' => "Date/time YYMMDDHHMMSS (vendor)",
        _ => "(non-standard / vendor-specific)"
    };

    private static string FormatAmount(string units)
    {
        if (!long.TryParse(units, NumberStyles.None, CultureInfo.InvariantCulture, out long v))
            return units;
        return (v / 1000m).ToString("F3", CultureInfo.InvariantCulture) + " LYD (units=" + units + ")";
    }

    private static string DescribeStatus(char stat) => stat switch
    {
        '0' => "Operation completed",
        '7' => "Operation not completed",
        '9' => "Request accepted (transaction in progress)",
        '8' => "Operation impossible",
        _ => "Unknown status"
    };

    private static string DescribeIssuer(char code) => code switch
    {
        '0' => "Indifferent",
        '1' => "Carte Bancaire",
        '2' => "American Express",
        'C' => "Chèque",
        _ => "Other / issuer-specific"
    };
}
