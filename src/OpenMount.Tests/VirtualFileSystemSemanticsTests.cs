using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Fsp.Interop;
using OpenMount.Core.FileSystem;
using OpenMount.Core.Storage.Local;
using Xunit;
using FileInfo = Fsp.Interop.FileInfo;

namespace OpenMount.Tests
{
    public class VirtualFileSystemSemanticsTests : IDisposable
    {
        private readonly string _testRoot;
        private readonly string _stagingRoot;
        private readonly LocalStorageProvider _storage;
        private readonly VirtualFileSystem _vfs;

        public VirtualFileSystemSemanticsTests()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "OpenMount_VFS_Root_" + Guid.NewGuid().ToString("N"));
            _stagingRoot = Path.Combine(Path.GetTempPath(), "OpenMount_VFS_Staging_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRoot);
            Directory.CreateDirectory(_stagingRoot);

            _storage = new LocalStorageProvider(_testRoot, "VFS Test");
            _vfs = new VirtualFileSystem(_storage, _stagingRoot);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, true);
                if (Directory.Exists(_stagingRoot)) Directory.Delete(_stagingRoot, true);
            }
            catch { }
        }

        [Fact]
        public void Test_GetVolumeInfo_ReturnsSuccess()
        {
            int status = _vfs.GetVolumeInfo(out var vol);
            Assert.Equal(0, status);
            Assert.True(vol.TotalSize > 0);
        }

        [Fact]
        public void Test_GetSecurityByName_RootAndSubpath()
        {
            byte[] sd = null!;
            int statusRoot = _vfs.GetSecurityByName("\\", out var rootAttrs, ref sd);
            Assert.Equal(0, statusRoot);
            Assert.True((rootAttrs & (uint)FileAttributes.Directory) != 0);

            int statusMissing = _vfs.GetSecurityByName("\\non_existent.txt", out var missingAttrs, ref sd);
            Assert.NotEqual(0, statusMissing);
        }

        [Fact]
        public void Test_CreateWriteReadCleanup_RoundTrip()
        {
            string fileName = "\\test_vfs.txt";
            string testData = "Antigravity OpenMount Virtual Filesystem Core Engine!";
            byte[] bytes = Encoding.UTF8.GetBytes(testData);

            // 1. Create file
            int createStatus = _vfs.Create(
                fileName,
                0, // createOptions (file)
                0x40000000, // GENERIC_WRITE
                (uint)FileAttributes.Normal,
                null!,
                0,
                out var fileNode,
                out var fileDesc,
                out var fileInfo,
                out var normalizedName);

            Assert.Equal(0, createStatus);
            Assert.NotNull(fileNode);
            Assert.NotNull(fileDesc);

            // 2. Write data into file via unmanaged buffer
            IntPtr unmanaged = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, unmanaged, bytes.Length);
                int writeStatus = _vfs.Write(
                    fileNode,
                    fileDesc,
                    unmanaged,
                    0,
                    (uint)bytes.Length,
                    false,
                    false,
                    out uint written,
                    out fileInfo);

                Assert.Equal(0, writeStatus);
                Assert.Equal((uint)bytes.Length, written);
                Assert.Equal((ulong)bytes.Length, fileInfo.FileSize);
            }
            finally
            {
                Marshal.FreeHGlobal(unmanaged);
            }

            // 3. Cleanup & Close (commits staged write to underlying storage)
            _vfs.Cleanup(fileNode, fileDesc, fileName, 0);
            _vfs.Close(fileNode, fileDesc);

            // 4. Reopen for read
            int openStatus = _vfs.Open(
                fileName,
                0,
                0x80000000, // GENERIC_READ
                out var readNode,
                out var readDesc,
                out var readFileInfo,
                out var norm);

            Assert.Equal(0, openStatus);
            Assert.Equal((ulong)bytes.Length, readFileInfo.FileSize);

            // 5. Read bytes
            IntPtr readBuffer = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                int readStatus = _vfs.Read(
                    readNode,
                    readDesc,
                    readBuffer,
                    0,
                    (uint)bytes.Length,
                    out uint bytesRead);

                Assert.Equal(0, readStatus);
                Assert.Equal((uint)bytes.Length, bytesRead);

                byte[] result = new byte[bytesRead];
                Marshal.Copy(readBuffer, result, 0, (int)bytesRead);
                string str = Encoding.UTF8.GetString(result);
                Assert.Equal(testData, str);
            }
            finally
            {
                Marshal.FreeHGlobal(readBuffer);
                _vfs.Close(readNode, readDesc);
            }
        }

        [Fact]
        public void Test_ReadDirectoryEntry_IncludesDotAndDotDot()
        {
            // Create a test file in root
            string filePath = Path.Combine(_testRoot, "hello.txt");
            File.WriteAllText(filePath, "test");

            int openStatus = _vfs.Open("\\", (uint)1, 0, out var rootNode, out var rootDesc, out _, out _);
            Assert.Equal(0, openStatus);

            object ctx = null!;
            var entries = new System.Collections.Generic.List<string>();

            while (_vfs.ReadDirectoryEntry(rootNode, rootDesc, "*", null!, ref ctx, out string name, out _))
            {
                entries.Add(name);
            }

            Assert.Contains(".", entries);
            Assert.Contains("..", entries);
            Assert.Contains("hello.txt", entries);

            _vfs.Close(rootNode, rootDesc);
        }

        [Fact]
        public void Test_SetFileSize_TruncateAndExpand()
        {
            string fileName = "\\size_test.dat";
            _vfs.Create(fileName, 0, 0x40000000, 0, null!, 0, out var node, out var desc, out _, out _);

            // Expand to 1024 bytes
            int setStatus1 = _vfs.SetFileSize(node, desc, 1024, false, out var info1);
            Assert.Equal(0, setStatus1);
            Assert.Equal(1024UL, info1.FileSize);

            // Truncate to 512 bytes
            int setStatus2 = _vfs.SetFileSize(node, desc, 512, false, out var info2);
            Assert.Equal(0, setStatus2);
            Assert.Equal(512UL, info2.FileSize);

            _vfs.Cleanup(node, desc, fileName, 0);
            _vfs.Close(node, desc);
        }

        [Fact]
        public void Test_ConcurrentHandles_ShareSameNode()
        {
            string fileName = "\\shared_node_test.txt";
            _vfs.Create(fileName, 0, 0x40000000, 0, null!, 0, out var node1, out var desc1, out _, out _);
            _vfs.Cleanup(node1, desc1, fileName, 0);

            // Open twice concurrently
            _vfs.Open(fileName, 0, 0x80000000, out var openNode1, out var openDesc1, out _, out _);
            _vfs.Open(fileName, 0, 0x80000000, out var openNode2, out var openDesc2, out _, out _);

            Assert.Same(openNode1, openNode2);
            var vNode = (VirtualFileNode)openNode1;
            Assert.True(vNode.OpenCount >= 2);

            _vfs.Close(openNode1, openDesc1);
            _vfs.Close(openNode2, openDesc2);
            _vfs.Close(node1, desc1);
        }
    }
}
