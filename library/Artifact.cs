namespace TeamCityConfigBuilder.Library
{
    public class Artifact
    {
        public Artifact(string name, string relativePath, OutputClass outputClass)
        {
            Name = name;
            RelativePath = relativePath;
            OutputClass = outputClass;
        }
        public string Name { get; private set; }
        public string RelativePath { get; private set; }
        public OutputClass OutputClass { get; private set; }
    }
}