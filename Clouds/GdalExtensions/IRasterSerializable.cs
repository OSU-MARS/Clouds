namespace Mars.Clouds.GdalExtensions
{
    public interface IRasterSerializable<TFile> where TFile : IRasterSerializable<TFile>
    {
        static abstract TFile Read(string filePath, bool readData);
    }
}
