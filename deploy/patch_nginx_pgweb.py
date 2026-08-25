from pathlib import Path

path = Path("/etc/nginx/sites-enabled/doublemark.ru")
text = path.read_text()
updated = text.replace("    include /etc/nginx/snippets/doublemark-pgweb.conf;\n", "")
if updated == text:
    print("nginx include already absent")
else:
    path.write_text(updated)
    print("nginx include removed")
