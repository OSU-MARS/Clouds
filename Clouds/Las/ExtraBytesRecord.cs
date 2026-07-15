using System;
using System.IO;

namespace Mars.Clouds.Las
{
    public class ExtraBytesRecord : VariableLengthRecord
    {
        public const UInt16 LasfSpecRecordID = 4;
        public const int DescriptorLengthInBytes = 192;

        public ExtraBytes[] Descriptors { get; private init; }

        public ExtraBytesRecord(UInt16 reserved, string description, ReadOnlySpan<byte> descriptorBytes)
            : base(reserved, LasFile.LasfSpec, ExtraBytesRecord.LasfSpecRecordID, (UInt16)descriptorBytes.Length, description)
        {
            int descriptors = descriptorBytes.Length / ExtraBytesRecord.DescriptorLengthInBytes;
            if ((descriptorBytes.Length - ExtraBytesRecord.DescriptorLengthInBytes * descriptors) != 0)
            {
                throw new InvalidDataException($"Extra bytes record contains {descriptorBytes.Length} descriptor bytes, which is not an integer multiple of the {ExtraBytesRecord.DescriptorLengthInBytes} byte descriptor length.");
            }

            this.Descriptors = new ExtraBytes[descriptors];
            for (int descriptorIndex = 0; descriptorIndex < descriptors; ++descriptorIndex)
            {
                this.Descriptors[descriptorIndex] = new(descriptorBytes.Slice(descriptorIndex * ExtraBytesRecord.DescriptorLengthInBytes, ExtraBytesRecord.DescriptorLengthInBytes));
            }
        }

        public override int GetSizeInBytes()
        {
            return VariableLengthRecord.HeaderSizeInBytes + ExtraBytesRecord.DescriptorLengthInBytes;
        }

        public override void Write(Stream stream)
        {
            this.WriteHeader(stream);

            Span<byte> descriptorBytes = stackalloc byte[ExtraBytesRecord.DescriptorLengthInBytes];
            for (int descriptorIndex = 0; descriptorIndex < this.Descriptors.Length; ++descriptorIndex)
            {
                this.Descriptors[descriptorIndex].Write(descriptorBytes);
                stream.Write(descriptorBytes);
            }
        }
    }
}
