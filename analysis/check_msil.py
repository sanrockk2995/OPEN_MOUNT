import sys
from dump_typedefs import dump_types

p = r"C:\Program Files (x86)\WinFsp\bin\winfsp-msil.dll"
# But winfsp-msil.dll has standard CLI metadata
# Let's check with inspect_managed_members
