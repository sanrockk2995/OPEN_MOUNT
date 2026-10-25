import pefile
import re

with open(r'D:\AI\MOUT\analysis\target_copy\RaiDrive.Mount.Service.x64.exe', 'rb') as f:
    data = f.read()

# Find strings ending with .dll
dll_matches = re.findall(rb'[A-Za-z0-9_\-\.]+\.dll', data)
unique_dlls = sorted(set(d.decode('ascii', errors='ignore') for d in dll_matches))
print('Count of unique .dll strings:', len(unique_dlls))
for d in unique_dlls:
    if any(k in d.lower() for k in ['raidrive', 'fsp', 'mount', 'service', 'storage', 'cloud', 'system', 'microsoft']):
        print(' ', d)
