﻿
using System;
using System.Security;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Runtime.ConstrainedExecution;
using System.Runtime.Versioning;
using System.Text;
using TinyWall.Interface;
using System.ComponentModel;

namespace PKSoft
{
    public class ProcessManager
    {
        public sealed class SafeProcessHandle : SafeHandleZeroOrMinusOneIsInvalid   // OpenProcess returns 0 on failure
        {
            internal SafeProcessHandle() : base(true) { }

            internal SafeProcessHandle(IntPtr handle) : base(true)
            {
                SetHandle(handle);
            }

            [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
            override protected bool ReleaseHandle()
            {
                return SafeNativeMethods.CloseHandle(handle);
            }
        }

        public sealed class SafeSnapshotHandle : SafeHandleMinusOneIsInvalid   // CreateToolhelp32Snapshot  returns -1 on failure
        {
            internal SafeSnapshotHandle() : base(true) { }

            internal SafeSnapshotHandle(IntPtr handle) : base(true)
            {
                SetHandle(handle);
            }

            [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
            override protected bool ReleaseHandle()
            {
                return SafeNativeMethods.CloseHandle(handle);
            }
        }

        [SuppressUnmanagedCodeSecurity]
        public static class SafeNativeMethods
        {
            [DllImport("kernel32", SetLastError = true)]
            internal static extern SafeProcessHandle OpenProcess(ProcessAccessFlags dwDesiredAccess, bool bInheritHandle, int dwProcessId);

