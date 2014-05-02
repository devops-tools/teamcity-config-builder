using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace TeamCityConfigBuilder.Shell
{
    class Program
    {
        static void Main(string[] args)
        {
            const string p4Root = @"c:\p4\ws1\depot\projects\Beazley.AgressoVendors\";
            const string tcApi = "http://teamcity:8111/httpAuth/app/rest";

            var buildProjects = Directory.GetFiles(p4Root, "*.sln", SearchOption.AllDirectories)
                .Select(x => new BuildProject
                {
                    Name = Path.GetFileNameWithoutExtension(x),
                    SolutionDirectory = Path.GetDirectoryName(x),
                    Branch = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(x))),
                    Solution = new Solution(x)
                });
            foreach (var buildProject in buildProjects)
            {
                TeamCityProject tcp = null;
                var projectTree = buildProject.Name.Split('.').ToList();
                projectTree.Add(buildProject.Branch);
                for (var i = 0; i < projectTree.Count; i++)
                {
                    var projectId = string.Join("_", projectTree.GetRange(0, i + 1));
                    tcp = GetProject(projectId)
                        ?? CreateProject(projectTree[i], projectId, string.Join("_", projectTree.GetRange(0, i)));
                }
                if (tcp != null)
                {
                    foreach (var buildConfig in new[] { "Build", "Drop", "Release" })
                    {
                        var buildId = string.Concat(tcp.Id, "_", buildConfig);
                        if (tcp.BuildConfigurations == null || !tcp.BuildConfigurations.Any(x => x.Id.Equals(buildId)))
                        {
                            var bc = CreateBuildConfiguration(buildConfig, buildId, tcp.Id);
                            switch (buildConfig)
                            {
                                case "Build":
                                    // Nuget Restore Packages
                                    Post(
                                        string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/steps"),
                                        string.Format("<step id=\"RUNNER_211\" name=\"Restore Packages\" type=\"jb.nuget.installer\" disabled=\"false\"><properties><property name=\"nuget.path\" value=\"?NuGet.CommandLine.DEFAULT.nupkg\"/><property name=\"nuget.sources\" value=\"http://nuget.bfl.local/api/v2/&#xA;https://www.nuget.org/api/v2/\"/><property name=\"nuget.updatePackages.mode\" value=\"sln\"/><property name=\"nugetCustomPath\" value=\"?NuGet.CommandLine.DEFAULT.nupkg\"/><property name=\"nugetPathSelector\" value=\"?NuGet.CommandLine.DEFAULT.nupkg\"/><property name=\"sln.path\" value=\"{0}\"/><property name=\"teamcity.step.mode\" value=\"default\"/></properties></step>", string.Concat(@"Source\", buildProject.Name, ".sln")));
                                    // Compile Solution
                                    Post(
                                        string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/steps"),
                                        string.Format("<step id=\"RUNNER_1\" name=\"Compile Solution\" type=\"VS.Solution\"><properties><property name=\"build-file-path\" value=\"{0}\"/><property name=\"msbuild.prop.Configuration\" value=\"Release\"/><property name=\"msbuild.prop.Platform\" value=\"Any CPU\"/><property name=\"msbuild_version\" value=\"4.5\"/><property name=\"run-platform\" value=\"x86\"/><property name=\"targets\" value=\"Rebuild\"/><property name=\"teamcity.step.mode\" value=\"default\"/><property name=\"toolsVersion\" value=\"4.0\"/><property name=\"vs.version\" value=\"vs2012\"/></properties></step>", string.Concat(@"Source\", buildProject.Name, ".sln")));
                                    break;
                            }
                        }
                    }
                }
            }
        }

        static TeamCityProject GetProject(string id)
        {
            try
            {
                using (var client = new WebClient())
                {
                    var up = string.Format("{0}:{1}", ConfigurationManager.AppSettings.Get("TeamCityUsername"), ConfigurationManager.AppSettings.Get("TeamCityPassword"));
                    var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(up), Base64FormattingOptions.None);
                    client.Headers[HttpRequestHeader.Authorization] = "Basic " + credentials;
                    var e = XDocument.Load(client.OpenRead(string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/projects/", id))).Root;
                    return e == null
                        ? null
                        : new TeamCityProject(e);
                }
            }
            catch (WebException e)
            {
                if (!e.Message.Contains("404"))
                    throw;
            }
            return null;
        }

        static TeamCityProject CreateProject(string name, string id, string parentId)
        {
            var url = string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/projects");
            var payload = string.Format("<newProjectDescription name='{0}' id='{1}'><parentProject locator='id:{2}'/></newProjectDescription>", name, id, parentId);
            Post(url, payload);
            return GetProject(id);
        }

        static TeamCityBuildConfiguration GetBuildConfiguration(string id)
        {
            try
            {
                using (var client = new WebClient())
                {
                    var up = string.Format("{0}:{1}", ConfigurationManager.AppSettings.Get("TeamCityUsername"), ConfigurationManager.AppSettings.Get("TeamCityPassword"));
                    var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(up), Base64FormattingOptions.None);
                    client.Headers[HttpRequestHeader.Authorization] = "Basic " + credentials;
                    var e = XDocument.Load(client.OpenRead(string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", id))).Root;
                    return e == null
                        ? null
                        : new TeamCityBuildConfiguration(e, null);
                }
            }
            catch (WebException e)
            {
                if (!e.Message.Contains("404"))
                    throw;
            }
            return null;
        }

        static TeamCityBuildConfiguration CreateBuildConfiguration(string name, string id, string projectId)
        {
            var url = string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/projects/", projectId, "/buildTypes");
            var payload = string.Format("<newBuildTypeDescription name='{0}' id='{1}' />", name, id);
            Post(url, payload);
            return GetBuildConfiguration(id);
        }

        static void Post(string url, string payload)
        {
            using (var client = new WebClient())
            {
                var up = string.Format("{0}:{1}", ConfigurationManager.AppSettings.Get("TeamCityUsername"), ConfigurationManager.AppSettings.Get("TeamCityPassword"));
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(up), Base64FormattingOptions.None);
                client.Headers[HttpRequestHeader.Authorization] = "Basic " + credentials;
                client.Headers[HttpRequestHeader.ContentType] = "application/xml";
                client.UploadString(url, payload);
            }
        }
    }

    public class BuildProject
    {
        public string Name { get; set; }
        public string SolutionDirectory { get; set; }
        public string Branch { get; set; }
        public Solution Solution { get; set; }
    }
}
