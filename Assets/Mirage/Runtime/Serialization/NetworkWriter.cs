/*
MIT License

Copyright (c) 2021 James Frowen

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Mirage.Serialization
{
    public static class BitMask
    {
        /// <summary>
        /// Creates mask for <paramref name="bits"/>
        /// <para>
        /// (showing 32 bits for simplify, result is 64 bit)
        /// <br/>
        /// Example bits = 4 => mask = 00000000_00000000_00000000_00001111
        /// <br/>
        /// Example bits = 10 => mask = 00000000_00000000_00000011_11111111
        /// </para>
        /// </summary>
        /// <param name="bits"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Mask(int bits)
        {
            return bits == 0 ? 0 : ulong.MaxValue >> (64 - bits);
        }
    }
    /// <summary>
    /// Binary stream Writer. Supports simple types, buffers, arrays, structs, and nested types
    /// <para>Use <see cref="NetworkWriterPool.GetWriter">NetworkWriter.GetWriter</see> to reduce memory allocation</para>
    /// </summary>
    public unsafe class NetworkWriter
    {
        byte[] managedBuffer;
        GCHandle handle;
        ulong* longPtr;
        int bitCapacity;
        bool disposed;

        int bitPosition;


        public int ByteLength
        {
            // rounds up to nearest 8
            // add to 3 last bits,
            //   if any are 1 then it will roll over 4th bit.
            //   if all are 0, then nothing happens 
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (bitPosition + 0b111) >> 3;
        }

        public int BitPosition
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => bitPosition;
        }

        public NetworkWriter(int minByteCapacity)
        {
            int ulongCapacity = Mathf.CeilToInt(minByteCapacity / (float)sizeof(ulong));
            int byteCapacity = ulongCapacity * sizeof(ulong);
            bitCapacity = byteCapacity * 8;
            managedBuffer = new byte[byteCapacity];
            handle = GCHandle.Alloc(managedBuffer, GCHandleType.Pinned);
            longPtr = (ulong*)handle.AddrOfPinnedObject();
        }
        ~NetworkWriter()
        {
            freeHandle();
        }
        /// <summary>
        /// Frees the handle for the buffer
        /// <para>In order for <see cref="PooledNetworkWriter"/> to work This class can not have <see cref="IDisposable"/>. Instead we call this method from Finalze</para>
        /// </summary>
        void freeHandle()
        {
            if (disposed) return;

            handle.Free();
            longPtr = null;
            disposed = true;
        }

        public void Reset()
        {
            bitPosition = 0;
        }

        /// <summary>
        /// Copies internal buffer to new Array
        /// </summary>
        /// <returns></returns>
        public byte[] ToArray()
        {
            byte[] data = new byte[ByteLength];
            // todo benchmark and optimize (can we copy from ptr faster
            Buffer.BlockCopy(managedBuffer, 0, data, 0, ByteLength);
            return data;
        }
        public ArraySegment<byte> ToArraySegment()
        {
            // todo clear extra bits in byte (dont want last byte to have useless data)
            return new ArraySegment<byte>(managedBuffer, 0, ByteLength);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void checkNewLength(int newLength)
        {
            if (newLength > bitCapacity)
            {
                throw new IndexOutOfRangeException();
            }
        }

        private void padToByte()
        {
            bitPosition = ByteLength << 3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBoolean(bool value)
        {
            WriteBoolean(value ? 1UL : 0UL);
        }
        /// <summary>
        /// Writes first bit of <paramref name="value"/> to buffer
        /// </summary>
        /// <param name="value"></param>
        public void WriteBoolean(ulong value)
        {
            int newPosition = bitPosition + 1;
            checkNewLength(newPosition);

            int bitsInLong = bitPosition & 0b11_1111;

            ulong* ptr = longPtr + (bitPosition >> 6);
            *ptr = (
                *ptr & (
                    // start with 0000_0001
                    // shift by number in bit, eg 5 => 0010_0000
                    // then not 1101_1111
                    ~(1UL << bitsInLong)
                )
            ) | ((value & 0b1) << bitsInLong);

            bitPosition = newPosition;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteSByte(sbyte value) => WriteByte((byte)value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value) => writerUnmasked(value, 8);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt16(short value) => WriteUInt16((ushort)value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt16(ushort value) => writerUnmasked(value, 16);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt32(int value) => WriteUInt32((uint)value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt32(uint value) => writerUnmasked(value, 32);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt64(long value) => WriteUInt64((ulong)value);
        public void WriteUInt64(ulong value)
        {
            int newPosition = bitPosition + 64;
            checkNewLength(newPosition);

            int bitsInLong = bitPosition & 0b11_1111;

            if (bitsInLong == 0)
            {
                ulong* ptr1 = longPtr + (bitPosition >> 6);
                *ptr1 = value;
            }
            else
            {
                int bitsLeft = 64 - bitsInLong;

                ulong* ptr1 = longPtr + (bitPosition >> 6);
                ulong* ptr2 = ptr1 + 1;

                *ptr1 = (*ptr1 & (ulong.MaxValue >> bitsLeft)) | (value << bitsInLong);
                *ptr2 = (*ptr2 & (ulong.MaxValue << newPosition)) | (value >> bitsLeft);

            }
            bitPosition = newPosition;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteSingle(float value) => WriteUInt32(*(uint*)&value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteDouble(double value) => WriteUInt64(*(ulong*)&value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(ulong value, int bits)
        {
            if (bits == 0) return;
            // mask so we dont overwrite
            writerUnmasked(value & BitMask.Mask(bits), bits);
        }
        private void writerUnmasked(ulong value, int bits)
        {
            int newPosition = bitPosition + bits;
            checkNewLength(newPosition);

            int bitsInLong = bitPosition & 0b11_1111;
            int bitsLeft = 64 - bitsInLong;

            if (bitsLeft >= bits)
            {
                ulong* ptr = longPtr + (bitPosition >> 6);

                // todo benchmark and optimize this new mask
                ulong mask1 = bitsInLong == 0 ? 0ul : (ulong.MaxValue >> bitsLeft);
                ulong mask2 = (newPosition & 0b11_1111) == 0 ? 0ul : (ulong.MaxValue << newPosition /*we can use full position here as c# will mask it to just 6 bits*/);
                ulong mask = mask1 | mask2;

                // old mask, doesn't work when bitposition before/after is multiple of 64
                //ulong mask = (ulong.MaxValue >> bitsLeft) | (ulong.MaxValue << newPosition /*we can use full position here as c# will mask it to just 6 bits*/);

                *ptr = (*ptr & mask) | (value << bitsInLong);
            }
            else
            {
                ulong* ptr1 = longPtr + (bitPosition >> 6);
                ulong* ptr2 = ptr1 + 1;

                *ptr1 = (*ptr1 & (ulong.MaxValue >> bitsLeft)) | (value << bitsInLong);
                *ptr2 = (*ptr2 & (ulong.MaxValue << newPosition)) | (value >> bitsLeft);
            }
            bitPosition = newPosition;
        }

        public void WriteAtBytePosition(ulong value, int bits, int bytePosition)
        {
            WriteAtPosition(value, bits, bytePosition * 8);
        }
        public void WriteAtPosition(ulong value, int bits, int bitPosition)
        {
            // careful with this method, dont set bitPosition

            int newPosition = bitPosition + bits;
            checkNewLength(newPosition);

            // mask so we dont overwrite
            value &= ulong.MaxValue >> (64 - bits);

            int bitsInLong = bitPosition & 0b11_1111;
            int bitsLeft = 64 - bitsInLong;
            if (bitsLeft >= bits)
            {
                ulong* ptr = longPtr + (bitPosition >> 6);
                *ptr = (
                    *ptr & (
                        (ulong.MaxValue >> bitsLeft) | (ulong.MaxValue << (newPosition /*we can use full position here as c# will mask it to just 6 bits*/))
                    )
                ) | (value << bitsInLong);
            }
            else
            {
                ulong* ptr1 = longPtr + (bitPosition >> 6);
                ulong* ptr2 = ptr1 + 1;

                *ptr1 = (*ptr1 & (ulong.MaxValue >> bitsLeft)) | (value << bitsInLong);
                *ptr2 = (*ptr2 & (ulong.MaxValue << newPosition)) | (value >> bitsLeft);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="valuePtr"></param>
        /// <param name="count">How many ulongs to copy, eg 64 bits</param>
        public void UnsafeCopy(ulong* valuePtr, int count)
        {
            if (count == 0) { return; }

            int newBit = bitPosition + (64 * count);
            checkNewLength(newBit);


            ulong* startPtr = longPtr + (bitPosition >> 6);
            for (int i = 0; i < count; i++)
            {
                WriteUInt64(startPtr[i]);
            }

            //int bitsInLong = bitPosition & 0b11_1111;
            //int bitsLeft = 64 - bitsInLong;

            //// write first part to end of current ulong
            //*startPtr = (*startPtr & (ulong.MaxValue >> bitsLeft)) | (*valuePtr << bitsInLong);

            //// write middle parts to single ulong
            //for (int i = 1; i < count; i++)
            //{
            //    *(startPtr + i) = (*(valuePtr + i - 1) >> (64 - bitsInLong)) | (*(valuePtr + i) << bitsInLong);
            //}

            //// write end part to start of next ulong
            //*(startPtr + count) = (*(startPtr + count) & (ulong.MaxValue << bitPosition)) | (*(valuePtr + count - 1) >> bitsLeft);

            Debug.Assert(bitPosition == newBit, "bitPosition Shoudl already be equal to newBit because it would have incremented each WriteUInt64");

            bitPosition = newBit;
        }

        /// <summary>
        /// <para>
        ///    Moves poition to nearest byte then copies struct to that position
        /// </para>
        /// See <see href="https://docs.unity3d.com/ScriptReference/Unity.Collections.LowLevel.Unsafe.UnsafeUtility.CopyStructureToPtr.html">UnsafeUtility.CopyStructureToPtr</see>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <param name="byteSize">size of stuct, in bytes</param>
        public void PadAndCopy<T>(ref T value, int byteSize) where T : struct
        {
            padToByte();
            int newPosition = bitPosition + (8 * byteSize);
            checkNewLength(newPosition);

            byte* startPtr = ((byte*)longPtr) + (bitPosition >> 3);

            UnsafeUtility.CopyStructureToPtr(ref value, startPtr);
            bitPosition = newPosition;
        }

        /// <summary>
        /// <para>
        ///    Moves position to nearest byte then writes bytes to that position
        /// </para>
        /// </summary>
        /// <param name="array"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        public void WriteBytes(byte[] array, int offset, int length)
        {
            padToByte();
            int newPosition = bitPosition + (8 * length);
            checkNewLength(newPosition);

            // todo benchmark this vs Marshal.Copy or for loop
            Buffer.BlockCopy(array, offset, managedBuffer, ByteLength, length);
            bitPosition = newPosition;
        }

        public void CopyFromWriter(NetworkWriter other, int otherBitPosition, int bitLength)
        {
            int newBit = bitPosition + bitLength;
            checkNewLength(newBit);

            int ulongPos = otherBitPosition >> 6;
            ulong* otherPtr = other.longPtr + ulongPos;


            int firstBitOffset = otherBitPosition & 0b11_1111;

            // first align other
            if (firstBitOffset != 0)
            {
                int bitsToCopyFromFirst = Math.Min(64 - firstBitOffset, bitLength);

                // if offset is 10, then we want to shift value by 10 to remove un-needed bits
                ulong firstValue = *otherPtr >> firstBitOffset;

                Write(firstValue, bitsToCopyFromFirst);

                bitLength -= bitsToCopyFromFirst;
                otherPtr++;
            }

            // write aligned with other
            while (bitLength > 64)
            {
                WriteUInt64(*otherPtr);

                bitLength -= 64;
                otherPtr++;
            }

            // write left over others
            //      if bitlength == 0 then write will return
            Write(*otherPtr, bitLength);

            Debug.Assert(bitPosition == newBit, "bitPosition Shoudl already be equal to newBit because it would have incremented each WriteUInt64");
            bitPosition = newBit;
        }
    }
}
