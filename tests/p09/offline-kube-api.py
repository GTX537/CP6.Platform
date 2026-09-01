#!/usr/bin/env python3
"""Loopback-only 404 sentinel for kubectl client dry-run apply."""

import argparse
import json
import pathlib
import ssl
from http.server import BaseHTTPRequestHandler, HTTPServer


class ReadOnlyNotFoundHandler(BaseHTTPRequestHandler):
    violation_path: pathlib.Path

    def do_GET(self) -> None:  # noqa: N802 - stdlib handler contract
        payload = json.dumps(
            {
                "kind": "Status",
                "apiVersion": "v1",
                "metadata": {},
                "status": "Failure",
                "message": "offline object does not exist",
                "reason": "NotFound",
                "code": 404,
            },
            separators=(",", ":"),
        ).encode("utf-8")
        self.send_response(404)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def _reject_mutation(self) -> None:
        self.violation_path.write_text(self.command, encoding="utf-8")
        self.send_response(405)
        self.send_header("Content-Length", "0")
        self.end_headers()

    do_POST = _reject_mutation
    do_PUT = _reject_mutation
    do_PATCH = _reject_mutation
    do_DELETE = _reject_mutation

    def log_message(self, _format: str, *_args: object) -> None:
        return


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--cert", required=True, type=pathlib.Path)
    parser.add_argument("--key", required=True, type=pathlib.Path)
    parser.add_argument("--ready", required=True, type=pathlib.Path)
    parser.add_argument("--violation", required=True, type=pathlib.Path)
    args = parser.parse_args()

    ReadOnlyNotFoundHandler.violation_path = args.violation
    server = HTTPServer(("127.0.0.1", 1), ReadOnlyNotFoundHandler)
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.load_cert_chain(certfile=args.cert, keyfile=args.key)
    server.socket = context.wrap_socket(server.socket, server_side=True)
    args.ready.write_text("ready", encoding="utf-8")
    server.serve_forever()


if __name__ == "__main__":
    main()
