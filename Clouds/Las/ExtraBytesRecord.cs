using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Mars.Clouds.Las
{
    public class ExtraBytesRecord : VariableLengthRecord
    {
        public const UInt16 LasfSpecRecordID = 4;
        public const int SizeInBytes = 192;

        public byte[] ReservedExtra { get; set; }
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
        public string DescriptionExtra { get; set; }

        public ExtraBytesRecord(UInt16 reserved, string description, ReadOnlySpan<byte> vlrBytes)
            : base(reserved, LasFile.LasfSpec, ExtraBytesRecord.LasfSpecRecordID, ExtraBytesRecord.SizeInBytes, description)
        {
            if (vlrBytes.Length != ExtraBytesRecord.SizeInBytes)
            {
                throw new InvalidDataException("Extra bytes record was " + vlrBytes.Length + " bytes long instead of " + ExtraBytesRecord.SizeInBytes + ".");
            }

            this.ReservedExtra = [ vlrBytes[0], vlrBytes[1] ];
            this.DataType = (ExtraBytesDataType)vlrBytes[2];
            this.Options = (ExtraBytesOptions)vlrBytes[3];
            this.Name = Encoding.UTF8.GetString(vlrBytes[4..32]).Trim('\0');
            this.Unused = [ vlrBytes[36], vlrBytes[37], vlrBytes[38], vlrBytes[39] ];
            this.NoData = [ vlrBytes[40], vlrBytes[41], vlrBytes[42], vlrBytes[43], vlrBytes[44], vlrBytes[45], vlrBytes[46], vlrBytes[47] ];
            this.Deprecated1 = [ vlrBytes[48], vlrBytes[49], vlrBytes[50], vlrBytes[51], vlrBytes[52], vlrBytes[53], vlrBytes[54], vlrBytes[55],
                                 vlrBytes[56], vlrBytes[57], vlrBytes[58], vlrBytes[59], vlrBytes[60], vlrBytes[61], vlrBytes[62], vlrBytes[63] ];
            this.Min = [ vlrBytes[64], vlrBytes[65], vlrBytes[66], vlrBytes[67], vlrBytes[68], vlrBytes[69], vlrBytes[70], vlrBytes[71] ];
            this.Deprecated2 = [ vlrBytes[72], vlrBytes[73], vlrBytes[74], vlrBytes[75], vlrBytes[76], vlrBytes[77], vlrBytes[78], vlrBytes[79],
                                 vlrBytes[80], vlrBytes[81], vlrBytes[82], vlrBytes[83], vlrBytes[84], vlrBytes[85], vlrBytes[86], vlrBytes[87] ];
            this.Max = [ vlrBytes[88], vlrBytes[89], vlrBytes[90], vlrBytes[91], vlrBytes[92], vlrBytes[93], vlrBytes[94], vlrBytes[95] ];
            this.Deprecated3 = [  vlrBytes[96],  vlrBytes[97],  vlrBytes[98],  vlrBytes[99], vlrBytes[100], vlrBytes[101], vlrBytes[102], vlrBytes[103],
                                 vlrBytes[104], vlrBytes[105], vlrBytes[106], vlrBytes[107], vlrBytes[108], vlrBytes[109], vlrBytes[110], vlrBytes[111] ];
            this.Scale = BinaryPrimitives.ReadDoubleLittleEndian(vlrBytes[112..]);
            this.Deprecated4 = [ vlrBytes[120], vlrBytes[121], vlrBytes[122], vlrBytes[123], vlrBytes[124], vlrBytes[125], vlrBytes[126], vlrBytes[127],
                                 vlrBytes[128], vlrBytes[129], vlrBytes[130], vlrBytes[131], vlrBytes[132], vlrBytes[133], vlrBytes[134], vlrBytes[135] ];
            this.Deprecated5 = [ vlrBytes[136], vlrBytes[137], vlrBytes[138], vlrBytes[139], vlrBytes[140], vlrBytes[141], vlrBytes[142], vlrBytes[143],
                                 vlrBytes[144], vlrBytes[145], vlrBytes[146], vlrBytes[147], vlrBytes[148], vlrBytes[149], vlrBytes[150], vlrBytes[151] ];
            this.Offset = BinaryPrimitives.ReadDoubleLittleEndian(vlrBytes[152..]);
            this.DescriptionExtra = Encoding.UTF8.GetString(vlrBytes[160..]).Trim('\0');
        }

        public override void Write(Stream stream)
        {
            this.WriteHeader(stream);
            Span<byte> vlrBytes = stackalloc byte[ExtraBytesRecord.SizeInBytes];

            this.ReservedExtra.CopyTo(vlrBytes);
            vlrBytes[2] = (byte)this.DataType;
            vlrBytes[3] = (byte)this.Options;
            LasWriter.WriteNullTerminated(vlrBytes[4..], this.Name, 32);
            this.Unused.CopyTo(vlrBytes[36..]);
            this.NoData.CopyTo(vlrBytes[40..]);
            this.Deprecated1.CopyTo(vlrBytes[48..]);
            this.Min.CopyTo(vlrBytes[64..]);
            this.Deprecated2.CopyTo(vlrBytes[72..]);
            this.Max.CopyTo(vlrBytes[88..]);
            this.Deprecated3.CopyTo(vlrBytes[96..]);
            this.Max.CopyTo(vlrBytes[88..]);
            this.Deprecated3.CopyTo(vlrBytes[96..]);
            BinaryPrimitives.WriteDoubleLittleEndian(vlrBytes[112..], this.Scale);
            this.Deprecated4.CopyTo(vlrBytes[120..]);
            this.Deprecated5.CopyTo(vlrBytes[136..]);
            BinaryPrimitives.WriteDoubleLittleEndian(vlrBytes[152..], this.Offset);
            LasWriter.WriteNullTerminated(vlrBytes[160..], this.DescriptionExtra, 32);

            stream.Write(vlrBytes);
        }
    }
}
