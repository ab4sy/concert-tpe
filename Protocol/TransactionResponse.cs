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

    // Retrieval reference number. The terminal returns it as TLV tag F followed by
    // the RRN digits (e.g. F12123456789123 -> "123456789123"); the F marks
    // transactions for which an RRN was returned. Null when absent or blank.
    public string? Rrn
    {
        get
        {
            string? v = Tlv?.Get('F')?.Trim();
            return string.IsNullOrEmpty(v) ? null : v;
        }
    }

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
        Console.WriteLine($"RRN      (F)   : {Rrn ?? "(none)"}");

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

    // CONCERT EURO/CAISSE ref. 095 §3.2 defines the card/cheque tags (A, C–G).
    // On this terminal tag F carries the RRN (F + RRN, e.g. F123456789123), not
    // the spec's cheque guarantor response.
    // This terminal also appends an EMV/vendor set (M, T, I, S, V, B, R, N, Z),
    // confirmed field-by-field against a real online sale response. Where the two
    // collide the observed EMV meaning wins — e.g. B is TSI here, not card expiry.
    // Unknown types are still printed above with their raw letter + value.
    private static string DescribeTlv(char type) => type switch
    {
        'A' => "PAN (card number)",
        'C' => "Authorization number",
        'D' => "CMC7 track (cheque)",
        'E' => "FNCI response (cheque)",
        'F' => "RRN (retrieval ref.)",
        'G' => "ISO2 track",
        'M' => "Amount (minor units)",
        'T' => "Date/time YYMMDDHHMMSS",
        'I' => "AID (application identifier)",
        'S' => "TC (transaction cryptogram)",
        'V' => "TVR (terminal verification results)",
        'B' => "TSI (transaction status info)",
        'R' => "Card type",
        'N' => "Invoice number",
        'Z' => "Transaction name",
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
