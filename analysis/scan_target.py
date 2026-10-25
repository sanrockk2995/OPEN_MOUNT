import os

targets = [
    r'D:\AI\MOUT\analysis\target_copy\RaiDrive.Mount.Service.x64.exe',
    r'D:\AI\MOUT\analysis\target_copy\RaiDrive.Mount.exe',
    r'D:\AI\MOUT\analysis\target_copy\RaiDrive.NotifyHelper.exe',
    r'D:\AI\MOUT\analysis\target_copy\RaiDrive.ShellExtension.x64.dll',
]

keywords = [
    b'coreclr.dll',
    b'BSJB',
    b'System.Private.CoreLib',
    b'winfsp',
    b'WinFsp',
    b'NativeAOT',
    b'SingleFile',
    b'OpenBoxLab',
    b'Microsoft.NETCore.App',
    b'Costura',
    b'FspFileSystem',
    b'fsp_',
    b'dokany',
    b'GoogleDrive',
    b'OneDrive',
    b'Dropbox',
    b'WebDAV',
    b'SFTP',
    b'FTP',
    b'S3'
]

for p in targets:
    fname = os.path.basename(p)
    if not os.path.exists(p):
        continue
    with open(p, 'rb') as f:
        data = f.read()
    print('========================================')
    print(f'File: {fname} (Size: {len(data)} bytes)')
    for kw in keywords:
        cnt = data.count(kw)
        if cnt > 0:
            pos = data.find(kw)
            print(f'  {kw.decode(errors="ignore")}: count={cnt}, first_offset={hex(pos)}')
