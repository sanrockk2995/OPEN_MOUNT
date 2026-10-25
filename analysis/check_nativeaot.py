with open(r'D:\AI\MOUT\analysis\target_copy\RaiDrive.Mount.Service.x64.exe', 'rb') as f:
    data = f.read()

idx = data.find(b'NativeAOT')
while idx != -1:
    snippet = data[max(0, idx - 40) : min(len(data), idx + 80)]
    print(f'Offset {hex(idx)}: {snippet}')
    idx = data.find(b'NativeAOT', idx + 1)
