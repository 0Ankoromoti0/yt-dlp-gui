"""Exercise the packaged native GUI with a bounded runtime on the macOS runner."""
import os
import pathlib
import subprocess
import sys

app = pathlib.Path(sys.argv[1]).resolve()
environment = dict(os.environ)
environment["YTDLPGUI_SETTINGS_DIR"] = str(pathlib.Path("artifacts/smoke-settings").resolve())
environment["YTDLPGUI_PREVIEW_DIR"] = str(pathlib.Path("artifacts/preview").resolve())
subprocess.run([str(app / "Contents/MacOS/YtDlpGui"), "--smoke-test"],
               env=environment, check=True, timeout=90)
