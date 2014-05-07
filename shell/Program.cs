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
        static void Main()
        {
            var buildProjects = Directory.GetFiles(ConfigurationManager.AppSettings.Get("DiscoveryFolder"), "*.sln", SearchOption.AllDirectories)
                .Select(x => new BuildProject(x));
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
                            Console.WriteLine("/{0}", buildId.Replace('_', '/'));
                            var bc = CreateBuildConfiguration(buildConfig, buildId, tcp.Id);
                            var xml = XDocument.Load(Path.Combine(ConfigurationManager.AppSettings.Get("TemplateFolder"), string.Concat(buildConfig, ".xml"))).Root;
                            Console.WriteLine(" - Settings");
                            foreach (var setting in xml.Descendants("option").Select(x => new { Name = x.Attribute("name").Value, x.Attribute("value").Value }))
                            {
                                var url = string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/settings/", setting.Name);
                                var payload = (setting.Name == "buildNumberPattern" && buildConfig != "Build")
                                    ? string.Format(setting.Value, buildConfig == "Drop" ? buildId.Replace("_Drop", "_Build") : buildId.Replace("_Release", "_Drop"))
                                    : setting.Value;
                                Console.WriteLine("  - Setting: {0}, value: {1}", setting.Name, payload);
                                Put(url, payload);
                            }
                            Console.WriteLine(" - Parameters");
                            foreach (var param in xml.Descendants("param").Select(x => new { Name = x.Attribute("name").Value, x.Attribute("value").Value }))
                            {
                                var url = string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/parameters/", param.Name);
                                var payload = (param.Name == "drop_folder")
                                    ? string.Format(param.Value, buildProject.Name, buildProject.Branch)
                                    : param.Value;
                                Console.WriteLine("  - Parameter: {0}, value: {1}", param.Name, payload);
                                Put(url, payload);
                            }
                            Console.WriteLine(" - Steps");
                            foreach (var stepXml in xml.Descendants("step"))
                            {
                                //build step special case: package web application for web deploy. repeat step for each web project in solution.
                                switch (stepXml.Attribute("id").Value)
                                {
                                    case "RUNNER_128":
                                        foreach (var sp in buildProject.Solution.Projects.Where(x=>x.OutputClass == OutputClass.WebDeploy))
                                        {
                                            Console.WriteLine("  - Step: {0}", stepXml.Attribute("name").Value.Replace("{0}", sp.ProjectName));
                                            Post(
                                                string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/steps"),
                                                stepXml.ToString()
                                                    .Replace("{0}", sp.ProjectName)
                                                    .Replace("{1}", Path.Combine("Source", sp.RelativePath)));
                                        }
                                        break;
                                    default:
                                        Console.WriteLine("  - Step: {0}", stepXml.Attribute("name").Value);
                                        Post(
                                            string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/steps"),
                                            stepXml.ToString().Replace("{0}", string.Concat(@"Source\", buildProject.Name, ".sln")));
                                        break;
                                }
                            }
                            Console.WriteLine(" - Features");
                            foreach (var featureXml in xml.Descendants("feature").Select(x => x.ToString()))
                            {
                                Post(
                                    string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/features"),
                                    featureXml);
                            }
                            Console.WriteLine(" - Triggers");
                            foreach (var triggerXml in xml.Descendants("trigger").Select(x => x.ToString()))
                            {
                                Post(
                                    string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/triggers"),
                                    triggerXml);
                            }
                            switch (buildConfig)
                            {
                                case "Build":
                                    if (/*buildProject.Artifacts.Any()*/true)
                                    {
                                        Console.WriteLine(" - Artifacts");
                                        Put(
                                            string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/settings/artifactRules"),
                                            string.Format("{0}\nWebDeploy => artifacts.zip!WebDeploy\nScripts => artifacts.zip!Scripts", string.Join("\n", buildProject.Artifacts.Select(x => string.Format("{0} => artifacts.zip!{1}", x.RelativePath, x.Name)))));
                                    }
                                    if (!string.IsNullOrWhiteSpace(buildProject.VcsRoot) && buildProject.VcsRoot.StartsWith("//depot/", StringComparison.InvariantCultureIgnoreCase))
                                    {
                                        Console.WriteLine(" - VcsRoots");
                                        Post(
                                                string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/vcs-roots"),
                                                string.Format(File.ReadAllText(Path.Combine(ConfigurationManager.AppSettings.Get("TemplateFolder"), "vcs-p4.xml")),
                                                    string.Concat(tcp.Id, "_Source"),
                                                    "Source",
                                                    tcp.Id,
                                                    tcp.Name,
                                                    buildProject.VcsRoot,
                                                    "svc_tcbuild_dev",
                                                    "zxx99860a5d5a21aa52ec3295a7ca7d1819"));
                                        foreach (var vcsRootXml in xml.Descendants("vcs-root-entry").Select(x => x.ToString()))
                                        {
                                            Console.WriteLine("  - VcsRoot: {0}", string.Concat(tcp.Id, "_Source"));
                                            Post(
                                                string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/vcs-root-entries"),
                                                string.Format(vcsRootXml,
                                                    string.Concat(tcp.Id, "_Source"),
                                                    "Source"));
                                        }
                                        
                                    }
                                    break;
                                case "Drop":
                                    Console.WriteLine(" - ArtifactDependencies");
                                    foreach (var dependencyXml in xml.Descendants("artifact-dependency").Select(x => x.ToString()))
                                    {
                                        Console.WriteLine("  - ArtifactDependency: {0}", buildId.Replace("_Drop", "_Build"));
                                        Post(
                                            string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/artifact-dependencies"),
                                            string.Format(dependencyXml,
                                                buildId.Replace("_Drop", "_Build"),
                                                "Build",
                                                tcp.Id,
                                                tcp.Name));
                                    }
                                    Console.WriteLine(" - SnapshotDependencies");
                                    foreach (var dependencyXml in xml.Descendants("snapshot-dependency").Select(x => x.ToString()))
                                    {
                                        Console.WriteLine("  - SnapshotDependency: {0}", buildId.Replace("_Drop", "_Build"));
                                        Post(
                                            string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/snapshot-dependencies"),
                                            string.Format(dependencyXml,
                                                buildId.Replace("_Drop", "_Build"),
                                                "Build",
                                                tcp.Id,
                                                tcp.Name));
                                    }
                                    Console.WriteLine(" - VcsRoots");
                                    Post(
                                            string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/vcs-roots"),
                                            string.Format(File.ReadAllText(Path.Combine(ConfigurationManager.AppSettings.Get("TemplateFolder"), "vcs-robocopy.xml")),
                                                string.Concat(tcp.Id, "_RoboCopy"),
                                                "RoboCopy",
                                                tcp.Id,
                                                tcp.Name));
                                    foreach (var vcsRootXml in xml.Descendants("vcs-root-entry").Select(x => x.ToString()))
                                    {
                                        Console.WriteLine("  - VcsRoot: {0}", string.Concat(tcp.Id, "_RoboCopy"));
                                        Post(
                                            string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/vcs-root-entries"),
                                            string.Format(vcsRootXml,
                                                string.Concat(tcp.Id, "_RoboCopy"),
                                                "RoboCopy"));
                                    }
                                    Console.WriteLine(" - Artifacts");
                                    Put(
                                        string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/settings/artifactRules"),
                                        @"%drop_folder%\%build.number% => artifacts.zip");
                                    break;
                                case "Release":
                                    Console.WriteLine(" - ArtifactDependencies");
                                    foreach (var dependencyXml in xml.Descendants("artifact-dependency").Select(x => x.ToString()))
                                    {
                                        Console.WriteLine("  -ArtifactDependency: {0}", buildId.Replace("_Release", "_Drop"));
                                        Post(
                                            string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/artifact-dependencies"),
                                            string.Format(dependencyXml,
                                                buildId.Replace("_Release", "_Drop"),
                                                "Drop",
                                                tcp.Id,
                                                tcp.Name));
                                    }
                                    Console.WriteLine(" - SnapshotDependencies");
                                    foreach (var dependencyXml in xml.Descendants("snapshot-dependency").Select(x => x.ToString()))
                                    {
                                        Console.WriteLine("  - SnapshotDependency: {0}", buildId.Replace("_Release", "_Drop"));
                                        Post(
                                            string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/snapshot-dependencies"),
                                            string.Format(dependencyXml,
                                                buildId.Replace("_Release", "_Drop"),
                                                "Drop",
                                                tcp.Id,
                                                tcp.Name));
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
            Console.WriteLine("press any key to exit");
            Console.ReadKey();
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
                var response = client.UploadString(url, payload);
            }
        }

        static void Put(string url, string payload)
        {
            using (var client = new WebClient())
            {
                var up = string.Format("{0}:{1}", ConfigurationManager.AppSettings.Get("TeamCityUsername"), ConfigurationManager.AppSettings.Get("TeamCityPassword"));
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(up), Base64FormattingOptions.None);
                client.Headers[HttpRequestHeader.Authorization] = "Basic " + credentials;
                client.Headers[HttpRequestHeader.ContentType] = "text/plain";
                var response = client.UploadString(url, "PUT", payload);
            }
        }
    }
}
