with open(r'D:\AI\MOUT\analysis\extracted_service\RaiDrive.Mount.Service.x64.dll', 'rb') as f:
    data = f.read()

lfanew = int.from_bytes(data[0x3c:0x40], 'little')
opt_magic = int.from_bytes(data[lfanew + 24 : lfanew + 26], 'little')
dd_offset = lfanew + 24 + (112 if opt_magic == 0x20b else 96)
cli_dd = dd_offset + 14 * 8
cli_rva = int.from_bytes(data[cli_dd : cli_dd + 4], 'little')
cli_size = int.from_bytes(data[cli_dd + 4 : cli_dd + 8], 'little')
print(f'Magic: {hex(opt_magic)}, CLI Directory RVA: {hex(cli_rva)}, Size: {hex(cli_size)}')

num_sections = int.from_bytes(data[lfanew + 6 : lfanew + 8], 'little')
opt_size = int.from_bytes(data[lfanew + 20 : lfanew + 22], 'little')
sec_start = lfanew + 24 + opt_size

sections = []
for s in range(num_sections):
    s_off = sec_start + s * 40
    name = data[s_off : s_off + 8].rstrip(b'\x00').decode('ascii', errors='ignore')
    vsize = int.from_bytes(data[s_off + 8 : s_off + 12], 'little')
    vrva = int.from_bytes(data[s_off + 12 : s_off + 16], 'little')
    rsize = int.from_bytes(data[s_off + 16 : s_off + 20], 'little')
    rptr = int.from_bytes(data[s_off + 20 : s_off + 24], 'little')
    sections.append((name, vrva, vsize, rptr, rsize))

def rva_to_offset(rva):
    for name, vrva, vsize, rptr, rsize in sections:
        if vrva <= rva < vrva + max(vsize, rsize):
            return rptr + (rva - vrva)
    return None

cli_file_off = rva_to_offset(cli_rva)
meta_rva = int.from_bytes(data[cli_file_off + 8 : cli_file_off + 12], 'little')
meta_size = int.from_bytes(data[cli_file_off + 12 : cli_file_off + 16], 'little')
meta_off = rva_to_offset(meta_rva)
print(f'Metadata RVA: {hex(meta_rva)}, Size: {hex(meta_size)}, File Offset: {hex(meta_off)}')

bsjb = data[meta_off : meta_off + 4]
vlen = int.from_bytes(data[meta_off + 12 : meta_off + 16], 'little')
vstr = data[meta_off + 16 : meta_off + 16 + vlen]
print(f'Version string ({vlen} bytes): {vstr}')
streams_offset = meta_off + 16 + vlen
flags = int.from_bytes(data[streams_offset : streams_offset + 2], 'little')
num_streams = int.from_bytes(data[streams_offset + 2 : streams_offset + 4], 'little')
print(f'Num streams: {num_streams}')

cur = streams_offset + 4
streams = {}
for i in range(num_streams):
    s_off = int.from_bytes(data[cur : cur + 4], 'little')
    s_size = int.from_bytes(data[cur + 4 : cur + 8], 'little')
    cur += 8
    name_bytes = b''
    while cur < len(data) and data[cur] != 0:
        name_bytes += bytes([data[cur]])
        cur += 1
    cur += 1
    while cur % 4 != 0:
        cur += 1
    s_name = name_bytes.decode('ascii')
    streams[s_name] = (meta_off + s_off, s_size)
    print(f'  Stream {s_name}: offset={hex(meta_off + s_off)}, size={hex(s_size)}')
