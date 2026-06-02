#!/usr/bin/env python3
"""
Mock TPE for CONCERT EURO/CAISSE (ref. 095) testing.
Acts as primary terminal: receives secondary (TPV) session, then emits A4 response.

Usage: python3 mock_terminal.py [port] [mode]
  mode: ok (default), fail, immediate
"""
import socket
import sys
import time

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8888
MODE = sys.argv[2] if len(sys.argv) > 2 else "ok"

STX, ETX, ENQ, ACK, EOT, NAK = 0x02, 0x03, 0x05, 0x06, 0x04, 0x15


def lrc(payload: bytes) -> int:
    x = 0
    for b in payload:
        x ^= b
    return x ^ ETX


def wrap_a4(payload: str) -> bytes:
    data = payload.encode("ascii")
    return bytes([STX]) + data + bytes([ETX, lrc(data)])


def read_exact(conn: socket.socket, n: int) -> bytes:
    buf = b""
    while len(buf) < n:
        chunk = conn.recv(n - len(buf))
        if not chunk:
            raise ConnectionError("closed")
        buf += chunk
    return buf


def read_byte(conn: socket.socket, timeout=2.0) -> int:
    conn.settimeout(timeout)
    b = conn.recv(1)
    if not b:
        raise ConnectionError("closed")
    return b[0]


def read_data_frame(conn: socket.socket) -> str:
    while True:
        while True:
            b = read_byte(conn)
            if b == STX:
                break
        payload = bytearray()
        while True:
            b = read_byte(conn)
            if b == ETX:
                break
            payload.append(b)
        recv_lrc = read_byte(conn)
        if recv_lrc != lrc(payload):
            conn.send(bytes([NAK]))
            continue
        conn.send(bytes([ACK]))
        return payload.decode("ascii")


def receive_secondary_session(conn: socket.socket) -> str:
    while read_byte(conn) != ENQ:
        pass
    conn.send(bytes([ACK]))
    payload = read_data_frame(conn)
    while read_byte(conn) != EOT:
        pass
    return payload


def emit_primary_response(conn: socket.socket, payload: str) -> None:
    conn.send(bytes([ENQ]))
    while read_byte(conn) != ACK:
        pass
    conn.send(wrap_a4(payload))
    while read_byte(conn) != ACK:
        pass
    conn.send(bytes([EOT]))


def parse_request(payload: str) -> dict:
    if len(payload) < 26:
        raise ValueError(f"request too short: {len(payload)}")
    return {
        "ecr": payload[0:2],
        "mnt": payload[2:10],
        "ind": payload[10],
        "issuer": payload[11],
        "typ": payload[12],
        "dev": payload[13:16],
        "priv": payload[16:26],
        "tlv": payload[26:],
    }


def make_response(req: dict, mode: str) -> str:
    ecr = req["ecr"]
    mnt = req["mnt"]
    dev = req["dev"]
    priv = " " * 10

    if mode == "immediate":
        stat, mode_used = "9", "0"
    elif mode == "fail":
        stat, mode_used = "7", "0"
    else:
        stat, mode_used = "0", "1"

    body = ecr + stat + mnt + mode_used + dev + priv
    if req["ind"] == "1":
        pan = "4111111111111111     "
        body += pan.ljust(55)[:55]
    elif req["ind"] == "2" and mode == "ok":
        # TLV type C = authorization number (example from spec)
        body += "C06123456"
    return body


def handle(conn: socket.socket) -> None:
    req_payload = receive_secondary_session(conn)
    print(f"[mock] Request payload: {req_payload!r}")
    req = parse_request(req_payload)
    print(f"[mock] Parsed: {req}")
    resp = make_response(req, MODE)
    print(f"[mock] Response payload: {resp!r}")
    time.sleep(0.05)
    emit_primary_response(conn, resp)
    print("[mock] Response sent.")


def main() -> None:
    print(f"[mock] TPE on port {PORT}, mode={MODE}")
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as srv:
        srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        srv.bind(("0.0.0.0", PORT))
        srv.listen(5)
        while True:
            conn, addr = srv.accept()
            print(f"[mock] Connection from {addr}")
            with conn:
                try:
                    handle(conn)
                except Exception as exc:
                    print(f"[mock] Error: {exc}")
            print("[mock] Waiting for next connection...")


if __name__ == "__main__":
    main()
