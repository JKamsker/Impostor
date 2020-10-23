using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace Impostor.Api
{
    /// <summary>
    /// Priovides a StreamReader-like api throught extensions
    /// </summary>
    public static class SpanReaderExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ReadByte(this ref ReadOnlySpan<byte> input)
        {
            var original = Advance<byte>(ref input);
            return original[0];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadInt32(this ref ReadOnlySpan<byte> input)
        {
            var original = Advance<int>(ref input);
            return BinaryPrimitives.ReadInt32LittleEndian(original);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadUInt32(this ref ReadOnlySpan<byte> input)
        {
            var original = Advance<uint>(ref input);
            return BinaryPrimitives.ReadUInt32LittleEndian(original);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ReadSingle(this ref ReadOnlySpan<byte> input)
        {
            var original = Advance<float>(ref input);

            // BitConverter.Int32BitsToSingle
            // Doesn't exist in net 2.0 for some reason
            return Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(original));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ReadBoolean(this ref ReadOnlySpan<byte> input)
        {
            return input.ReadByte() != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe float Int32BitsToSingle(int value)
        {
            return *((float*)&value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadPackedInt32(this ref ReadOnlySpan<byte> input)
        {
            return (int)ReadPackedUInt32(ref input);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadPackedUInt32(this ref ReadOnlySpan<byte> input)
        {
            bool readMore = true;
            int shift = 0;
            uint output = 0;

            while (readMore)
            {
                byte b = input[0];
                input = input.Slice(1);

                if (b >= 128)
                {
                    b ^= 0x80;
                }
                else
                {
                    readMore = false;
                }

                output |= (uint)(b << shift);
                shift += 7;
            }

            return output;
        }

        public static string ReadString(this ref ReadOnlySpan<byte> input)
        {
            var len = input.ReadPackedInt32();
            var stringWindow = input.Slice(0, len);
            input = input.Slice(len);

            var output = Encoding.UTF8.GetString(stringWindow);
            return output;
        }

        /// <summary>
        /// Advances the position of <see cref="input"/> by the size of <see cref="T"/>.
        /// </summary>
        /// <typeparam name="T">Type that will be read.</typeparam>
        /// <param name="input">input "stream"/span.</param>
        /// <returns>The original input</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe ReadOnlySpan<byte> Advance<T>(ref ReadOnlySpan<byte> input)
            where T : unmanaged
        {
            var original = input;
            input = input.Slice(sizeof(T));
            return original;
        }
    }
}