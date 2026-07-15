using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Mars.Clouds.Las
{
    // TODO: ExtraBytes<T> where T is obtained from ExtraBytesDataType
    public class ExtraBytes
    {
        public byte[] Reserved { get; set; }
        public ExtraBytesDataType DataType { get; set; }
        public ExtraBytesOptions Options { get; set; }
        public string Name { get; set; } // 32 bytes
        public byte[] Unused { get; set; }
        public byte[] NoData { get; set; }
        public byte[] Deprecated1 { get; set; }
        public byte[] Min { get; set; }
        public byte[] Deprecated2 { get; set; }
        public byte[] Max { get; set; }
        public byte[] Deprecated3 { get; set; }
        public double Scale { get; set; }
        public byte[] Deprecated4 { get; set; }
        public double Offset { get; set; }
        public byte[] Deprecated5 { get; set; }
        public string Description { get; set; }

        public ExtraBytes(ReadOnlySpan<byte> descriptorBytes)
        {
            Debug.Assert(descriptorBytes.Length == ExtraBytesRecord.DescriptorLengthInBytes, $"Descriptor bytes length is {descriptorBytes.Length} ratherthan the required {ExtraBytesRecord.DescriptorLengthInBytes} bytes.");

            this.Reserved = [ descriptorBytes[0], descriptorBytes[1] ];
            this.DataType = (ExtraBytesDataType)descriptorBytes[2];
            this.Options = (ExtraBytesOptions)descriptorBytes[3];
            this.Name = Encoding.UTF8.GetString(descriptorBytes[4..32]).Trim('\0');
            this.Unused = [ descriptorBytes[36], descriptorBytes[37], descriptorBytes[38], descriptorBytes[39] ];
            this.NoData = [ descriptorBytes[40], descriptorBytes[41], descriptorBytes[42], descriptorBytes[43], descriptorBytes[44], descriptorBytes[45], descriptorBytes[46], descriptorBytes[47] ];
            this.Deprecated1 = [ descriptorBytes[48], descriptorBytes[49], descriptorBytes[50], descriptorBytes[51], descriptorBytes[52], descriptorBytes[53], descriptorBytes[54], descriptorBytes[55],
                                     descriptorBytes[56], descriptorBytes[57], descriptorBytes[58], descriptorBytes[59], descriptorBytes[60], descriptorBytes[61], descriptorBytes[62], descriptorBytes[63] ];
            this.Min = [descriptorBytes[64], descriptorBytes[65], descriptorBytes[66], descriptorBytes[67], descriptorBytes[68], descriptorBytes[69], descriptorBytes[70], descriptorBytes[71]];
            this.Deprecated2 = [ descriptorBytes[72], descriptorBytes[73], descriptorBytes[74], descriptorBytes[75], descriptorBytes[76], descriptorBytes[77], descriptorBytes[78], descriptorBytes[79],
                                     descriptorBytes[80], descriptorBytes[81], descriptorBytes[82], descriptorBytes[83], descriptorBytes[84], descriptorBytes[85], descriptorBytes[86], descriptorBytes[87] ];
            this.Max = [descriptorBytes[88], descriptorBytes[89], descriptorBytes[90], descriptorBytes[91], descriptorBytes[92], descriptorBytes[93], descriptorBytes[94], descriptorBytes[95]];
            this.Deprecated3 = [ descriptorBytes[96],  descriptorBytes[97],  descriptorBytes[98],  descriptorBytes[99], descriptorBytes[100], descriptorBytes[101], descriptorBytes[102], descriptorBytes[103],
                                     descriptorBytes[104], descriptorBytes[105], descriptorBytes[106], descriptorBytes[107], descriptorBytes[108], descriptorBytes[109], descriptorBytes[110], descriptorBytes[111] ];
            this.Scale = BinaryPrimitives.ReadDoubleLittleEndian(descriptorBytes[112..120]);
            this.Deprecated4 = [ descriptorBytes[120], descriptorBytes[121], descriptorBytes[122], descriptorBytes[123], descriptorBytes[124], descriptorBytes[125], descriptorBytes[126], descriptorBytes[127],
                                 descriptorBytes[128], descriptorBytes[129], descriptorBytes[130], descriptorBytes[131], descriptorBytes[132], descriptorBytes[133], descriptorBytes[134], descriptorBytes[135] ];
            this.Deprecated5 = [ descriptorBytes[136], descriptorBytes[137], descriptorBytes[138], descriptorBytes[139], descriptorBytes[140], descriptorBytes[141], descriptorBytes[142], descriptorBytes[143],
                                     descriptorBytes[144], descriptorBytes[145], descriptorBytes[146], descriptorBytes[147], descriptorBytes[148], descriptorBytes[149], descriptorBytes[150], descriptorBytes[151] ];
            this.Offset = BinaryPrimitives.ReadDoubleLittleEndian(descriptorBytes[152..160]);
            this.Description = Encoding.UTF8.GetString(descriptorBytes[160..192]).Trim('\0');
        }

        public void Write(Span<byte> descriptorBytes)
        {
            Debug.Assert(descriptorBytes.Length == ExtraBytesRecord.DescriptorLengthInBytes, $"Descriptor bytes length is {descriptorBytes.Length} ratherthan the required {ExtraBytesRecord.DescriptorLengthInBytes} bytes.");

            this.Reserved.CopyTo(descriptorBytes[0..2]);
            descriptorBytes[2] = (byte)this.DataType;
            descriptorBytes[3] = (byte)this.Options;
            LasWriter.WriteNullTerminated(descriptorBytes[4..36], this.Name, 32);
            this.Unused.CopyTo(descriptorBytes[36..40]);
            this.NoData.CopyTo(descriptorBytes[40..48]);
            this.Deprecated1.CopyTo(descriptorBytes[48..64]);
            this.Min.CopyTo(descriptorBytes[64..72]);
            this.Deprecated2.CopyTo(descriptorBytes[72..88]);
            this.Max.CopyTo(descriptorBytes[88..96]);
            this.Deprecated3.CopyTo(descriptorBytes[96..112]);
            BinaryPrimitives.WriteDoubleLittleEndian(descriptorBytes[112..120], this.Scale);
            this.Deprecated4.CopyTo(descriptorBytes[120..136]);
            this.Deprecated5.CopyTo(descriptorBytes[136..152]);
            BinaryPrimitives.WriteDoubleLittleEndian(descriptorBytes[152..160], this.Offset);
            LasWriter.WriteNullTerminated(descriptorBytes[160..192], this.Description, 32);
        }
    }
}
