using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace CyphalSharp
{
    /// <summary>
    /// Represents a single data field within a Cyphal message, supporting bit-level alignment.
    /// </summary>
    public class Field
    {
        /// <summary>The DSDL type name of the field.</summary>
        public string Type { get; set; }
        /// <summary>The name of the field.</summary>
        public string Name { get; set; }
        /// <summary>The raw text content from the DSDL definition for this field.</summary>
        public string TagBody { get; set; }

        #region Helpers
        /// <summary>The resolved .NET data type for this field.</summary>
        public Type DataType { get; private set; }
        
        /// <summary>Length of the field in bits.</summary>
        public int BitLength { get; set; }
        
        /// <summary>Position of the field in the payload in bits.</summary>
        public int BitOffset { get; internal set; }

        /// <summary>Total length in bytes (rounded up if not byte-aligned).</summary>
        public int Length => (BitLength + 7) / 8;

        /// <summary>
        /// For union types: the tag value that activates this field.
        /// </summary>
        public int UnionTagValue { get; set; }

        /// <summary>
        /// True if this field is part of a union and is not the tag field itself.
        /// </summary>
        public bool IsUnionVariant { get; set; }

        /// <summary>The number of elements if the field is an array.</summary>
        public int ArrayLength { get; private set; } = 0;
        /// <summary>True if the field is an array type.</summary>
        public bool IsArray => ArrayLength > 0;
        /// <summary>The .NET type of the individual elements (if an array) or the field itself.</summary>
        public Type ElementType => DataType.IsArray ? DataType.GetElementType() : DataType;

        /// <summary>Sets the .NET data type based on the DSDL type name.</summary>
        public void SetDataType()
        {
            // Simple parsing for MVP
            string baseType = Type;
            if (Type.Contains("["))
            {
                baseType = Type.Substring(0, Type.IndexOf("["));
            }

            DataType = baseType switch
            {
                "uint64_t" => typeof(ulong),
                "uint32_t" => typeof(uint),
                "uint16_t" => typeof(ushort),
                "uint8_t"  => typeof(byte),
                "int64_t"  => typeof(long),
                "int32_t"  => typeof(int),
                "int16_t"  => typeof(short),
                "int8_t"   => typeof(sbyte),
                "float"    => typeof(float),
                "double"   => typeof(double),
                "bool"     => typeof(bool),
                _ => typeof(byte) // Fallback
            };

            if (Type.Contains("[")) DataType = DataType.MakeArrayType();
        }

        internal void SetBitLength()
        {
            // Extract bit length from types like 'uint7' or 'int32'
            // For MVP, we assume the DsdlParser has already mapped them to internal types
            // but we need to know the actual bit width.
            
            int elementBits = 0;
            string t = Type.ToLower();

            if (t.Contains("uint64")) elementBits = 64;
            else if (t.Contains("uint32")) elementBits = 32;
            else if (t.Contains("uint16")) elementBits = 16;
            else if (t.Contains("uint8"))  elementBits = 8;
            else if (t.Contains("int64"))  elementBits = 64;
            else if (t.Contains("int32"))  elementBits = 32;
            else if (t.Contains("int16"))  elementBits = 16;
            else if (t.Contains("int8"))   elementBits = 8;
            else if (t.Contains("float"))  elementBits = 32;
            else if (t.Contains("double")) elementBits = 64;
            else if (t.Contains("bool"))   elementBits = 1;
            else if (t.StartsWith("void")) 
            {
                // Handle voidN
                int.TryParse(t.Replace("void", ""), out elementBits);
            }

            if (Type.Contains("["))
            {
                var start = Type.IndexOf("[") + 1;
                var end = Type.IndexOf("]");
                int.TryParse(Type.Substring(start, end - start), out var len);
                ArrayLength = len;
                BitLength = elementBits * ArrayLength;
            }
            else
            {
                BitLength = elementBits;
            }
        }

        internal object GetValue(ReadOnlySpan<byte> payload)
        {
            if (DataType.IsArray)
            {
                var values = Array.CreateInstance(ElementType, ArrayLength);
                int elementBits = BitLength / ArrayLength;
                for (int i = 0; i < ArrayLength; i++)
                {
                    values.SetValue(ReadSingleValue(payload, BitOffset + (i * elementBits), elementBits), i);
                }
                return values;
            }

            return ReadSingleValue(payload, BitOffset, BitLength);
        }

        private object ReadSingleValue(ReadOnlySpan<byte> payload, int offset, int bits)
        {
            if (ElementType == typeof(bool)) return BitHelpers.ReadBits(payload, offset, 1) != 0;
            if (ElementType == typeof(byte)) return (byte)BitHelpers.ReadBits(payload, offset, bits);
            if (ElementType == typeof(ushort)) return (ushort)BitHelpers.ReadBits(payload, offset, bits);
            if (ElementType == typeof(uint)) return (uint)BitHelpers.ReadBits(payload, offset, bits);
            if (ElementType == typeof(ulong)) return BitHelpers.ReadBits(payload, offset, bits);
            
            if (ElementType == typeof(sbyte)) return (sbyte)BitHelpers.ReadBitsSigned(payload, offset, bits);
            if (ElementType == typeof(short)) return (short)BitHelpers.ReadBitsSigned(payload, offset, bits);
            if (ElementType == typeof(int)) return (int)BitHelpers.ReadBitsSigned(payload, offset, bits);
            if (ElementType == typeof(long)) return BitHelpers.ReadBitsSigned(payload, offset, bits);

            if (ElementType == typeof(float)) return BitHelpers.Int32BitsToSingle((int)BitHelpers.ReadBits(payload, offset, 32));
            if (ElementType == typeof(double)) return BitHelpers.Int64BitsToDouble((long)BitHelpers.ReadBits(payload, offset, 64));

            return null;
        }

        internal void SetValue(Span<byte> payload, object value)
        {
            if (DataType.IsArray)
            {
                var array = (Array)value;
                int elementBits = BitLength / ArrayLength;
                for (int i = 0; i < ArrayLength; i++)
                {
                    var val = i < array.Length ? array.GetValue(i) : 0;
                    WriteSingleValue(payload, BitOffset + (i * elementBits), elementBits, val);
                }
            }
            else
            {
                WriteSingleValue(payload, BitOffset, BitLength, value);
            }
        }

        private void WriteSingleValue(Span<byte> payload, int offset, int bits, object value)
        {
            ulong uval = 0;
            if (value is bool b) uval = b ? 1UL : 0UL;
            else if (value is byte u8) uval = u8;
            else if (value is ushort u16) uval = u16;
            else if (value is uint u32) uval = u32;
            else if (value is ulong u64) uval = u64;
            else if (value is sbyte i8) uval = (ulong)i8;
            else if (value is short i16) uval = (ulong)i16;
            else if (value is int i32) uval = (ulong)i32;
            else if (value is long i64) uval = (ulong)i64;
            else if (value is float f) uval = (ulong)BitHelpers.SingleToInt32Bits(f);
            else if (value is double d) uval = (ulong)BitHelpers.DoubleToInt64Bits(d);

            BitHelpers.WriteBits(payload, offset, bits, uval);
        }
        #endregion
    }
}
