with open(r'D:\AI\MOUT\analysis\target_copy\RaiDrive.Mount.Service.x64.exe', 'rb') as f:
    f.seek(-1024, 2)
    tail = f.read()

print('Tail length:', len(tail))
print('Tail hex (last 128 bytes):', tail[-128:].hex())
print('Tail ascii (last 256 bytes):', repr(tail[-256:]))
