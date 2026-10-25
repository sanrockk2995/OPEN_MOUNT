with open(r'D:\AI\MOUT\analysis\extracted_service\RaiDrive.Mount.Service.x64.dll', 'rb') as f:
    data = f.read()

import re
matches = re.findall(rb'(?:[A-Za-z0-9_\-\\]+\.db)', data)
print('ASCII .db matches:', set(matches))

# UTF-16 matches
u16_matches = re.findall(rb'(?:[A-Za-z0-9_\-\\]\x00)+\.db\x00', data)
decoded_u16 = set()
for m in u16_matches:
    try:
        decoded_u16.add(m.decode('utf-16le'))
    except:
        pass
print('UTF-16 .db matches:', decoded_u16)

# Look for SQLite connection strings (e.g. Data Source=...)
cs_matches = re.findall(rb'Data Source=[^\;\x00\"]+', data, re.IGNORECASE)
print('Connection strings:', set(cs_matches))
