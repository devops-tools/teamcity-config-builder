using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace TeamCityConfigBuilder.Shell
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

        public SolutionProject(object solutionProject, string rootPath)
        {
            ProjectName = Info["ProjectName"].GetValue(solutionProject, null) as string;
            RelativePath = Info["RelativePath"].GetValue(solutionProject, null) as string;
            ProjectGuid = Info["ProjectGuid"].GetValue(solutionProject, null) as string;

            var xml = XDocument.Load(Path.Combine(rootPath, RelativePath));
            XNamespace ns = xml.Root.Attribute("xmlns").Value;
            OutputType = xml
                .Element(ns + "Project")
                .Element(ns + "PropertyGroup")
                .Element(ns + "OutputType").Value;
            OutputPath = xml.Descendants(ns + "OutputPath").First(x => x.Parent.Attribute("Condition").Value.Contains("'$(Configuration)|$(Platform)' == 'Release|AnyCPU'")).Value.TrimEnd('\\');
            if (ProjectName != null && OutputType == "Library" && (ProjectName.ToLower().Contains(".nsb") || ProjectName.ToLower().Contains(".endpoint")))
                OutputClass = OutputClass.NServiceBus;
        }
    }
}