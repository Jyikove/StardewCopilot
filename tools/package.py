"""Create a UTF-8 ZIP with executable permissions, then replace the output atomically."""
from pathlib import Path
import sys
import tempfile
import zipfile

source = Path(sys.argv[1]).resolve()
output = Path(sys.argv[2]).resolve()
output.parent.mkdir(parents=True, exist_ok=True)
names = ("StardewCopilot.dll", "HtmlAgilityPack.dll", "manifest.json", "README.md", "TESTING.md", "INSTALL.txt", "THIRD-PARTY-NOTICES.txt")
for name in names:
    if not (source / name).is_file() or (source / name).is_symlink():
        raise SystemExit(f"Missing required distribution file: {name}")
with tempfile.NamedTemporaryFile(dir=output.parent, suffix=".zip", delete=False) as handle:
    temporary = Path(handle.name)
try:
    with zipfile.ZipFile(temporary, "w", zipfile.ZIP_DEFLATED) as archive:
        for name in names:
            path = source / name
            archive.write(path, path.relative_to(source.parent))
    temporary.replace(output)
    output.chmod(0o644)
finally:
    temporary.unlink(missing_ok=True)
