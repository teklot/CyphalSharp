using System;

namespace CyphalSharp
{
    /// <summary>
    /// Provides helper methods for bit-level manipulations required by Cyphal DSDL packing.
    /// </summary>
    public static class BitHelpers
    {
        /// <summary>Converts a single-precision floating-point value to its 32-bit integer representation.</summary>
        public static unsafe int SingleToInt32Bits(float value) => *(int*)&value;
        /// <summary>Converts a 32-bit integer representation to its single-precision floating-point value.</summary>
        public static unsafe float Int32BitsToSingle(int value) => *(float*)&value;
        /// <summary>Converts a double-precision floating-point value to its 64-bit integer representation.</summary>
        public static unsafe long DoubleToInt64Bits(double value) => *(long*)&value;
        /// <summary>Converts a 64-bit integer representation to its double-precision floating-point value.</summary>
        public static unsafe double Int64BitsToDouble(long value) => *(double*)&value;

        /// <summary>
        /// Reads an unsigned integer of arbitrary bit length from a buffer.
        /// </summary>
        public static ulong ReadBits(ReadOnlySpan<byte> buffer, int bitOffset, int bitLength)
        {
            ulong value = 0;
            for (int i = 0; i < bitLength; i++)
            {
                int currentBit = bitOffset + i;
                int byteIdx = currentBit / 8;
                int bitIdx = currentBit % 8;

                if ((buffer[byteIdx] & (1 << bitIdx)) != 0)
                {
                    value |= (1UL << i);
                }
            }
            return value;
        }

        /// <summary>
        /// Writes an unsigned integer of arbitrary bit length to a buffer.
        /// </summary>
        public static void WriteBits(Span<byte> buffer, int bitOffset, int bitLength, ulong value)
        {
            for (int i = 0; i < bitLength; i++)
            {
                int currentBit = bitOffset + i;
                int byteIdx = currentBit / 8;
                int bitIdx = currentBit % 8;

                if ((value & (1UL << i)) != 0)
                {
                    buffer[byteIdx] |= (byte)(1 << bitIdx);
                }
                else
                {
                    buffer[byteIdx] &= (byte)~(1 << bitIdx);
                }
            }
        }

        /// <summary>
        /// Reads a signed integer of arbitrary bit length.
        /// </summary>
        public static long ReadBitsSigned(ReadOnlySpan<byte> buffer, int bitOffset, int bitLength)
        {
            ulong uval = ReadBits(buffer, bitOffset, bitLength);
            if (bitLength < 64 && (uval & (1UL << (bitLength - 1))) != 0)
            {
                // Apply sign extension
                return (long)(uval | (ulong.MaxValue << bitLength));
            }
            return (long)uval;
        }
    }
}
