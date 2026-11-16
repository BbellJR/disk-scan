// Disk Scan - ดูว่าอะไรกินพื้นที่ในไดร์ฟ
// เขียนด้วย C# (WinForms, .NET Framework 4.x) -> ได้ไฟล์ .exe ไฟล์เดียว เอาไปใช้เครื่องไหนก็ได้ (Windows 10/11)
// โปรแกรมนี้ "อ่านอย่างเดียว" ไม่มีการลบไฟล์ใดๆ ทั้งสิ้น

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiskScan
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string autoScan = null;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--scan") autoScan = args[i + 1];
            if (Native.IsAdmin()) Native.EnableBackupPrivilege();
            Application.Run(new MainForm(autoScan));
        }
    }

    // ------------------------------------------------------------------ data

    class DirNode
    {
        public string Name;
        public DirNode Parent;
        public List<DirNode> Children;
        public List<FileEntry> BigFiles;   // ไฟล์ใหญ่ (>= BigFileMin) ในโฟลเดอร์นี้โดยตรง
        public long Size;                  // ขนาดรวมทั้งหมด (รวมโฟลเดอร์ย่อย)
        public long FilesSize;             // ขนาดไฟล์ในโฟลเดอร์นี้โดยตรง
        public int FileCount;              // จำนวนไฟล์ในโฟลเดอร์นี้โดยตรง
        public long TotalFiles;
        public bool AccessDenied;
        public int DeniedInside;

        public string FullPath
        {
            get
            {
                if (Parent == null) return Name;
                string p = Parent.FullPath;
                return p.EndsWith("\\") ? p + Name : p + "\\" + Name;
            }
        }
    }

    class FileEntry
    {
        public string Name;
        public DirNode Dir;
        public long Size;
        public uint Attributes;
        public DateTime Modified;

        public string FullPath
        {
            get
            {
                string p = Dir.FullPath;
                return p.EndsWith("\\") ? p + Name : p + "\\" + Name;
            }
        }
    }

    class OtherFilesTag
    {
        public DirNode Dir;
        public long Size;
        public int Count;
    }

    class ExtStat
    {
        public long Size;
        public long Count;
    }

    // เก็บไฟล์ใหญ่สุด N อันดับ (min-heap)
    class TopFiles
    {
        readonly FileEntry[] h;
        int n;
        long threshold = -1;
        readonly object lk = new object();

        public TopFiles(int cap) { h = new FileEntry[cap]; }

        public long Threshold { get { return Interlocked.Read(ref threshold); } }

        public void Offer(FileEntry f)
        {
            lock (lk)
            {
                if (n < h.Length)
                {
                    h[n] = f;
                    int i = n++;
                    while (i > 0)
                    {
                        int p = (i - 1) / 2;
                        if (h[p].Size <= h[i].Size) break;
                        Swap(i, p); i = p;
                    }
                    if (n == h.Length) Interlocked.Exchange(ref threshold, h[0].Size);
                }
                else if (f.Size > h[0].Size)
                {
                    h[0] = f;
                    int i = 0;
                    while (true)
                    {
                        int l = i * 2 + 1, r = l + 1, m = i;
                        if (l < n && h[l].Size < h[m].Size) m = l;
                        if (r < n && h[r].Size < h[m].Size) m = r;
                        if (m == i) break;
                        Swap(i, m); i = m;
                    }
                    Interlocked.Exchange(ref threshold, h[0].Size);
                }
            }
        }

        void Swap(int a, int b) { FileEntry t = h[a]; h[a] = h[b]; h[b] = t; }

        public List<FileEntry> ToSortedList()
        {
            List<FileEntry> l = new List<FileEntry>();
            for (int i = 0; i < n; i++) l.Add(h[i]);
            l.Sort((a, b) => b.Size.CompareTo(a.Size));
            return l;
        }
    }

    // ------------------------------------------------------------------ scanner

    class Scanner
    {
        public const long BigFileMin = 10L * 1024 * 1024; // 10 MB

        public volatile bool Cancel;
        public long FilesScanned, DirsScanned, BytesScanned, Errors;
        public volatile string CurrentPath = "";
        public readonly TopFiles Top = new TopFiles(1000);
        public readonly ConcurrentDictionary<string, ExtStat> Exts =
            new ConcurrentDictionary<string, ExtStat>(StringComparer.OrdinalIgnoreCase);
        public DirNode Root;

        readonly ParallelOptions popt = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) };

        const uint ATTR_DIRECTORY = 0x10;
        const uint ATTR_REPARSE = 0x400;
        const uint ATTR_OFFLINE = 0x1000;
        const uint ATTR_RECALL_ON_OPEN = 0x40000;
        const uint ATTR_RECALL_ON_DATA = 0x400000;

        public void Run(string rootPath)
        {
            Root = new DirNode { Name = rootPath };
            ScanDir(Root, rootPath, 0);
        }

        void ScanDir(DirNode node, string path, int depth)
        {
            if (Cancel) return;
            CurrentPath = path;

            string search = @"\\?\" + (path.EndsWith("\\") ? path : path + "\\") + "*";
            Native.WIN32_FIND_DATA fd;
            IntPtr h = Native.FindFirstFileExW(search, 1 /*Basic*/, out fd, 0, IntPtr.Zero, 2 /*LargeFetch*/);
            if (h == Native.INVALID_HANDLE)
            {
                int err = Marshal.GetLastWin32Error();
                if (err != 2 && err != 18)
                {
                    node.AccessDenied = true;
                    Interlocked.Increment(ref Errors);
                }
                return;
            }

            long filesSize = 0;
            int fileCount = 0;
            List<DirNode> subs = null;
            try
            {
                do
                {
                    string name = fd.cFileName;
                    if (name == "." || name == "..") continue;
                    uint a = fd.dwFileAttributes;
                    if ((a & ATTR_DIRECTORY) != 0)
                    {
                        // ข้าม junction / symlink เพื่อไม่ให้นับซ้ำหรือวนลูป
                        if ((a & ATTR_REPARSE) != 0) continue;
                        if (subs == null) subs = new List<DirNode>();
                        subs.Add(new DirNode { Name = name, Parent = node });
                    }
                    else
                    {
                        long size = ((long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
                        // ไฟล์ OneDrive แบบ online-only ไม่ได้ใช้พื้นที่จริงในเครื่อง
                        if ((a & (ATTR_OFFLINE | ATTR_RECALL_ON_OPEN | ATTR_RECALL_ON_DATA)) != 0) size = 0;
                        filesSize += size;
                        fileCount++;

                        ExtStat es = Exts.GetOrAdd(GetExt(name), k => new ExtStat());
                        Interlocked.Add(ref es.Size, size);
                        Interlocked.Increment(ref es.Count);

                        bool big = size >= BigFileMin;
                        if (big || size > Top.Threshold)
                        {
                            long ft = ((long)(uint)fd.ftLastWriteTime.dwHighDateTime << 32) | (uint)fd.ftLastWriteTime.dwLowDateTime;
                            DateTime mod;
                            try { mod = DateTime.FromFileTime(ft); } catch { mod = DateTime.MinValue; }
                            FileEntry fe = new FileEntry { Name = name, Dir = node, Size = size, Attributes = a, Modified = mod };
                            if (big)
                            {
                                if (node.BigFiles == null) node.BigFiles = new List<FileEntry>();
                                node.BigFiles.Add(fe);
                            }
                            if (size > Top.Threshold) Top.Offer(fe);
                        }
                    }
                } while (!Cancel && Native.FindNextFileW(h, out fd));
            }
            finally
            {
                Native.FindClose(h);
            }

            node.FilesSize = filesSize;
            node.FileCount = fileCount;
            Interlocked.Add(ref BytesScanned, filesSize);
            Interlocked.Add(ref FilesScanned, fileCount);
            Interlocked.Increment(ref DirsScanned);

            long total = filesSize;
            long tf = fileCount;
            if (subs != null)
            {
                node.Children = subs;
                string basePath = path.EndsWith("\\") ? path : path + "\\";
                if (depth < 4 && subs.Count > 1)
                    Parallel.ForEach(subs, popt, c => ScanDir(c, basePath + c.Name, depth + 1));
                else
                    foreach (DirNode c in subs) ScanDir(c, basePath + c.Name, depth + 1);

                foreach (DirNode c in subs)
                {
                    total += c.Size;
                    tf += c.TotalFiles;
                    node.DeniedInside += c.DeniedInside + (c.AccessDenied ? 1 : 0);
                }
                subs.Sort((x, y) => y.Size.CompareTo(x.Size));
            }
            if (node.BigFiles != null) node.BigFiles.Sort((x, y) => y.Size.CompareTo(x.Size));
            node.Size = total;
            node.TotalFiles = tf;
        }

        static string GetExt(string name)
        {
            int i = name.LastIndexOf('.');
            if (i <= 0 || i == name.Length - 1) return "(ไม่มีนามสกุล)";
            return name.Substring(i).ToLowerInvariant();
        }
    }

    // ------------------------------------------------------------------ safety rules

    enum Level { None, Info, Caution, Danger }

    class Safety
    {
        public Level Level;
        public string Message;

        Safety(Level l, string m) { Level = l; Message = m; }

        static bool Under(string rel, string prefix)
        {
            return rel == prefix || rel.StartsWith(prefix + "\\");
        }

        public static Safety Classify(string path, bool isDir, uint attrs)
        {
            string root = Path.GetPathRoot(path) ?? "";
            string rel = path.Length > root.Length ? path.Substring(root.Length).TrimEnd('\\').ToLowerInvariant() : "";

            if (rel == "")
                return new Safety(Level.None, "ทั้งไดร์ฟ — คลิกที่โฟลเดอร์/ไฟล์ในรายการเพื่อดูคำแนะนำ ดับเบิลคลิกโฟลเดอร์เพื่อดูข้างใน");

            // ---- Windows
            if (Under(rel, "windows\\temp"))
                return new Safety(Level.Info, "ไฟล์ชั่วคราวของ Windows — ล้างได้ด้วย Disk Cleanup หรือ Settings > System > Storage > Temporary files");
            if (Under(rel, "windows\\softwaredistribution\\download"))
                return new Safety(Level.Info, "ไฟล์ดาวน์โหลดของ Windows Update — ล้างได้ด้วย Disk Cleanup > Clean up system files (ไม่ควรลบเองตรงๆ)");
            if (Under(rel, "windows\\winsxs"))
                return new Safety(Level.Danger, "ห้ามลบเด็ดขาด! คลังคอมโพเนนต์ของ Windows (ขนาดที่เห็นอาจเกินจริงเพราะใช้ hard link ร่วมกับ System32) — ลดขนาดได้ด้วย Disk Cleanup > Clean up system files");
            if (Under(rel, "windows\\installer"))
                return new Safety(Level.Danger, "ห้ามลบเด็ดขาด! ไฟล์ติดตั้งของโปรแกรมต่างๆ ลบแล้วจะถอนการติดตั้ง/อัปเดตโปรแกรมไม่ได้");
            if (Under(rel, "windows"))
                return new Safety(Level.Danger, "ห้ามลบเด็ดขาด! โฟลเดอร์ระบบ Windows — ลบแล้วเครื่องอาจเปิดไม่ติด");
            if (Under(rel, "windows.old"))
                return new Safety(Level.Caution, "Windows เวอร์ชันเก่า (หลังอัปเกรด) — ลบได้ผ่าน Disk Cleanup > Previous Windows installation(s) เท่านั้น ห้ามลบเอง");
            if (Under(rel, "$windows.~bt") || Under(rel, "$windows.~ws") || Under(rel, "$winreagent") || Under(rel, "$getcurrent"))
                return new Safety(Level.Caution, "ไฟล์ชั่วคราวสำหรับอัปเกรด Windows — ล้างได้ผ่าน Disk Cleanup เท่านั้น ห้ามลบเอง");

            // ---- ไฟล์ระบบที่ root ของไดร์ฟ
            if (rel == "pagefile.sys" || rel == "swapfile.sys")
                return new Safety(Level.Danger, "ห้ามลบเด็ดขาด! ไฟล์ Virtual Memory — ปรับขนาดได้ที่ System Properties > Advanced > Performance > Virtual memory");
            if (rel == "hiberfil.sys")
                return new Safety(Level.Danger, "ห้ามลบตรงๆ! ไฟล์ Hibernate — ถ้าไม่ใช้ Hibernate/Fast Startup ปิดได้ด้วยคำสั่ง  powercfg /h off  (เปิด Command Prompt แบบ Admin)");
            if (Under(rel, "system volume information"))
                return new Safety(Level.Danger, "ห้ามลบเด็ดขาด! จุดคืนค่าระบบ (Restore Point) — จัดการได้ที่ Control Panel > System > System Protection > Configure");
            if (Under(rel, "$recycle.bin"))
                return new Safety(Level.Info, "ถังขยะ (Recycle Bin) — ล้างได้โดยคลิกขวาที่ Recycle Bin บน Desktop > Empty Recycle Bin");
            if (Under(rel, "recovery") || Under(rel, "boot") || Under(rel, "efi") || rel == "bootmgr" || rel == "bootnxt"
                || Under(rel, "$sysreset") || Under(rel, "config.msi") || Under(rel, "documents and settings") || rel == "dumpstack.log.tmp")
                return new Safety(Level.Danger, "ห้ามลบเด็ดขาด! ไฟล์บูต/กู้คืนระบบของ Windows");

            // ---- โปรแกรม
            if (Under(rel, "program files\\windowsapps"))
                return new Safety(Level.Danger, "ห้ามลบ! แอปจาก Microsoft Store — ถอนการติดตั้งผ่าน Settings > Apps > Installed apps");
            if (Under(rel, "program files") || Under(rel, "program files (x86)"))
                return new Safety(Level.Caution, "โปรแกรมที่ติดตั้งไว้ — ห้ามลบโฟลเดอร์ตรงๆ ถ้าไม่ใช้ให้ Uninstall ผ่าน Settings > Apps > Installed apps");
            if (Under(rel, "programdata"))
                return new Safety(Level.Caution, "ข้อมูลของระบบและโปรแกรม — ห้ามลบถ้าไม่แน่ใจ อาจทำให้โปรแกรม/Windows ทำงานผิดพลาด");

            // ---- Users
            string[] parts = rel.Split('\\');
            if (parts.Length >= 3 && parts[0] == "users")
            {
                string sub = string.Join("\\", parts, 2, parts.Length - 2);
                if (sub.StartsWith("ntuser.") || sub.StartsWith("usrclass."))
                    return new Safety(Level.Danger, "ห้ามลบเด็ดขาด! ไฟล์โปรไฟล์ผู้ใช้ (Registry) ลบแล้วจะ Login ไม่ได้");
                if (Under(sub, "appdata\\local\\temp"))
                    return new Safety(Level.Info, "ไฟล์ชั่วคราว — ลบได้ (ควรปิดโปรแกรมทั้งหมดก่อน ไฟล์ที่กำลังใช้อยู่จะลบไม่ได้ ซึ่งไม่เป็นไร)");
                if (Under(sub, "appdata"))
                    return new Safety(Level.Caution, "ข้อมูลตั้งค่า/แคชของโปรแกรม — ห้ามลบถ้าไม่แน่ใจ (ลบแคชได้เฉพาะโปรแกรมที่รู้จัก และควรล้างผ่านเมนูของโปรแกรมนั้น)");
                if (Under(sub, "downloads"))
                    return new Safety(Level.Info, "โฟลเดอร์ Downloads — มักมีไฟล์เก่าที่ไม่ใช้แล้ว ตรวจดูก่อนแล้วลบได้");
            }

            if (!isDir && (attrs & 0x4) != 0)
                return new Safety(Level.Danger, "ไฟล์ระบบ (มี System attribute) — ห้ามลบ");

            return new Safety(Level.None, "ไม่ใช่ไฟล์ระบบที่รู้จัก — ตรวจสอบให้แน่ใจก่อนลบเสมอ และลบแบบลงถังขยะ (Recycle Bin) เผื่อกู้คืน");
        }

        public static string Tag(Level l)
        {
            switch (l)
            {
                case Level.Danger: return "  [ห้ามลบ]";
                case Level.Caution: return "  [ระวัง]";
                case Level.Info: return "  [ล้างได้]";
                default: return "";
            }
        }

        public static Color Fore(Level l)
        {
            switch (l)
            {
                case Level.Danger: return Color.FromArgb(200, 0, 0);
                case Level.Caution: return Color.FromArgb(190, 100, 0);
                case Level.Info: return Color.FromArgb(0, 128, 60);
                default: return SystemColors.WindowText;
            }
        }

        public static Color Back(Level l)
        {
            switch (l)
            {
                case Level.Danger: return Color.FromArgb(255, 220, 220);
                case Level.Caution: return Color.FromArgb(255, 236, 200);
                case Level.Info: return Color.FromArgb(215, 245, 220);
                default: return Color.FromArgb(232, 238, 248);
            }
        }

        public static string Word(Level l)
        {
            switch (l)
            {
                case Level.Danger: return "ห้ามลบ";
                case Level.Caution: return "ระวัง";
                case Level.Info: return "ล้างได้";
                default: return "-";
            }
        }
    }

    // ------------------------------------------------------------------ native

    static class Native
    {
        public static readonly IntPtr INVALID_HANDLE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindFirstFileExW(string lpFileName, int fInfoLevelId, out WIN32_FIND_DATA lpFindFileData,
            int fSearchOp, IntPtr lpSearchFilter, int dwAdditionalFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll")]
        public static extern bool FindClose(IntPtr hFindFile);

        public static bool IsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        // ---- SeBackupPrivilege: ให้ Admin อ่านรายชื่อไฟล์ในโฟลเดอร์ที่ถูกล็อกสิทธิ์ได้ (อ่านอย่างเดียว)
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public long Luid;
            public uint Attributes;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out long lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState,
            int BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        public static void EnableBackupPrivilege()
        {
            IntPtr tok;
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x20 | 0x8, out tok)) return;
            try
            {
                long luid;
                if (!LookupPrivilegeValue(null, "SeBackupPrivilege", out luid)) return;
                TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = 2 };
                AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally { CloseHandle(tok); }
        }
    }
}
