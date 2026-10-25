import struct

with open(r'D:\AI\MOUT\analysis\target_copy\RaiDrive.Mount.Service.x64.exe', 'rb') as f:
    data = f.read()

pos = 0
found = []
while True:
    idx = data.find(b'BSJB', pos)
    if idx == -1:
        break
    found.append(idx)
    pos = idx + 4

print(f'Total BSJB signatures found: {len(found)}')
for i, off in enumerate(found[:10]):
    # BSJB metadata header format:
    # 4 bytes: Signature (BSJB)
    # 2 bytes: MajorVersion
    # 2 bytes: MinorVersion
    # 4 bytes: Reserved
    # 4 bytes: Length of Version string
    # Version string
    maj, min, res, vlen = struct.unpack_from('<HHII', data, off + 4)
    vstr = data[off + 16 : off + 16 + vlen].decode(errors='ignore')
    print(f'[{i}] Offset: {hex(off)}, Version: {vstr}')
