namespace CaisseApIpTester.Protocol;

/// <summary>ECR_PT_* — transaction type (§3.1 TYPE).</summary>
public enum EuroCaisseTransactionType
{
    Sale = '0',
    Refund = '1',
    Cancellation = '2',
    Duplicate = '3',
    Iso2Read = 'A'
}

/// <summary>ECR_AT_* — response delay TLV A (§3.1 DELAI).</summary>
public enum EuroCaisseAnswerDelay
{
    EndOfTransaction = '0',
    Immediate = '1'
}

/// <summary>ECR_AU_* — authorization TLV B (§3.1 AUTO).</summary>
public enum EuroCaisseAuthorizationType
{
    EftDecides = '0',
    Forced = '1',
    Guarantee = '2'
}

public sealed class TransactionRequest
{
    public decimal Amount { get; set; }
    public EuroCaisseTransactionType Type { get; set; } = EuroCaisseTransactionType.Sale;
    public string CurrencyCode { get; set; } = "434";  // LYD (ISO 4217 numeric)
    public int CurrencyDecimals { get; set; } = 3;      // LYD minor unit = 3 decimals
    public string CashRegisterNumber { get; set; } = "01";

    /// <summary>IND — '0' no REP, '1' fixed REP (55), '2' TLV at end of response.</summary>
    public char ResponseIndicator { get; set; } = '2';

    /// <summary>EMETTEUR — '0' = indifferent (recommended).</summary>
    public char Issuer { get; set; } = '0';

    public string PrivateData { get; set; } = "";

    public bool IncludeAnswerDelayTlv { get; set; } = true;
    public EuroCaisseAnswerDelay AnswerDelay { get; set; } = EuroCaisseAnswerDelay.EndOfTransaction;

    public bool IncludeAuthorizationTlv { get; set; }
    public EuroCaisseAuthorizationType AuthorizationType { get; set; } = EuroCaisseAuthorizationType.EftDecides;

    public char TransactionTypeCode => (char)Type;

    public string BuildPayload() => EuroCaisseMessage.EncodeRequest(this);

    public override string ToString() =>
        $"{Type} {Amount:F3} {CurrencyCode} reg={CashRegisterNumber} IND={ResponseIndicator}";
}
