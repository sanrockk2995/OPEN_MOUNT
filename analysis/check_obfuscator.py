with open(r'D:\AI\MOUT\analysis\extracted_service\RaiDrive.Mount.Service.x64.dll', 'rb') as f:
    data = f.read()

import re
obf_matches = re.findall(rb'(?:Babel|Confuser|Reactor|SmartAssembly|Eazfuscator|Dotfuscator|CryptoObfuscator|Yano)[a-zA-Z0-9_\.]*', data)
print('Obfuscator signatures found:', set(obf_matches))

# Also search for custom attributes on assembly
import dump_typedefs
# Let's see all custom attributes
