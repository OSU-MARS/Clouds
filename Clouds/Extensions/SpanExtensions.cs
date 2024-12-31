using System;
using System.Diagnostics;

namespace Mars.Clouds.Extensions
{
    public static class SpanExtensions
    {
        public static void SortRadix256(Span<Int32> spanToSortInPlace, Span<Int32> workingBuffer)
        {
            Debug.Assert(workingBuffer.Length >= spanToSortInPlace.Length);

            // counting sort on least significant byte
            Span<int> count = stackalloc int[256];
            count.Clear();
            for (int index = 0; index < spanToSortInPlace.Length; ++index)
            {
                int value = spanToSortInPlace[index];
                if (value < 0)
                {
                    throw new NotSupportedException("Currently only non-negative integers are supported. Span contains negative value " + value + ".");
                }
                ++count[value & 0x000000ff];
            }

            Span<int> dataIndexByBin = stackalloc int[256];
            dataIndexByBin[0] = 0;
            for (int binIndex = 1; binIndex < dataIndexByBin.Length; ++binIndex)
            {
                dataIndexByBin[binIndex] = dataIndexByBin[binIndex - 1] + count[binIndex - 1];
            }

            for (int index = 0; index < spanToSortInPlace.Length; ++index)
            {
                workingBuffer[dataIndexByBin[spanToSortInPlace[index] & 0x000000ff]++] = spanToSortInPlace[index];
            }

            // counting sort on second byte
            count.Clear();
            for (int index = 0; index < spanToSortInPlace.Length; ++index)
            {
                ++count[(workingBuffer[index] & 0x0000ff00) >> 8];
            }

            dataIndexByBin[0] = 0;
            for (int binIndex = 1; binIndex < dataIndexByBin.Length; ++binIndex)
            {
                dataIndexByBin[binIndex] = dataIndexByBin[binIndex - 1] + count[binIndex - 1];
            }

            for (int index = 0; index < spanToSortInPlace.Length; ++index)
            {
                spanToSortInPlace[dataIndexByBin[(workingBuffer[index] & 0x0000ff00) >> 8]++] = workingBuffer[index];
            }

            // counting sort on third byte
            count.Clear();
            for (int index = 0; index < spanToSortInPlace.Length; ++index)
            {
                ++count[spanToSortInPlace[index] & 0x00ff0000];
            }

            dataIndexByBin[0] = 0;
            for (int binIndex = 1; binIndex < dataIndexByBin.Length; ++binIndex)
            {
                dataIndexByBin[binIndex] = dataIndexByBin[binIndex - 1] + count[binIndex - 1];
            }

            for (int index = 0; index < spanToSortInPlace.Length; ++index)
            {
                workingBuffer[dataIndexByBin[spanToSortInPlace[index] & 0x00ff0000]++] = spanToSortInPlace[index];
            }

            // counting sort on fourth byte
            count.Clear();
            for (int index = 0; index < spanToSortInPlace.Length; ++index)
            {
                ++count[unchecked((Int32)(((UInt32)workingBuffer[index] & 0xff000000) >> 24))];
            }

            dataIndexByBin[0] = 0;
            for (int binIndex = 1; binIndex < dataIndexByBin.Length; ++binIndex)
            {
                dataIndexByBin[binIndex] = dataIndexByBin[binIndex - 1] + count[binIndex - 1];
            }

            for (int index = 0; index < spanToSortInPlace.Length; ++index)
            {
                // TODO: reorder bins to support negative values https://stackoverflow.com/questions/15306665/radix-sort-for-negative-integers
                spanToSortInPlace[dataIndexByBin[unchecked((Int32)(((UInt32)workingBuffer[index] & 0xff000000) >> 8))]++] = workingBuffer[index];
            }
        }

        public static void SortRadix256(Span<UInt16> spanToSortInPlace, Span<UInt16> workingBuffer)
        {
            Debug.Assert(workingBuffer.Length >= spanToSortInPlace.Length);

            // counting sort on least significant byte
            Span<int> count = stackalloc int[256];
            count.Clear();
            for (int index = 0; index < spanToSortInPlace.Length; ++index)
            {
                ++count[spanToSortInPlace[index] & 0x00ff];
            }

            Span<int> dataIndexByBin = stackalloc int[256];
            dataIndexByBin[0] = 0;
            for (int binIndex = 1; binIndex < dataIndexByBin.Length; ++binIndex)
            {
                dataIndexByBin[binIndex] = dataIndexByBin[binIndex - 1] + count[binIndex - 1]; // initialize to start of bin
            }

            for (int index = 0; index < spanToSortInPlace.Length; ++index)
            {
                workingBuffer[dataIndexByBin[spanToSortInPlace[index] & 0x00ff]++] = spanToSortInPlace[index];
            }

            // counting sort on most significant byte
            count.Clear();
            for (int index = 0; index < spanToSortInPlace.Length; ++index)
            {
                ++count[(workingBuffer[index] & 0xff00) >> 8];
            }

            dataIndexByBin[0] = 0;
            for (int binIndex = 1; binIndex < dataIndexByBin.Length; ++binIndex)
            {
                dataIndexByBin[binIndex] = dataIndexByBin[binIndex - 1] + count[binIndex - 1];
            }

            for (int index = 0; index < spanToSortInPlace.Length; ++index)
            {
                spanToSortInPlace[dataIndexByBin[(workingBuffer[index] & 0xff00) >> 8]++] = workingBuffer[index];
            }
        }
    }
}
