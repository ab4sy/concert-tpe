# concert-tpe

CONCERT EURO/CAISSE (ref. 095 v2.0) tester — sends A4 payment requests to a TPE over RS-232 or TCP.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Python 3 (optional, for `mock_terminal.py`)

## Usage

```bash
dotnet run
```

List serial ports:

```bash
dotnet run -- --list-ports
```

Mock TPE (TCP):

```bash
python3 mock_terminal.py 8888 ok
```

## Protocol

- Application: fixed-field A4 message (ECR#, MNT, IND, EMETTEUR, TYPE, DEV, PRIV, optional TLV).
- Link: CONCERT ENQ/ACK/STX/ETX/LRC/EOT; TPV acts as secondary terminal.
- Serial: 7 data bits, even parity, 1 stop bit; 9600 baud recommended (1200 mandatory per spec).
