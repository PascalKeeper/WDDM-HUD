// =============================================================================
// Read-only Dell SMM fan speed.
// Official signed PawnIO module DellSMM.bin 0.2.11 (LGPL-2.1, namazso/PawnIO.Modules).
// The module runs the call on CPU 0 and refuses to load unless the BIOS
// answers the Dell signature. This class sends command 0x02A3 only.
// =============================================================================

using System.Runtime.InteropServices;
using System.Text;
using WddmHud.Core;

namespace WddmHud.Hardware
{
    public sealed class DellSmmReader : IDisposable
    {
        private const long GetSpeed = 0x02A3;
        private const uint IoctlLoad = (41394u << 16) | (0x821u << 2);
        private const uint IoctlExec = (41394u << 16) | (0x841u << 2);
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        private IntPtr _handle = InvalidHandle;
        // BIOS fan indexes that answered get-speed. This machine uses 0 and 2.
        private readonly int[] _biosIndex = { -1, -1 };

        public bool IsOpen => _handle != InvalidHandle && _handle != IntPtr.Zero;

        public bool TryOpen(out string error)
        {
            error = "";
            if (IsOpen)
                return true;

            _handle = CreateFileW(
                @"\\?\GLOBALROOT\Device\PawnIO",
                0x80000000u | 0x40000000u,
                0x3,
                IntPtr.Zero,
                3,
                0x80,
                IntPtr.Zero);
            if (_handle == InvalidHandle || _handle == IntPtr.Zero)
            {
                _handle = InvalidHandle;
                error = "PawnIO device did not open (" + Marshal.GetLastWin32Error() + ").";
                return false;
            }

            byte[] module = ReadModule();
            if (module.Length == 0)
            {
                error = "DellSMM module is missing from the build.";
                Close();
                return false;
            }

            if (!DeviceIoControl(_handle, IoctlLoad, module, module.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            {
                error = "Dell SMM signature was rejected (" + Marshal.GetLastWin32Error() + ").";
                Close();
                return false;
            }

            int slot = 0;
            for (int fan = 0; fan < 4; fan++)
            {
                if (!TryRead(fan, out _))
                    continue;
                if (slot < _biosIndex.Length)
                    _biosIndex[slot] = fan;
                slot++;
            }
            Logger.Info("Dell SMM open. bios0=" + _biosIndex[0] + " bios1=" + _biosIndex[1]);
            return true;
        }

        public void Read(out int? fan0, out int? fan1)
        {
            fan0 = ReadBios(_biosIndex[0]);
            fan1 = ReadBios(_biosIndex[1]);
        }

        private int? ReadBios(int fan)
        {
            if (fan < 0 || !TryRead(fan, out int rpm))
                return null;
            return rpm;
        }

        public void Dispose() => Close();

        private void Close()
        {
            if (IsOpen)
                CloseHandle(_handle);
            _handle = InvalidHandle;
        }

        private bool TryRead(int fan, out int rpm)
        {
            rpm = 0;
            if (fan is < 0 or > 3 || !IsOpen)
                return false;

            byte[] packet = new byte[32 + 48];
            Encoding.ASCII.GetBytes("ioctl_query_smm").CopyTo(packet, 0);
            BitConverter.GetBytes(GetSpeed).CopyTo(packet, 32);
            BitConverter.GetBytes((long)fan).CopyTo(packet, 32 + 24);
            if (BitConverter.ToInt64(packet, 32) != GetSpeed)
                return false;
            if (BitConverter.ToInt64(packet, 32 + 24) != fan)
                return false;

            byte[] output = new byte[48];
            if (!DeviceIoControl(_handle, IoctlExec, packet, packet.Length, output, output.Length, out int returned, IntPtr.Zero))
                return false;
            if (returned < 8)
                return false;

            long eax = BitConverter.ToInt64(output, 0) & 0xFFFFFFFF;
            int low = (int)(eax & 0xFFFF);
            if (eax == GetSpeed || low == 0xFFFF || low > 20000)
                return false;
            rpm = low;
            return true;
        }

        private static byte[] ReadModule()
        {
            using Stream? stream = typeof(DellSmmReader).Assembly.GetManifestResourceStream("WddmHud.Hardware.DellSMM.bin");
            if (stream == null)
                return Array.Empty<byte>();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(
            string fileName, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr handle, uint code, byte[] input, int inputSize,
            IntPtr output, int outputSize, out int returned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr handle, uint code, byte[] input, int inputSize,
            byte[] output, int outputSize, out int returned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
