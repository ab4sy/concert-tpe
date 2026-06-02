using System.Globalization;
using System.Text;

namespace CaisseApIpTester.Protocol;

/// <summary>
/// CONCERT EURO/CAISSE A4 application messages (ref. 095 v2.0 §3).
/// Payload is the ASCII between STX and ETX (STX/ETX/LRC added by transport).
/// </summary>
public static class EuroCaisseMessage
{
    public const int RegisterIdLen = 2;
    public const int AmountLen = 8;
    public const int CurrencyLen = 3;
    public const int PrivateLen = 10;
    public const int RepFieldLen = 55;

    // Fixed request: ECR# MNT IND EMETTEUR TYPE DEV PRIV
    public const int RequestFixedLen =
        RegisterIdLen + AmountLen + 1 + 1 + 1 + CurrencyLen + PrivateLen;

    // Fixed response without REP: ECR# STAT MNT MODE DEV PRIV
    public const int ResponseFixedWithoutRepLen =
        RegisterIdLen + 1 + AmountLen + 1 + CurrencyLen + PrivateLen;

    public static string EncodeRequest(TransactionRequest req)
    {
        long amountUnits = (long)Math.Round(
            req.Amount * (decimal)Math.Pow(10, req.CurrencyDecimals),
            MidpointRounding.AwayFromZero);

        var sb = new StringBuilder(RequestFixedLen + 8);
        sb.Append(NormalizeRegister(req.CashRegisterNumber));
        sb.Append(amountUnits.ToString("D8", CultureInfo.InvariantCulture));
        sb.Append(req.ResponseIndicator);
        sb.Append(req.Issuer);
        sb.Append(req.TransactionTypeCode);
        sb.Append(NormalizeCurrency(req.CurrencyCode));
        sb.Append(NormalizePrivate(req.PrivateData));

        var tlv = new EuroCaisseTlv();
        if (req.IncludeAnswerDelayTlv)
            tlv.Set('A', ((char)req.AnswerDelay).ToString());
        if (req.IncludeAuthorizationTlv)
            tlv.Set('B', ((char)req.AuthorizationType).ToString());

        sb.Append(tlv.Encode());
        return sb.ToString();
    }

    public static TransactionResponse DecodeResponse(string payload)
    {
        if (payload.Length < ResponseFixedWithoutRepLen)
            throw new FormatException(
                $"Response too short ({payload.Length} chars, need at least {ResponseFixedWithoutRepLen}).");

        int pos = 0;
        string register = payload.Substring(pos, RegisterIdLen);
        pos += RegisterIdLen;

        char status = payload[pos++];
        string amount = payload.Substring(pos, AmountLen);
        pos += AmountLen;

        char mode = payload[pos++];

        string? rep = null;
        int tailAfterMode = payload.Length - pos;
        int fixedTail = CurrencyLen + PrivateLen;

        // IND='1' layout: … MODE REP(55) DEV PRIV [TLV]
        if (tailAfterMode >= RepFieldLen + fixedTail)
        {
            rep = payload.Substring(pos, RepFieldLen);
            pos += RepFieldLen;
        }

        if (pos + fixedTail > payload.Length)
            throw new FormatException($"Response truncated after MODE (at offset {pos}).");

        string currency = payload.Substring(pos, CurrencyLen);
        pos += CurrencyLen;
        string priv = payload.Substring(pos, PrivateLen);
        pos += PrivateLen;

        EuroCaisseTlv? tlv = null;
        if (pos < payload.Length)
            tlv = EuroCaisseTlv.Decode(payload[pos..]);

        return new TransactionResponse
        {
            RawPayload = payload,
            CashRegisterNumber = register,
            Status = status,
            AmountUnits = amount,
            PaymentMode = mode,
            ResponseData = rep,
            CurrencyCode = currency,
            PrivateData = priv,
            Tlv = tlv
        };
    }

    private static string NormalizeRegister(string id)
    {
        string digits = new(id.Where(char.IsDigit).Take(RegisterIdLen).ToArray());
        if (digits.Length == 0) digits = "1";
        return digits.PadLeft(RegisterIdLen, '0')[^RegisterIdLen..];
    }

    private static string NormalizeCurrency(string code)
    {
        string digits = new(code.Where(char.IsDigit).Take(CurrencyLen).ToArray());
        return digits.PadLeft(CurrencyLen, '0')[^CurrencyLen..];
    }

    private static string NormalizePrivate(string data)
    {
        if (string.IsNullOrEmpty(data))
            return new string(' ', PrivateLen);
        return data.Length >= PrivateLen
            ? data[..PrivateLen]
            : data.PadRight(PrivateLen, ' ');
    }
}
