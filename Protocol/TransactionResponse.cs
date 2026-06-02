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
            Console.WriteLine($"REP           : '{ResponseData.TrimEnd()}'");

        Console.WriteLine($"Currency (DEV) : {CurrencyCode}");
        Console.WriteLine($"Private  (PRIV): '{PrivateData.TrimEnd()}'");

        if (Tlv != null)
        {
            string encoded = Tlv.Encode();
            if (encoded.Length > 0)
                Console.WriteLine($"TLV           : {encoded}");
            if (Tlv.Get('A') is { } pan)
                Console.WriteLine($"  PAN (A)     : {pan.TrimEnd()}");
            if (Tlv.Get('B') is { } expiry)
                Console.WriteLine($"  Expiry (B)  : {expiry}");
            if (Tlv.Get('C') is { } auth)
                Console.WriteLine($"  Auth (C)    : {auth}");
        }
    }

    private static string FormatAmount(string units)
    {
        if (!long.TryParse(units, NumberStyles.None, CultureInfo.InvariantCulture, out long v))
            return units;
        return (v / 100m).ToString("F2", CultureInfo.InvariantCulture) + " (units=" + units + ")";
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