            [DllImport("kernel32", SetLastError = true)]
            [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
            internal static extern bool CloseHandle(IntPtr hHandle);

            [DllImport("kernel32", SetLastError = true)]
            internal static extern bool QueryFullProcessImageName(SafeProcessHandle hProcess, QueryFullProcessImageNameFlags dwFlags, StringBuilder lpExeName, out int size);

            [DllImport("ntdll")]
            internal static extern int NtQueryInformationProcess(SafeProcessHandle hProcess, int processInformationClass, [Out] out PROCESS_BASIC_INFORMATION processInformation, int processInformationLength, out int returnLength);

            [DllImport("kernel32", SetLastError = true)]
            internal static extern SafeSnapshotHandle CreateToolhelp32Snapshot(SnapshotFlags flags, int id);
            [DllImport("kernel32", SetLastError = true)]
            internal static extern bool Process32First(SafeSnapshotHandle hSnapshot, ref PROCESSENTRY32 lppe);
            [DllImport("kernel32", SetLastError = true)]
            internal static extern bool Process32Next(SafeSnapshotHandle hSnapshot, ref PROCESSENTRY32 lppe);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool PostThreadMessage(int threadId, uint msg, UIntPtr wParam, IntPtr lParam);

            [DllImport("kernel32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetProcessTimes(SafeProcessHandle hProcess, out long lpCreationTime, out long lpExitTime, out long lpKernelTime, out long lpUserTime);
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PROCESS_BASIC_INFORMATION
        {
            // Fore more info, see docs for NtQueryInformationProcess()
            internal IntPtr Reserved1;
            internal IntPtr PebBaseAddress;
            internal IntPtr Reserved2_0;
            internal IntPtr Reserved2_1;
            internal IntPtr UniqueProcessId;
            internal IntPtr InheritedFromUniqueProcessId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public int th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public int th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        };

        public struct ExtendedProcessEntry
        {
            public PROCESSENTRY32 BaseEntry;
            public long CreationTime;
            public string ImagePath;
        };

        [Flags]
        internal enum SnapshotFlags : uint
        {
            HeapList = 0x00000001,
            Process = 0x00000002,
            Thread = 0x00000004,
            Module = 0x00000008,
            Module32 = 0x00000010,
            All = (HeapList | Process | Thread | Module),
            Inherit = 0x80000000,
            NoHeaps = 0x40000000
        }

        [Flags]
        public enum ProcessAccessFlags
        {
            All = 0x001F0FFF,
            Terminate = 0x00000001,
            CreateThread = 0x00000002,
            VMOperation = 0x00000008,
            VMRead = 0x00000010,
            VMWrite = 0x00000020,
            DupHandle = 0x00000040,
            SetInformation = 0x00000200,
            QueryInformation = 0x00000400,
            Synchronize = 0x00100000,
            ReadControl = 0x00020000,
            QueryLimitedInformation = 0x00001000,
        }

        [Flags]
        public enum QueryFullProcessImageNameFlags
        {
            Win32Format = 0,
            NativeFormat = 1
        }

        public static string ExecutablePath { get; } = GetCurrentExecutablePath();
        private static string GetCurrentExecutablePath()
        {
            using (var proc = Process.GetCurrentProcess())
            {
                return ProcessManager.GetProcessPath(proc.Id);
            }
        }
        public static string GetProcessPath(int processId)
        {
            var buffer = new StringBuilder(1024);
            return GetProcessPath(processId, buffer);
        }

        public static string GetProcessPath(int processId, StringBuilder buffer)
        {
            using (SafeProcessHandle hProcess = SafeNativeMethods.OpenProcess(ProcessAccessFlags.QueryLimitedInformation, false, processId))
            {
                return GetProcessPath(hProcess, buffer);
            }
        }

        public static string GetProcessPath(SafeProcessHandle hProcess, StringBuilder buffer)
        {
            // This method needs Windows Vista or newer OS
            System.Diagnostics.Debug.Assert(Environment.OSVersion.Version.Major >= 6);

            buffer.Length = 0;

            if (hProcess.IsInvalid)
                return null;

            int size = buffer.Capacity;
            if (SafeNativeMethods.QueryFullProcessImageName(hProcess, QueryFullProcessImageNameFlags.Win32Format, buffer, out size))
                return buffer.ToString();
            else
                return null;
        }

        public static bool GetParentProcess(int processId, ref int parentPid)
        {
            using (SafeProcessHandle hProcess = SafeNativeMethods.OpenProcess(ProcessAccessFlags.QueryLimitedInformation, false, processId))
            {
                if (hProcess.IsInvalid)
                    return false;
                    //throw new Exception($"Cannot open process Id {processId}.");

                if (VersionInfo.IsWow64Process)
                {
                    return false;
                    //throw new NotSupportedException("This method is not supported in 32-bit process on a 64-bit OS.");
                }
                else
                {
                    PROCESS_BASIC_INFORMATION pbi = new PROCESS_BASIC_INFORMATION();
                    int status = SafeNativeMethods.NtQueryInformationProcess(hProcess, 0, out pbi, Marshal.SizeOf(pbi), out int returnLength);
                    if (status < 0)
                        throw new Exception($"NTSTATUS: {status}");

                    parentPid = pbi.InheritedFromUniqueProcessId.ToInt32();

                    // parentPid might have been reused and thus might not be the actual parent.
                    // Check process creation times to figure it out.
                    using (SafeProcessHandle hParentProcess = SafeNativeMethods.OpenProcess(ProcessAccessFlags.QueryLimitedInformation, false, parentPid))
                    {
                        if (GetProcessCreationTime(hParentProcess, out long parentCreation) && GetProcessCreationTime(hProcess, out long childCreation))
                        {
                            return parentCreation <= childCreation;
                        }
                        return false;
                    }
                }
            }
        }

        private static bool GetProcessCreationTime(SafeProcessHandle hProcess, out long creationTime)
        {
            long dummy1, dummy2, dummy3;
            return SafeNativeMethods.GetProcessTimes(hProcess, out creationTime, out dummy1, out dummy2, out dummy3);
        }

        public static IEnumerable<PROCESSENTRY32> CreateToolhelp32Snapshot()
        {
            const int ERROR_NO_MORE_FILES = 0x12;

            PROCESSENTRY32 pe32 = new PROCESSENTRY32 { };
            pe32.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
            using (var hSnapshot = SafeNativeMethods.CreateToolhelp32Snapshot(SnapshotFlags.Process, 0))
            {
                if (hSnapshot.IsInvalid)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                if (!SafeNativeMethods.Process32First(hSnapshot, ref pe32))
                {
                    int errno = Marshal.GetLastWin32Error();
                    if (errno == ERROR_NO_MORE_FILES)
                        yield break;
                    throw new Win32Exception(errno);
                }
                do
                {
                    yield return pe32;
                } while (SafeNativeMethods.Process32Next(hSnapshot, ref pe32));
            }
        }

        public static IEnumerable<ExtendedProcessEntry> CreateToolhelp32SnapshotExtended()
        {
            StringBuilder sbuilder = new StringBuilder(1024);
            foreach (var p in CreateToolhelp32Snapshot())
            {
                using (SafeProcessHandle hProcess = SafeNativeMethods.OpenProcess(ProcessAccessFlags.QueryLimitedInformation, false, p.th32ProcessID))
                {
                    ExtendedProcessEntry ret;
                    ret.BaseEntry = p;
                    ret.ImagePath = GetProcessPath(hProcess, sbuilder);
                    GetProcessCreationTime(hProcess, out ret.CreationTime);
                    yield return ret;
                }
            }
        }

        public static void TerminateProcess(Process p, int timeoutMs)
        {
            if (p.MainWindowHandle == IntPtr.Zero)
            {
                foreach (ProcessThread thread in p.Threads)
                {
                    const uint WM_QUIT = 0x0012;
                    SafeNativeMethods.PostThreadMessage(thread.Id, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
                }
            }
            else
            {
                p.CloseMainWindow();
            }
            if (!p.WaitForExit(timeoutMs))
            {
                p.Kill();
                p.WaitForExit(1000);
            }
        }
    }
}
