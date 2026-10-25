with open(r'D:\AI\MOUT\analysis\extracted_service\RaiDrive.Mount.Service.x64.dll', 'rb') as f:
    data = f.read()

# Let's search for string occurrences of providers or filesystem classes
# Let's find all types in RaiDrive.Service or Fsp
from dump_typedefs import types

fs_types = []
for tns, tname in types:
    full = f'{tns}.{tname}' if tns else tname
    if any(k in full.lower() for k in ['filesystem', 'fs', 'volume', 'mount', 'provider', 'storage', 'session', 'cache', 'buffer', 'channel', 'service']):
        fs_types.append(full)

print(f'Total matching filesystem/service types: {len(fs_types)}')
for t in sorted(fs_types):
    if not t.startswith('Microsoft.') and not t.startswith('System.') and not t.startswith('AWSSDK.') and not t.startswith('Rebex.'):
        print(' ', t)
