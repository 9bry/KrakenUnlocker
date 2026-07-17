import hashlib, os

exe = r"bin\Release\net8.0-windows10.0.22621.0\win-x64\publish\KrakenXboxUnlocker.exe"
sha256_file = r"bin\Release\net8.0-windows10.0.22621.0\win-x64\publish\KrakenXboxUnlocker.sha256"

with open(exe, "rb") as f:
    h = hashlib.sha256(f.read()).hexdigest()

with open(sha256_file, "w") as f:
    f.write(h)

print(f"Hash: {h}")
print(f"Saved to: {sha256_file}")
