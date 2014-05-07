using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TeamCityConfigBuilder.Shell
{
    public class BuildProject
    {
        public BuildProject(string solutionPath)
        {
            Name = Path.GetFileNameWithoutExtension(solutionPath);
            SolutionDirectory = Path.GetDirectoryName(solutionPath);
            Branch = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(solutionPath)));
            Solution = new Solution(solutionPath);
            Artifacts = new List<Artifact>();
            foreach (var x in Solution.Projects.Where(x => x.OutputClass != OutputClass.ClassLibrary && !string.IsNullOrWhiteSpace(x.OutputPath)))
            {
                switch (x.OutputClass)
                {
                    case OutputClass.NServiceBus:
                    case OutputClass.WindowsService:
                        Artifacts.Add(new Artifact(x.ProjectName, Path.Combine("Source", Path.GetDirectoryName(x.RelativePath), x.OutputPath), x.OutputClass));
                        break;
                    //case OutputClass.WebDeploy:
                    //    break;
                }
            }
            //Artifacts.AddRange(Solution.Projects.Where(x => x.OutputClass != OutputClass.ClassLibrary && !string.IsNullOrWhiteSpace(x.OutputPath)).Select(x => new Artifact(x.ProjectName, Path.Combine(Path.GetDirectoryName(x.RelativePath), x.OutputPath), x.OutputClass)));
            if (SolutionDirectory != null)
            {
                VcsRoot = SolutionDirectory.Contains("\\depot\\")
                    ? '/' + Path.GetDirectoryName(SolutionDirectory).Substring(SolutionDirectory.IndexOf("\\depot\\")).Replace('\\', '/')
                    : SolutionDirectory.Contains("\\projects\\")
                        ? "//depot" + Path.GetDirectoryName(SolutionDirectory).Substring(SolutionDirectory.IndexOf("\\projects\\")).Replace('\\', '/')
                        : null;
            }
        }

        public string Name { get; private set; }
        public string SolutionDirectory { get; private set; }
        public string VcsRoot { get; private set; }
        public string Branch { get; private set; }
        public Solution Solution { get; private set; }
        public List<Artifact> Artifacts { get; private set; }
    }

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

    public enum OutputClass
    {
        ClassLibrary,
        NServiceBus,
        WindowsService,
        WebDeploy
    }
}