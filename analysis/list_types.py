import os
import struct

def dump_types(dll_path):
    with open(dll_path, 'rb') as f:
        file_bytes = f.read()

    bsjb = file_bytes.find(b'BSJB')
    if bsjb == -1:
        print('No BSJB found')
        return

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
    guid_index_size = 4 if (heap_sizes & 0x02) else 2
    blob_index_size = 4 if (heap_sizes & 0x04) else 2

    # Calculate offset of TypeDef table (table 0x02)
    # Tables before TypeDef: 0x00: Module, 0x01: TypeRef
    # Module row size: Generation (2), Name (string_index), Mvid (guid_index), EncId (guid_index), EncBaseId (guid_index)
    module_row_size = 2 + string_index_size + 3 * guid_index_size
    # TypeRef: ResolutionScope (coded index: 2 or 4), TypeName (string_index), TypeNamespace (string_index)
    # ResolutionScope coded index: 2 bits for tag (Module, ModuleRef, AssemblyRef, TypeRef)
    # Max rows among those 4 tables:
    max_scope = max(table_row_counts.get(0, 0), table_row_counts.get(0x1a, 0), table_row_counts.get(0x23, 0), table_row_counts.get(1, 0))
    res_scope_size = 4 if (max_scope >= (1 << 14)) else 2
    typeref_row_size = res_scope_size + 2 * string_index_size

    typedef_start = rows_offset + table_row_counts.get(0, 0) * module_row_size + table_row_counts.get(1, 0) * typeref_row_size

    # TypeDef row size: Flags (4), TypeName (string_index), TypeNamespace (string_index), Extends (coded index: TypeDefOrRef), FieldList (index into Field), MethodList (index into MethodDef)
    max_type_def_or_ref = max(table_row_counts.get(2, 0), table_row_counts.get(1, 0), table_row_counts.get(0x1b, 0))
    type_def_or_ref_size = 4 if (max_type_def_or_ref >= (1 << 14)) else 2
    field_index_size = 4 if (table_row_counts.get(4, 0) >= (1 << 16)) else 2
    method_index_size = 4 if (table_row_counts.get(6, 0) >= (1 << 16)) else 2

    typedef_row_size = 4 + 2 * string_index_size + type_def_or_ref_size + field_index_size + method_index_size

    def read_string(idx):
        s_cur = strings_offset + idx
        end = file_bytes.find(b'\x00', s_cur)
        return file_bytes[s_cur:end].decode('utf-8', errors='ignore')

    types = []
    num_typedefs = table_row_counts.get(2, 0)
    for r in range(num_typedefs):
        r_off = typedef_start + r * typedef_row_size
        name_idx = int.from_bytes(file_bytes[r_off + 4 : r_off + 4 + string_index_size], 'little')
        ns_idx = int.from_bytes(file_bytes[r_off + 4 + string_index_size : r_off + 4 + 2 * string_index_size], 'little')
        tname = read_string(name_idx)
        tns = read_string(ns_idx)
        full_name = f'{tns}.{tname}' if tns else tname
        types.append(full_name)
    return types

print('=== RaiDrive.Mount.Service.x64.dll Types ===')
svc_types = dump_types(r'D:\AI\MOUT\analysis\extracted_service\RaiDrive.Mount.Service.x64.dll')
print(f'Total types: {len(svc_types)}')
for t in svc_types[:80]:
    print(' ', t)

print('\n=== RaiDrive.Mount.dll (GUI) Types ===')
gui_types = dump_types(r'D:\AI\MOUT\analysis\extracted_gui\RaiDrive.Mount.dll')
print(f'Total types: {len(gui_types)}')
for t in gui_types[:80]:
    print(' ', t)
