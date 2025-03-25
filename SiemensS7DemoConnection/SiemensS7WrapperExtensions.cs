using S7.Net;
using System;
using System.Text;
using System.Threading.Tasks;

namespace SiemensS7DemoConnection
{
    // Extension methods for SiemensS7Wrapper
    public static class SiemensS7WrapperExtensions
    {
        /// <summary>
        /// Reads a Siemens S7 string from a DB address
        /// </summary>
        /// <param name="wrapper">The wrapper instance</param>
        /// <param name="dbNumber">DB number</param>
        /// <param name="startByteAddress">Start byte address of the string</param>
        /// <param name="maxLength">Maximum length of the string (if known, otherwise defaults to 254)</param>
        /// <returns>The read string value</returns>
        public static async Task<string> ReadS7StringAsync(this SiemensS7Wrapper wrapper, int dbNumber, int startByteAddress, int maxLength = 254)
        {
            // Read the actual length byte first (second byte in S7 string)
            var actualLength = await wrapper.ReadAsync<byte>($"DB{dbNumber}.DBB{startByteAddress + 1}");

            // Ensure we don't exceed the maximum length
            actualLength = (byte)Math.Min(actualLength, maxLength);

            if (actualLength == 0)
                return string.Empty;

            // Read the string content (starting from the third byte)
            var bytes = await wrapper.ReadBytesAsync(DataType.DataBlock, dbNumber, startByteAddress + 2, actualLength);

            // Convert bytes to string
            return Encoding.ASCII.GetString(bytes);
        }

        /// <summary>
        /// Writes a Siemens S7 string to a DB address
        /// </summary>
        /// <param name="wrapper">The wrapper instance</param>
        /// <param name="dbNumber">DB number</param>
        /// <param name="startByteAddress">Start byte address of the string</param>
        /// <param name="value">String value to write</param>
        /// <param name="maxLength">Maximum length of the string (if known, otherwise defaults to 254)</param>
        /// <returns>True if successful</returns>
        public static async Task<bool> WriteS7StringAsync(this SiemensS7Wrapper wrapper, int dbNumber, int startByteAddress, string value, int maxLength = 254)
        {
            if (value == null)
                value = string.Empty;

            // Ensure we don't exceed maximum length
            byte actualLength = (byte)Math.Min(value.Length, maxLength);

            // Write the maximum length byte
            await wrapper.WriteAsync($"DB{dbNumber}.DBB{startByteAddress}", (byte)maxLength);

            // Write the actual length byte
            await wrapper.WriteAsync($"DB{dbNumber}.DBB{startByteAddress + 1}", actualLength);

            if (actualLength == 0)
                return true;

            // Convert string to bytes
            byte[] bytes = Encoding.ASCII.GetBytes(value.Substring(0, actualLength));

            // Write the string content
            await wrapper.WriteBytesAsync(DataType.DataBlock, dbNumber, startByteAddress + 2, bytes);

            return true;
        }

        /// <summary>
        /// Reads a DateTime value from a DB address
        /// </summary>
        /// <param name="wrapper">The wrapper instance</param>
        /// <param name="dbNumber">DB number</param>
        /// <param name="startByteAddress">Start byte address of the DateTime</param>
        /// <returns>The read DateTime value</returns>
        public static async Task<DateTime> ReadDateTimeAsync(this SiemensS7Wrapper wrapper, int dbNumber, int startByteAddress)
        {
            // S7 DateTime values are 8 bytes long
            var bytes = await wrapper.ReadBytesAsync(DataType.DataBlock, dbNumber, startByteAddress, 8);

            // Year is stored in bytes 0-1
            int year = bytes[0] * 256 + bytes[1];

            // Month is stored in byte 2
            int month = bytes[2];

            // Day is stored in byte 3
            int day = bytes[3];

            // Hour is stored in byte 4
            int hour = bytes[4];

            // Minute is stored in byte 5
            int minute = bytes[5];

            // Second is stored in byte 6
            int second = bytes[6];

            // Millisecond is stored in byte 7 (0-999)
            int millisecond = bytes[7] * 100;

            return new DateTime(year, month, day, hour, minute, second, millisecond);
        }

        /// <summary>
        /// Writes a DateTime value to a DB address
        /// </summary>
        /// <param name="wrapper">The wrapper instance</param>
        /// <param name="dbNumber">DB number</param>
        /// <param name="startByteAddress">Start byte address of the DateTime</param>
        /// <param name="value">DateTime value to write</param>
        /// <returns>True if successful</returns>
        public static async Task<bool> WriteDateTimeAsync(this SiemensS7Wrapper wrapper, int dbNumber, int startByteAddress, DateTime value)
        {
            byte[] bytes = new byte[8];

            // Year is stored in bytes 0-1
            bytes[0] = (byte)(value.Year / 256);
            bytes[1] = (byte)(value.Year % 256);

            // Month is stored in byte 2
            bytes[2] = (byte)value.Month;

            // Day is stored in byte 3
            bytes[3] = (byte)value.Day;

            // Hour is stored in byte 4
            bytes[4] = (byte)value.Hour;

            // Minute is stored in byte 5
            bytes[5] = (byte)value.Minute;

            // Second is stored in byte 6
            bytes[6] = (byte)value.Second;

            // Millisecond is stored in byte 7 (0-999)
            bytes[7] = (byte)(value.Millisecond / 100);

            // Write the bytes
            await wrapper.WriteBytesAsync(DataType.DataBlock, dbNumber, startByteAddress, bytes);

            return true;
        }

        /// <summary>
        /// Helper method to read bytes from the PLC
        /// </summary>
        private static async Task<byte[]> ReadBytesAsync(this SiemensS7Wrapper wrapper, DataType dataType, int dbNumber, int startByteAddress, int count)
        {
            byte[] result = new byte[count];
            for (int i = 0; i < count; i++)
            {
                var byteValue = await wrapper.ReadAsync<byte>($"DB{dbNumber}.DBB{startByteAddress + i}");
                result[i] = byteValue;
            }
            return result;
        }

        /// <summary>
        /// Helper method to write bytes to the PLC
        /// </summary>
        private static async Task WriteBytesAsync(this SiemensS7Wrapper wrapper, DataType dataType, int dbNumber, int startByteAddress, byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                await wrapper.WriteAsync($"DB{dbNumber}.DBB{startByteAddress + i}", bytes[i]);
            }
        }
    }
}