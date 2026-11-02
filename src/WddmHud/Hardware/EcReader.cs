// =============================================================================
// Read-only sample of the Compal embedded controller through the driver that
// Copyright (C) 2026 Joseph Rocco Peransi
// www.github.com/pascalkeeper
// is already loaded (EMSC, \\.\DosCPLEMSC). Command 0x80 only.
// Never sends EC write 0x81, never touches port 0xB2, never opens a fan-set
// method. The only address read here is DSDT field CTMP at 0xB0.
// =============================================================================

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WddmHud.Hardware
{
    internal sealed class EcReader : IDisposable
    {
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint OpenExisting = 3;
        private const uint IoctlRead = 0x222004;
        private const uint IoctlWrite = 0x222008;
        private const byte ReadCommand = 0x80;
        private const byte StatusPort = 0x66;
        private const byte DataPort = 0x62;

        /// <summary>DSDT ECMM field CTMP. CPU temperature the EC fan logic uses.</summary>
        private const byte CpuTempAddress = 0xB0;

        private SafeFileHandle? _handle;

        public bool TryOpen(out string error)
        {
            error = "";
            _handle = CreateFileW(@"\\.\DosCPLEMSC", GenericRead | GenericWrite, 0, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (_handle.IsInvalid)
            {
                error = "CreateFile " + Marshal.GetLastWin32Error();
                _handle.Dispose();
                _handle = null;
                return false;
            }
            return true;
        }

        /// <summary>Returns Celsius when the byte is a live temperature. Null otherwise.</summary>
        public int? ReadCpuTempC()
        {
            int value = ReadByte(CpuTempAddress);
            if (value <= 0 || value >= 125)
                return null;
            return value;
        }

        public void Dispose()
        {
            _handle?.Dispose();
            _handle = null;
        }

        private int ReadByte(byte address)
        {
            if (_handle == null || _handle.IsInvalid)
                throw new InvalidOperationException("EC device is closed");

            WaitIbfClear();
            Outb(ReadCommand, StatusPort);
            WaitIbfClear();
            Outb(address, DataPort);
            WaitObf();
            return Inb(DataPort);
        }

        private void WaitIbfClear()
        {
            for (int i = 0; i < 4000; i++)
            {
                if ((Inb(StatusPort) & 0x02) == 0)
                    return;
            }
            throw new TimeoutException("EC status bit IBF stayed set");
        }

        private void WaitObf()
        {
            for (int i = 0; i < 4000; i++)
            {
                if ((Inb(StatusPort) & 0x01) != 0)
                    return;
            }
            throw new TimeoutException("EC data port stayed empty");
        }

        private byte Inb(byte port)
        {
            return Ioctl(IoctlRead, 0x00, port);
        }

        private void Outb(byte value, byte port)
        {
            Ioctl(IoctlWrite, value, port);
        }

        private byte Ioctl(uint code, byte b0, byte b1)
        {
            byte[] buf = new byte[] { b0, b1 };
            uint returned = 0;
            bool ok = DeviceIoControl(_handle!, code, buf, 2, buf, 2, ref returned, IntPtr.Zero);
            if (!ok)
                throw new IOException("EC ioctl " + code.ToString("X") + " failed (" + Marshal.GetLastWin32Error() + ")");
            return buf[0];
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFileW(
            string fileName, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle device, uint code, byte[] inBuffer, uint inSize, byte[] outBuffer, uint outSize, ref uint returned, IntPtr overlapped);
    }
}
