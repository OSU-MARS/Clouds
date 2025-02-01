using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mars.Clouds.Cmdlets.Hardware
{
    [SupportedOSPlatform("windows")]
    internal partial class NativeMethodsWindows
    {
        // https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getlogicalprocessorinformation
        // https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-system_logical_processor_information
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool GetLogicalProcessorInformation(SYSTEM_LOGICAL_PROCESSOR_INFORMATION* buffer, ref UInt32 bufferSize);

        public static unsafe int GetPhysicalCoreCount()
        {
            UInt32 bufferSizeInBytes = 0;
            NativeMethodsWindows.GetLogicalProcessorInformation(null, ref bufferSizeInBytes);
            int processorInformationFields = (int)(bufferSizeInBytes / sizeof(SYSTEM_LOGICAL_PROCESSOR_INFORMATION));

            int physicalCores = 0;
            SYSTEM_LOGICAL_PROCESSOR_INFORMATION[] logicalProcessorInfo = new SYSTEM_LOGICAL_PROCESSOR_INFORMATION[processorInformationFields];
            fixed (SYSTEM_LOGICAL_PROCESSOR_INFORMATION* pLogicalProcessorInfo = logicalProcessorInfo)
            {
                if (NativeMethodsWindows.GetLogicalProcessorInformation(pLogicalProcessorInfo, ref bufferSizeInBytes) == false)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }

            for (int logicalProcessorIndex = 0; logicalProcessorIndex < processorInformationFields; ++logicalProcessorIndex)
            {
                SYSTEM_LOGICAL_PROCESSOR_INFORMATION info = logicalProcessorInfo[logicalProcessorIndex];
                if (info.Relationship == LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore)
                {
                    ++physicalCores;
                }
            }

            return physicalCores;
        }

        // https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-cache_descriptor
        [StructLayout(LayoutKind.Sequential)]
        private struct CACHE_DESCRIPTOR
        {
            public byte Level;
            public byte Associativity;
            public ushort LineSize;
            public uint Size;
            public uint Type;
        }

        // https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-logical_processor_relationship
        private enum LOGICAL_PROCESSOR_RELATIONSHIP
        {
            RelationProcessorCore = 0,
            RelationNumaNode = 1,
            RelationCache = 2,
            RelationProcessorPackage = 3,
            RelationGroup,
            RelationProcessorDie,
            RelationNumaNodeEx,
            RelationAll = 0xffff
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION
        {
            public UIntPtr ProcessorMask;
            public LOGICAL_PROCESSOR_RELATIONSHIP Relationship;
            public SYSTEM_LOGICAL_PROCESSOR_INFORMATION_UNION ProcessorInformation;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_UNION
        {
            [FieldOffset(0)] 
            public byte ProcessorCore;
            [FieldOffset(0)] 
            public UInt32 NumaNode;
            [FieldOffset(0)] 
            public CACHE_DESCRIPTOR Cache;
            [FieldOffset(0)] 
            private readonly UInt64 Reserved0;
            [FieldOffset(8)] 
            private readonly UInt64 Reserved1;
        }
    }
}
