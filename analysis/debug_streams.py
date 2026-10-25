with open(r'D:\AI\MOUT\analysis\extracted_service\RaiDrive.Mount.Service.x64.dll', 'rb') as f:
    file_bytes = f.read()

bsjb = file_bytes.find(b'BSJB')
vlen = int.from_bytes(file_bytes[bsjb + 12 : bsjb + 16], 'little')
vstr = file_bytes[bsjb + 16 : bsjb + 16 + vlen]
print('BSJB at:', hex(bsjb), 'vlen:', vlen, 'vstr:', vstr)
streams_offset = bsjb + 16 + vlen
flags = int.from_bytes(file_bytes[streams_offset : streams_offset + 2], 'little')
num_streams = int.from_bytes(file_bytes[streams_offset + 2 : streams_offset + 4], 'little')
print('num_streams:', num_streams)
cur = streams_offset + 4
for i in range(num_streams):
    s_off = int.from_bytes(file_bytes[cur : cur + 4], 'little')
    s_size = int.from_bytes(file_bytes[cur + 4 : cur + 8], 'little')
    cur += 8
    name_bytes = b''
    while cur < len(file_bytes) and file_bytes[cur] != 0:
        name_bytes += bytes([file_bytes[cur]])
        cur += 1
    # Skip null bytes to 4-byte boundary
    while cur % 4 != 0 or file_bytes[cur] == 0:
        cur += 1
        if cur % 4 == 0:
            break
    print(f'  Stream {i}: off={hex(s_off)}, size={hex(s_size)}, name={name_bytes}')
