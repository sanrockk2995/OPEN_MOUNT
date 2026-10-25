with open(r'D:\AI\MOUT\analysis\extracted_service\RaiDrive.Mount.Service.x64.dll', 'rb') as f:
    data = f.read()

import re
matches = [m.start() for m in re.finditer(rb'SqliteConnection', data)]
print('SqliteConnection occurrences at file offsets:', [hex(m) for m in matches])
for m in matches:
    print('Context around', hex(m), ':', repr(data[max(0, m - 50) : min(len(data), m + 100)]))
