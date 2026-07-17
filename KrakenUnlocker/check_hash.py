import hashlib

exe = r"bin\Release\net8.0-windows10.0.22621.0\win-x64\publish\KrakenXboxUnlocker.exe"
sha_file = r"bin\Release\net8.0-windows10.0.22621.0\win-x64\publish\KrakenXboxUnlocker.sha256"

with open(exe, "rb") as f:
    actual = hashlib.sha256(f.read()).hexdigest()

with open(sha_file, "r") as f:
    stored = f.read().strip()

print(f"EXE hash:    {actual}")
print(f"Stored hash: {stored}")
print(f"Match: {actual == stored}")
