import os
import pathlib
import re

path = pathlib.Path("README.md")
text = path.read_text(encoding="utf-8")
version = os.environ["VERSION"]
repo = os.environ["REPO"]
badge = (
    f"[![version](https://img.shields.io/badge/version-{version}-blue?style=flat-square)]"
    f"(https://github.com/{repo}/releases/tag/{version})"
)
updated, count = re.subn(r"\[!\[version\].*", badge, text, count=1)
if count != 1:
    raise SystemExit("version badge line not found")
path.write_text(updated, encoding="utf-8")
