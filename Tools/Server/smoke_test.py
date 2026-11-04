#!/usr/bin/env python3
"""Start the actual Linux server, require its ready marker, and watch for startup exceptions."""
import argparse
import os
from pathlib import Path
import subprocess
import tempfile
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('binary', type=Path)
    parser.add_argument('--log', type=Path, default=Path('artifacts/server-smoke.log'))
    parser.add_argument('--timeout', type=float, default=90)
    args = parser.parse_args()
    binary = args.binary.resolve()
    log = args.log.resolve()
    log.parent.mkdir(parents=True, exist_ok=True)
    log.write_text('')
    with tempfile.TemporaryDirectory(prefix='pudgewars-server-smoke-') as state:
        environment = dict(os.environ, HOME=state, XDG_CONFIG_HOME=state + '/config', XDG_DATA_HOME=state + '/data')
        process = subprocess.Popen([str(binary), '-batchmode', '-nographics', '-server', '-port', '17777',
                                    '-teamSize', '3', '-bots', '1', '-botDifficulty', '1', '-logFile', str(log)],
                                   cwd=binary.parent, env=environment, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + args.timeout
            ready_at = None
            while time.monotonic() < deadline:
                contents = log.read_text(errors='replace')
                if process.poll() is not None:
                    raise RuntimeError(f'Server exited with code {process.returncode}; see {log}')
                if 'Exception:' in contents or '[Server] Startup failed:' in contents or '[Server] Resources/' in contents:
                    raise RuntimeError(f'Server startup failed; see {log}')
                if '[Server] Ready: scene=Arena, UDP port=17777, heroes=6' in contents:
                    ready_at = ready_at or time.monotonic()
                    if time.monotonic() - ready_at >= 3:
                        print('Server ready on UDP 17777 with six heroes; survived three seconds without startup exceptions.')
                        return
                time.sleep(0.25)
            raise RuntimeError(f'Server did not become ready within {args.timeout:g}s; see {log}')
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()


if __name__ == '__main__':
    main()
