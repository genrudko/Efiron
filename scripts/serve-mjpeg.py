#!/usr/bin/env python3
"""Serve one JPEG frame as an endless multipart MJPEG stream."""

from __future__ import annotations

import argparse
import http.server
import pathlib
import socketserver
import time
from typing import Final

BOUNDARY: Final[bytes] = b"efiron-frame"
STREAM_PATHS: Final[frozenset[str]] = frozenset(
    {"/stream.mjpg", "/stream.mpjpeg"}
)


class MjpegHandler(http.server.BaseHTTPRequestHandler):
    frame: bytes = b""
    frame_interval_seconds: float = 0.1

    def do_GET(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler contract
        if self.path == "/health":
            payload = b"ok\n"
            self.send_response(200)
            self.send_header("Content-Type", "text/plain; charset=utf-8")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)
            return

        if self.path == "/frame.jpg":
            self.send_response(200)
            self.send_header("Content-Type", "image/jpeg")
            self.send_header("Content-Length", str(len(self.frame)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(self.frame)
            return

        if self.path not in STREAM_PATHS:
            self.send_error(404)
            return

        self.send_response(200)
        self.send_header(
            "Content-Type",
            "multipart/x-mixed-replace; boundary=efiron-frame",
        )
        self.send_header("Cache-Control", "no-store, no-cache, must-revalidate")
        self.send_header("Pragma", "no-cache")
        self.send_header("Connection", "close")
        self.end_headers()

        try:
            while True:
                self.wfile.write(b"--" + BOUNDARY + b"\r\n")
                self.wfile.write(b"Content-Type: image/jpeg\r\n")
                self.wfile.write(
                    f"Content-Length: {len(self.frame)}\r\n\r\n".encode("ascii")
                )
                self.wfile.write(self.frame)
                self.wfile.write(b"\r\n")
                self.wfile.flush()
                time.sleep(self.frame_interval_seconds)
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            return

    def log_message(self, format: str, *args: object) -> None:
        return


class ThreadingServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--frame", required=True, type=pathlib.Path)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", default=18770, type=int)
    parser.add_argument("--fps", default=10.0, type=float)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    if args.fps <= 0:
        raise ValueError("fps must be positive")

    frame = args.frame.read_bytes()
    if len(frame) < 1024:
        raise ValueError("JPEG fixture is unexpectedly small")

    MjpegHandler.frame = frame
    MjpegHandler.frame_interval_seconds = 1.0 / args.fps
    with ThreadingServer((args.host, args.port), MjpegHandler) as server:
        server.serve_forever(poll_interval=0.1)


if __name__ == "__main__":
    main()
