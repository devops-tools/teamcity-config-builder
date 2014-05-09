using System.IO;

namespace TeamCityConfigBuilder.Library
{
    public class BuildProject
    {
        public BuildProject(string solutionPath)
        {
            Name = Path.GetFileNameWithoutExtension(solutionPath);
            SolutionDirectory = Path.GetDirectoryName(solutionPath);
            Branch = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(solutionPath)));
            Solution = new Solution(solutionPath);
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
    }
}