using System.IO.Ports;
using CaisseApIpTester.Protocol;

// ── CONCERT EURO/CAISSE tester ───────────────────────────────────────────────
// Goal: as few keystrokes as possible. Connects to the terminal over USB serial
// (auto-detected COM port, 9600 7E1), then just asks for an amount and runs a
// sale. Insert the card on the TPE to complete.
//
//   dotnet run                       interactive sale loop (auto-detect port)
//   dotnet run -- 10.50              one-shot sale of 10.50 then exit
//   dotnet run -- --port COM5 --baud 1200
//   dotnet run -- --reg 02          set cash register number (default 01)
//   dotnet run -- --refund          make the (one-shot or first) transaction a refund
//   dotnet run -- --online          force an online authorization (AUTO=B011)
//   dotnet run -- --list-ports

var opts = args.ToList();

if (opts.Contains("--list-ports")) { ListSerialPorts(); return; }
if (opts.Contains("-h") || opts.Contains("--help")) { PrintHelp(); return; }

// Offline: decode a raw response payload captured from a real run.
string? decodePayload = TakeOption(opts, "--decode");
if (decodePayload != null)
{
    Console.WriteLine($"Raw payload ({decodePayload.Length} chars): {Visible(decodePayload)}");
    TransactionResponse.Parse(decodePayload).Print();
    return;
}

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║  CONCERT EURO/CAISSE Tester (Ref. 095)   ║");
Console.WriteLine("╚══════════════════════════════════════════╝");

string cashReg = TakeOption(opts, "--reg") ?? "01";
bool refund = opts.Remove("--refund");
bool online = opts.Remove("--online");
ITransport transport = BuildTransport(opts);

// Any leftover non-flag token is a one-shot amount.
string? oneShotAmount = opts.FirstOrDefault(a => !a.StartsWith('-'));

using (transport)
{
    if (oneShotAmount != null)
    {
        await TrySale(transport, oneShotAmount, refund, online, cashReg);
        return;
    }

    Console.WriteLine();
    Console.WriteLine("Enter an amount in LYD and press Enter to run a sale (blank to quit).");
    Console.WriteLine("Prefix with 'r' for a refund, e.g.  r 10.50");
    Console.WriteLine();

    bool first = refund;
    while (true)
    {
        Console.Write("Amount LYD > ");
        string? line = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(line) || line is "q" or "quit" or "exit")
            return;

        bool isRefund = first;
        first = false;
        if (line.StartsWith("r ", StringComparison.OrdinalIgnoreCase))
        {
            isRefund = true;
            line = line[2..].Trim();
        }

        await TrySale(transport, line, isRefund, online, cashReg);
        Console.WriteLine();
    }
}

