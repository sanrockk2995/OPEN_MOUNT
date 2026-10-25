# Search for bundle signature in .NET single-file
# .NET bundle signatures:
# .NET Core 3.x / .NET 5 / 6 / 7 / 8 / 9:
# Look for bundle identifier or embedded PE files (MZ header 0x4D 0x5A)

with open(r'D:\AI\MOUT\analysis\target_copy\RaiDrive.Mount.Service.x64.exe', 'rb') as f:
    data = f.read()

# Let's find all occurrences of 'MZ' (0x4D, 0x5A)
pos = 0
mz_offsets = []
while True:
    idx = data.find(b'MZ', pos)
    if idx == -1:
        break
    # Check if it has PE header (e_lfanew at 0x3C)
    if idx + 0x40 <= len(data):
        lfanew = int.from_bytes(data[idx + 0x3c : idx + 0x40], 'little')
        if lfanew < 0x1000 and idx + lfanew + 4 <= len(data):
            if data[idx + lfanew : idx + lfanew + 4] == b'PE\x00\x00':
                mz_offsets.append((idx, lfanew))
    pos = idx + 2

print(f'Total embedded PE files with valid PE header: {len(mz_offsets)}')
for off, lfanew in mz_offsets:
    print(f'  Offset: {hex(off)}, e_lfanew: {hex(lfanew)}')
