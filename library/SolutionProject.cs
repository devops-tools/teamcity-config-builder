using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace TeamCityConfigBuilder.Library
{
    [DebuggerDisplay("{ProjectName}, {RelativePath}, {ProjectGuid}")]
    public class SolutionProject
    {
        static readonly Dictionary<string, PropertyInfo> Info;

        static SolutionProject()
        {
            var type = Type.GetType("Microsoft.Build.Construction.ProjectInSolution, Microsoft.Build, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a", false, false);
            Info = new Dictionary<string, PropertyInfo>
            {
                {"ProjectName", type.GetProperty("ProjectName", BindingFlags.NonPublic | BindingFlags.Instance)},
                {"RelativePath", type.GetProperty("RelativePath", BindingFlags.NonPublic | BindingFlags.Instance)},
                {"ProjectGuid", type.GetProperty("ProjectGuid", BindingFlags.NonPublic | BindingFlags.Instance)}
            };
        }

        public string ProjectName { get; private set; }
        public string RelativePath { get; private set; }
        public string ProjectGuid { get; private set; }
        public string OutputPath { get; private set; }
        public string OutputType { get; private set; }
        public OutputClass OutputClass { get; private set; }
        public Artifact Artifact { get; private set; }

        public SolutionProject(object solutionProject, string rootPath)
        {
            ProjectName = Info["ProjectName"].GetValue(solutionProject, null) as string;
            RelativePath = Info["RelativePath"].GetValue(solutionProject, null) as string;
            ProjectGuid = Info["ProjectGuid"].GetValue(solutionProject, null) as string;

            var projectFile = Path.Combine(rootPath, RelativePath);
            //if (!projectFile.ToLower().EndsWith(".csproj") && Directory.Exists(projectFile))
                //projectFile = Directory.GetFiles(projectFile, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (/*projectFile != null &&*/ File.Exists(projectFile)
                && !Path.GetExtension(projectFile).StartsWith(".wix", StringComparison.InvariantCultureIgnoreCase)
                && Path.GetExtension(projectFile).EndsWith("proj", StringComparison.InvariantCultureIgnoreCase))
            {
                var xml = XDocument.Load(projectFile);
                XNamespace ns = xml.Root.Attribute("xmlns").Value;
                OutputType = xml
                    .Element(ns + "Project")
                    .Element(ns + "PropertyGroup")
                    .Element(ns + "OutputType").Value;
                OutputPath = xml.Descendants(ns + "OutputPath").First(x => x.Parent.Attribute("Condition").Value.Contains("'$(Configuration)|$(Platform)' == 'Release|AnyCPU'")).Value.TrimEnd('\\');
                var projectFolder = Path.GetDirectoryName(projectFile);
                
                // web app?
                if (File.Exists(Path.Combine(projectFolder, "web.config")))
                    OutputClass = OutputClass.WebDeploy;

                // nsb?
                else if (File.Exists(Path.Combine(projectFolder, "app.config"))
                    && ProjectName != null && OutputType == "Library"
                    && (!ProjectName.ToLower().Contains(".test"))
                    && (ProjectName.ToLower().Contains(".nsb") || ProjectName.ToLower().Contains(".endpoint")))
                    OutputClass = OutputClass.NServiceBus;

                // win svc?
                else if (File.Exists(Path.Combine(projectFolder, "app.config"))
                    && File.Exists(Path.Combine(projectFolder, "program.cs"))
                    && File.ReadAllText(Path.Combine(projectFolder, "program.cs")).Contains("ServiceBase.Run"))
                    OutputClass = OutputClass.WindowsService;
            }
            if (ProjectName != null)
                switch (OutputClass)
                {
                    case OutputClass.NServiceBus:
                    case OutputClass.WindowsService:
                        Artifact = new Artifact(ProjectName, Path.Combine("Source", Path.GetDirectoryName(RelativePath), OutputPath), OutputClass);
                        break;
                    case OutputClass.WebDeploy:
                        Artifact = new Artifact(ProjectName, Path.Combine("WebDeploy", ProjectName), OutputClass);
                        break;
                    default:
                        Artifact = null;
                        break;
                }
            else
                Artifact = null;
        }
    }
}