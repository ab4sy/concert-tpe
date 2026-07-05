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

    // Fixed response header: ECR# STAT MNT MODE
    public const int ResponseHeaderLen = RegisterIdLen + 1 + AmountLen + 1;

    public static TransactionResponse DecodeResponse(string payload)
    {
        if (payload.Length < ResponseHeaderLen)
            throw new FormatException(
                $"Response too short ({payload.Length} chars, need at least {ResponseHeaderLen}).");

        string register = payload.Substring(0, RegisterIdLen);
        char status = payload[RegisterIdLen];
        string amount = payload.Substring(RegisterIdLen + 1, AmountLen);
        char mode = payload[RegisterIdLen + 1 + AmountLen];

        // The terminal returns a tag-length-value stream after MODE, and on the
        // observed CONCERT response the fixed DEV(3 digits)+PRIV(10) block sits
        // at the very end. Strip that tail first, then scan the middle for TLVs.
        int midStart = ResponseHeaderLen;
        int midEnd = payload.Length;
        string currency = "";
        string priv = "";

        int tailLen = CurrencyLen + PrivateLen;
        if (payload.Length - ResponseHeaderLen >= tailLen)
        {
            string devCandidate = payload.Substring(payload.Length - tailLen, CurrencyLen);
            if (devCandidate.All(char.IsDigit))
            {
                currency = devCandidate;
                priv = payload[^PrivateLen..];
                midEnd = payload.Length - tailLen;
            }
        }

        EuroCaisseTlv tlv = ScanTlv(payload, midStart, midEnd);

        return new TransactionResponse
        {
            RawPayload = payload,
            CashRegisterNumber = register,
            Status = status,
            AmountUnits = amount,
            PaymentMode = mode,
            ResponseData = null,
            CurrencyCode = currency,
            PrivateData = priv,
            Tlv = tlv
        };
    }

    // Tolerant scan: a TLV is a letter tag + 2-digit length + value. Any byte
    // that doesn't start a valid TLV (e.g. a stray separator) is skipped so one
    // odd field can't abort the whole parse.
    private static EuroCaisseTlv ScanTlv(string s, int start, int end)
    {
        var tlv = new EuroCaisseTlv();
        int i = start;
        while (i < end)
        {
            char tag = s[i];
            if (char.IsLetter(tag) && i + 3 <= end
                && char.IsDigit(s[i + 1]) && char.IsDigit(s[i + 2]))
            {
                int len = (s[i + 1] - '0') * 10 + (s[i + 2] - '0');
                if (i + 3 + len <= end)
                {
                    tlv.Set(tag, s.Substring(i + 3, len));
                    i += 3 + len;
                    continue;
                }
            }
            i++;
        }
        return tlv;
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
