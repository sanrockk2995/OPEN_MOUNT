with open(r'D:\AI\MOUT\analysis\extracted_service\RaiDrive.Mount.Service.x64.dll', 'rb') as f:
    data = f.read()

import re
matches = re.findall(rb'[^\x00]{4,100}', data)
fsp_strings = []
for m in matches:
    if any(k in m.lower() for k in [b'winfsp', b'fsp_', b'fspfilesystem', b'mountpoint', b'volume']):
        try:
            s = m.decode('utf-8')
            fsp_strings.append(s)
        except:
            pass

print(f'Total matches: {len(fsp_strings)}')
for s in sorted(set(fsp_strings))[:60]:
    print(' ', s)
