with open(r'D:\AI\MOUT\analysis\extracted_service\RaiDrive.Mount.Service.x64.dll', 'rb') as f:
    data = f.read()

import re
matches = re.findall(rb'CREATE TABLE[^\;]{5,200}', data, re.IGNORECASE)
print(f'Total CREATE TABLE queries found: {len(matches)}')
for m in sorted(set(matches)):
    print(' ', m.decode('ascii', errors='ignore'))
