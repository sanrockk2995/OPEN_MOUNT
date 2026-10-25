with open(r'D:\AI\MOUT\analysis\extracted_service\RaiDrive.Mount.Service.x64.dll', 'rb') as f:
    data = f.read()

# Metadata offsets
tables_off = 0x555650
strings_off = 0x622e14

def read_str(idx):
    end = data.find(b'\x00', strings_off + idx)
    return data[strings_off + idx : end].decode('utf-8', errors='ignore')

heap_sizes = data[tables_off + 6]
string_idx_size = 4 if (heap_sizes & 0x01) else 2
guid_idx_size = 4 if (heap_sizes & 0x02) else 2
blob_idx_size = 4 if (heap_sizes & 0x04) else 2

valid_mask = int.from_bytes(data[tables_off + 8 : tables_off + 16], 'little')
rows_off = tables_off + 24

table_counts = {}
for t in range(64):
    if valid_mask & (1 << t):
        cnt = int.from_bytes(data[rows_off : rows_off + 4], 'little')
        table_counts[t] = cnt
        rows_off += 4
    else:
        table_counts[t] = 0

# Tables before TypeDef (0x02):
# 0x00: Module
# 0x01: TypeRef
mod_row_size = 2 + string_idx_size + 3 * guid_idx_size

max_typeref_scope = max(table_counts[0], table_counts[0x1a], table_counts[0x23], table_counts[1])
typeref_scope_size = 4 if max_typeref_scope >= (1 << 14) else 2
typeref_row_size = typeref_scope_size + 2 * string_idx_size

typedef_start = rows_off + table_counts[0] * mod_row_size + table_counts[1] * typeref_row_size

max_typedef_or_ref = max(table_counts[2], table_counts[1], table_counts[0x1b])
typedef_or_ref_size = 4 if max_typedef_or_ref >= (1 << 14) else 2
field_idx_size = 4 if table_counts[4] >= (1 << 16) else 2
method_idx_size = 4 if table_counts[6] >= (1 << 16) else 2

typedef_row_size = 4 + 2 * string_idx_size + typedef_or_ref_size + field_idx_size + method_idx_size

types = []
for r in range(table_counts[2]):
    r_off = typedef_start + r * typedef_row_size
    flags = int.from_bytes(data[r_off : r_off + 4], 'little')
    name_idx = int.from_bytes(data[r_off + 4 : r_off + 4 + string_idx_size], 'little')
    ns_idx = int.from_bytes(data[r_off + 4 + string_idx_size : r_off + 4 + 2 * string_idx_size], 'little')
    tname = read_str(name_idx)
    tns = read_str(ns_idx)
    types.append((tns, tname))

print(f'Total TypeDefs in RaiDrive.Mount.Service.x64: {len(types)}')

# Filter interesting namespaces / types
interesting = []
for tns, tname in types:
    full = f'{tns}.{tname}' if tns else tname
    if any(k in full.lower() for k in ['raidrive', 'mount', 'fsp', 'drive', 'storage', 'provider', 'cache', 'auth', 'oauth', 'service', 'volume', 's3', 'webdav', 'ftp', 'sftp']):
        interesting.append(full)

print(f'Interesting types: {len(interesting)}')
for t in sorted(interesting)[:100]:
    print(' ', t)