static async Task TrySale(ITransport transport, string amountText, bool refund, bool online, string cashReg)
{
    if (!decimal.TryParse(amountText,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out decimal amount) || amount <= 0)
    {
        Console.WriteLine($"[ERR] Invalid amount: '{amountText}'");
        return;
    }

    var request = new TransactionRequest
    {
        Amount = amount,
        Type = refund ? EuroCaisseTransactionType.Refund : EuroCaisseTransactionType.Sale,
        CashRegisterNumber = cashReg,
        ResponseIndicator = '2',
        AnswerDelay = EuroCaisseAnswerDelay.EndOfTransaction,
        IncludeAnswerDelayTlv = true,
        // --online forces the TPE to go online to the acquirer (AUTO TLV = B011),
        // which is what populates the authorization number (tag C) in the response.
        IncludeAuthorizationTlv = online,
        AuthorizationType = EuroCaisseAuthorizationType.Forced
    };

    string payload = request.BuildPayload();
    string label = (refund ? "REFUND" : "SALE") + (online ? " (online)" : "");
    Console.WriteLine($"→ {label} {amount:F3} LYD  (A4: {payload})");

    try
    {
        string raw = await transport.SendAsync(payload);

        Console.WriteLine();
        Console.WriteLine($"Raw response ({raw.Length} chars):");
        Console.WriteLine($"  text: {Visible(raw)}");
        Console.WriteLine($"  hex : {Hex(raw)}");

        try
        {
            TransactionResponse response = TransactionResponse.Parse(raw);
            response.Print();
            Console.WriteLine(response.IsAccepted || response.IsRequestAccepted
                ? "\n✓ Positive response — insert / present the card on the terminal to complete."
                : "\n✗ Terminal did not accept the request.");
        }
        catch (FormatException fe)
        {
            Console.WriteLine($"\n[parse] Could not decode response: {fe.Message}");
            Console.WriteLine("[parse] Raw payload is shown above — share it so the layout can be mapped.");
        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("[ERR] Request timed out.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERR] {ex.GetType().Name}: {ex.Message}");
    }
}

// Render control chars as <XX> so the layout is readable in a terminal.
static string Visible(string s)
{
    var sb = new System.Text.StringBuilder(s.Length);
    foreach (char c in s)
        sb.Append(c < 0x20 || c == 0x7f ? $"<{(int)c:X2}>" : c);
    return sb.ToString();
}

static string Hex(string s) =>
    string.Join(' ', s.Select(c => ((int)c).ToString("X2")));

static ITransport BuildTransport(List<string> opts)
{
    string? portName = TakeOption(opts, "--port");
    int baud = int.TryParse(TakeOption(opts, "--baud"), out int b) ? b : 9600;

    if (portName == null)
    {
        string[] available = SerialPort.GetPortNames();
        if (available.Length == 0)
        {
            portName = DetectDefaultPort();
            Console.WriteLine($"[WARN] No serial ports detected; trying {portName}.");
        }
        else
        {
            portName = available[0];
            if (available.Length > 1)
                Console.WriteLine($"Detected ports: {string.Join(", ", available)} (using {portName}; override with --port)");
        }
    }

    Console.WriteLine($"Transport: serial {portName} @ {baud} 7E1");
    return new UsbSerialTransport(portName, baud);
}

// Returns the value following a flag and removes both from the list, or null.
static string? TakeOption(List<string> opts, string flag)
{
    int i = opts.IndexOf(flag);
    if (i < 0 || i + 1 >= opts.Count) return null;
    string value = opts[i + 1];
    opts.RemoveAt(i + 1);
    opts.RemoveAt(i);
    return value;
}

static string DetectDefaultPort() =>
    OperatingSystem.IsWindows() ? "COM3" :
    OperatingSystem.IsMacOS() ? "/dev/cu.usbmodem1" :
    "/dev/ttyACM0";

static void PrintHelp()
{
    Console.WriteLine("CONCERT EURO/CAISSE tester");
    Console.WriteLine();
    Console.WriteLine("  dotnet run                      sale loop over auto-detected serial port");
    Console.WriteLine("  dotnet run -- 10.50             one-shot sale then exit");
    Console.WriteLine("  dotnet run -- --refund 10.50    one-shot refund");
    Console.WriteLine("  dotnet run -- --online 10.50    force online authorization (populates auth no.)");
    Console.WriteLine("  dotnet run -- --port COM5 --baud 1200");
    Console.WriteLine("  dotnet run -- --reg 02          cash register number (default 01)");
    Console.WriteLine("  dotnet run -- --list-ports");
}

static void ListSerialPorts()
{
    string[] ports = SerialPort.GetPortNames();
    Console.WriteLine("Serial ports (.NET SerialPort.GetPortNames):");
    if (ports.Length == 0)
    {
        Console.WriteLine("  (none)");
        return;
    }

    foreach (string p in ports.OrderBy(x => x))
    {
        bool likelyUsb = p.Contains("usb", StringComparison.OrdinalIgnoreCase)
            || p.Contains("modem", StringComparison.OrdinalIgnoreCase)
            || p.Contains("ACM", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("COM", StringComparison.OrdinalIgnoreCase);
        Console.WriteLine(likelyUsb ? $"  {p}  ← likely USB/terminal" : $"  {p}");
    }

    if (OperatingSystem.IsMacOS())
    {
        Console.WriteLine();
        Console.WriteLine("macOS tip: use /dev/cu.* (call-out) for sending, not /dev/tty.*");
        IEnumerable<string> cuUsb = Directory.Exists("/dev")
            ? Directory.GetFiles("/dev", "cu.usb*")
                .Concat(Directory.GetFiles("/dev", "cu.wch*"))
                .OrderBy(x => x)
            : Array.Empty<string>();
        if (cuUsb.Any())
        {
            Console.WriteLine("Extra cu.usb* devices:");
            foreach (string d in cuUsb)
                Console.WriteLine($"  {d}");
        }
    }
}
