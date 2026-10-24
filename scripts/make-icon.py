"""Regenerate every platform icon from the shared SVG master."""
import os
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parents[1]
subprocess.run([os.environ.get("DOTNET_PATH", "dotnet"), "run", "--project",
                str(root / "tools" / "YingView.IconBuilder"), "-c", "Release"],
               cwd=root, check=True)
