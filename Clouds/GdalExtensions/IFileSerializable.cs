namespace Mars.Clouds.GdalExtensions
{
    public interface IFileSerializable<TFile> where TFile : IFileSerializable<TFile>
    {
        static abstract TFile Read(string filePath);
    }
}
