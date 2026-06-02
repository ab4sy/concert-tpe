using System.IO.Ports;
using CaisseApIpTester.Protocol;

if (args.Contains("--list-ports"))
{
    ListSerialPorts();
    return;
}

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║  CONCERT EURO/CAISSE Tester (Ref. 095)   ║");
Console.WriteLine("╚══════════════════════════════════════════╝");
Console.WriteLine();

Console.WriteLine("Transport:");
Console.WriteLine("  1. TCP/IP (gateway)");
Console.WriteLine("  2. RS-232 USB serial (7E1, CONCERT link)");
Console.Write("> ");

ITransport transport;

switch (Console.ReadLine()?.Trim())
{
    case "2":
        transport = BuildUsbTransport();
        break;
    default:
        transport = BuildTcpTransport();
        break;
}

string cashReg = Prompt("Cash register number (ECR#)", "01");

using (transport)
{
    while (true)
    {
        Console.WriteLine();
        Console.WriteLine("┌─ Menu ──────────────────────────────────┐");
        Console.WriteLine("│  1. Debit (TYPE=0)                       │");
        Console.WriteLine("│  2. Refund (TYPE=1)                      │");
        Console.WriteLine("│  3. Immediate ack (DELAI TLV A=1)        │");
        Console.WriteLine("│  4. Send raw A4 payload (no STX/ETX)    │");
        Console.WriteLine("│  0. Exit                                 │");
        Console.WriteLine("└──────────────────────────────────────────┘");
        Console.Write("> ");

        switch (Console.ReadLine()?.Trim())
        {
            case "1":
                await RunTransaction(transport, EuroCaisseTransactionType.Sale,
                    EuroCaisseAnswerDelay.EndOfTransaction, cashReg);
                break;
            case "2":
                await RunTransaction(transport, EuroCaisseTransactionType.Refund,
                    EuroCaisseAnswerDelay.EndOfTransaction, cashReg);
                break;
            case "3":
                await RunTransaction(transport, EuroCaisseTransactionType.Sale,
                    EuroCaisseAnswerDelay.Immediate, cashReg);
                break;
            case "4":
                await RunRaw(transport);
                break;
            case "0":
                return;
            default:
                Console.WriteLine("Unknown option.");
                break;
        }
    }
}

static ITransport BuildTcpTransport()
{
    string host = Prompt("Terminal / gateway IP", "192.168.1.100");
    int port = int.Parse(Prompt("TCP port", "8888"));
    return new CaisseApIpClient(host, port);
}

static ITransport BuildUsbTransport()
{
    string[] available = SerialPort.GetPortNames();
    if (available.Length == 0)
        Console.WriteLine("[WARN] No serial ports detected.");
    else
        Console.WriteLine("Detected ports: " + string.Join(", ", available));

    string defaultPort = available.Length > 0 ? available[0] : DetectDefaultPort();
    string portName = Prompt("COM port", defaultPort);
    int baud = int.Parse(Prompt("Baud rate (1200 mandatory, 9600 recommended)", "9600"));

    return new UsbSerialTransport(portName, baud);
}

static string DetectDefaultPort() =>
    OperatingSystem.IsWindows() ? "COM3" :
    OperatingSystem.IsMacOS() ? "/dev/cu.usbmodem1" :
    "/dev/ttyACM0";

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

static async Task RunTransaction(
    ITransport transport,
    EuroCaisseTransactionType type,
    EuroCaisseAnswerDelay delay,
    string cashReg)
{
    Console.Write("Amount (e.g. 10.50): ");
    if (!decimal.TryParse(Console.ReadLine()?.Trim(),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out decimal amount))
    {
        Console.WriteLine("[ERR] Invalid amount.");
        return;
    }

    var request = new TransactionRequest
    {
        Amount = amount,
        Type = type,
        CashRegisterNumber = cashReg,
        ResponseIndicator = '2',
        AnswerDelay = delay,
        IncludeAnswerDelayTlv = true
    };

    string payload = request.BuildPayload();
    Console.WriteLine();
    Console.WriteLine($"Request : {request}");
    Console.WriteLine($"A4 out  : {payload}");
    Console.WriteLine();

    try
    {
        string raw = await transport.SendAsync(payload);
        TransactionResponse.Parse(raw).Print();
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

static async Task RunRaw(ITransport transport)
{
    Console.Write("Raw A4 payload (between STX and ETX, ASCII): ");
    string? payload = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(payload)) return;

    try
    {
        string raw = await transport.SendAsync(payload);
        Console.WriteLine($"Raw response payload: {raw}");
        TransactionResponse.Parse(raw).Print();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERR] {ex.GetType().Name}: {ex.Message}");
    }
}

static string Prompt(string label, string defaultVal)
{
    Console.Write($"{label} [{defaultVal}]: ");
    string? input = Console.ReadLine()?.Trim();
    return string.IsNullOrEmpty(input) ? defaultVal : input;
}
