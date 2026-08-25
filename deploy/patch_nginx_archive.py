from pathlib import Path
import re

path = Path("/etc/nginx/sites-available/doublemark.ru")
text = path.read_text(encoding="utf-8")
bak = path.with_suffix(path.suffix + ".bak-20260824-ico")
if not bak.exists():
    bak.write_text(text, encoding="utf-8")

# Remove blanket archive 404
text2 = re.sub(
    r"\n\s*location \^~ /downloads/archive/\s*\{[^}]*\}\s*",
    "\n",
    text,
    count=1,
)

# Ensure 2.x block covers both root and archive paths
if "DoubleMarkSetup-2" in text2:
    text2 = re.sub(
        r"location ~\* \^/downloads/DoubleMarkSetup-2\\\.\s*\{[^}]*\}",
        "location ~* ^/downloads/(?:archive/)?DoubleMarkSetup-2\\. {\n        return 404;\n    }",
        text2,
        count=1,
    )
else:
    text2 = text2.replace(
        "location / {\n        try_files $uri $uri/ /index.html;\n    }",
        "location / {\n        try_files $uri $uri/ /index.html;\n    }\n\n    location ~* ^/downloads/(?:archive/)?DoubleMarkSetup-2\\. {\n        return 404;\n    }",
        1,
    )

path.write_text(text2, encoding="utf-8")
print("patched")
print("--- relevant ---")
for i, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
    if "download" in line.lower() or "archive" in line.lower() or "DoubleMark" in line:
        print(f"{i}:{line}")
