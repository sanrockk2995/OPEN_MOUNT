import os
import struct

output_dir = r'D:\AI\MOUT\analysis\extracted_gui'
os.makedirs(output_dir, exist_ok=True)

with open(r'D:\AI\MOUT\analysis\target_copy\RaiDrive.Mount.exe', 'rb') as f:
    data = f.read()

pos = 0
mz_list = []
while True:
    idx = data.find(b'MZ', pos)
    if idx == -1:
        break
    if idx + 0x40 <= len(data):
        lfanew = int.from_bytes(data[idx + 0x3c : idx + 0x40], 'little')
        if lfanew == 0x80 and idx + lfanew + 4 <= len(data):
            if data[idx + lfanew : idx + lfanew + 4] == b'PE\x00\x00':
                mz_list.append(idx)
    pos = idx + 2

def get_assembly_name(file_bytes):
    bsjb = file_bytes.find(b'BSJB')
    if bsjb == -1:
        return None
    try:
        vlen = int.from_bytes(file_bytes[bsjb + 12 : bsjb + 16], 'little')
        streams_offset = bsjb + 16 + vlen
        num_streams = int.from_bytes(file_bytes[streams_offset + 2 : streams_offset + 4], 'little')
        cur = streams_offset + 4
        strings_offset = None
        tables_offset = None
        for _ in range(num_streams):
            s_off = int.from_bytes(file_bytes[cur : cur + 4], 'little')
            s_size = int.from_bytes(file_bytes[cur + 4 : cur + 8], 'little')
            cur += 8
            name_bytes = b''
            while cur < len(file_bytes) and file_bytes[cur] != 0:
                name_bytes += bytes([file_bytes[cur]])
                cur += 1
            cur = (cur + 4) & ~3
            s_name = name_bytes.decode('ascii', errors='ignore')
            if s_name == '#Strings':
                strings_offset = bsjb + s_off
            elif s_name in ('#~', '#-'):
                tables_offset = bsjb + s_off
        
        if strings_offset and tables_offset:
            heap_sizes = file_bytes[tables_offset + 6]
            valid_mask = int.from_bytes(file_bytes[tables_offset + 8 : tables_offset + 16], 'little')
            rows_offset = tables_offset + 24
            table_row_counts = {}
            for t in range(64):
                if (valid_mask & (1 << t)):
                    cnt = int.from_bytes(file_bytes[rows_offset : rows_offset + 4], 'little')
                    table_row_counts[t] = cnt
                    rows_offset += 4
                else:
                    table_row_counts[t] = 0
            
            string_index_size = 4 if (heap_sizes & 0x01) else 2
            
            if table_row_counts.get(0, 0) > 0:
                mod_row_offset = rows_offset
                name_idx_bytes = file_bytes[mod_row_offset + 2 : mod_row_offset + 2 + string_index_size]
                name_idx = int.from_bytes(name_idx_bytes, 'little')
                s_cur = strings_offset + name_idx
                end = file_bytes.find(b'\x00', s_cur)
                if end != -1:
                    return file_bytes[s_cur:end].decode('utf-8', errors='ignore')
    except Exception as e:
        pass
    return None

for i, off in enumerate(mz_list):
    pe_off = off + 0x80
    num_sections = int.from_bytes(data[pe_off + 6 : pe_off + 8], 'little')
    opt_header_size = int.from_bytes(data[pe_off + 20 : pe_off + 22], 'little')
    sec_start = pe_off + 24 + opt_header_size
    max_file_offset = 0
    for s in range(num_sections):
        s_off = sec_start + s * 40
        raw_size = int.from_bytes(data[s_off + 16 : s_off + 20], 'little')
        raw_ptr = int.from_bytes(data[s_off + 20 : s_off + 24], 'little')
        if raw_ptr + raw_size > max_file_offset:
            max_file_offset = raw_ptr + raw_size
    
    file_bytes = data[off : off + max_file_offset]
    mod_name = get_assembly_name(file_bytes)
    if not mod_name:
        mod_name = f'UnknownAssembly_{i:02d}.dll'
    if not mod_name.endswith('.dll') and not mod_name.endswith('.exe'):
        mod_name += '.dll'
    
    out_path = os.path.join(output_dir, mod_name)
    with open(out_path, 'wb') as out_f:
        out_f.write(file_bytes)
    print(f'[{i:02d}] {mod_name:45s} (Size: {len(file_bytes):8d} bytes, Offset: {hex(off)})')
