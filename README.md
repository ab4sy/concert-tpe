# concert-tpe

CONCERT EURO/CAISSE (ref. 095 v2.0) tester — sends A4 payment requests to a TPE over RS-232 USB serial.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download)

## Usage

Connects to the terminal over USB serial (auto-detected COM port, 9600 7E1),
then just asks for an amount and runs a sale. Insert the card on the TPE to
complete.

```bash
dotnet run                       # interactive sale loop (auto-detect port)
dotnet run -- 10.50              # one-shot sale of 10.50 then exit
dotnet run -- --refund 7.25      # one-shot refund
dotnet run -- --online 7.25      # force online authorization (populates auth number)
dotnet run -- --port COM5 --baud 1200
dotnet run -- --reg 02           # cash register number (default 01)
dotnet run -- --list-ports
```

In the interactive loop, type an amount for a sale, or prefix with `r` for a
refund (e.g. `r 10.50`). Blank line quits.

## Protocol

- Application: fixed-field A4 message (ECR#, MNT, IND, EMETTEUR, TYPE, DEV, PRIV, optional TLV).
- Link: CONCERT ENQ/ACK/STX/ETX/LRC/EOT; TPV acts as secondary terminal.
- Serial: 7 data bits, even parity, 1 stop bit; 9600 baud recommended (1200 mandatory per spec).
