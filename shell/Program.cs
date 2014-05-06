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
                            var bc = CreateBuildConfiguration(buildConfig, buildId, tcp.Id);
                            var xml = XDocument.Load(Path.Combine(ConfigurationManager.AppSettings.Get("TemplateFolder"), string.Concat(buildConfig, ".xml"))).Root;
                            foreach (var setting in xml.Descendants("option").Select(x => new { Name = x.Attribute("name").Value, x.Attribute("value").Value }))
                            {
                                if (setting.Name == "buildNumberPattern" && buildConfig != "Build")
                                {
                                    Put(
                                        string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/settings/", setting.Name),
                                        string.Format(setting.Value, buildConfig == "Drop" ? buildId.Replace("_Drop", "_Build") : buildId.Replace("_Release", "_Drop")));
                                }
                                else
                                {
                                    Put(
                                        string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/settings/", setting.Name),
                                        setting.Value);
                                }
                            }
                            foreach (var param in xml.Descendants("param").Select(x => new { Name = x.Attribute("name").Value, x.Attribute("value").Value }))
                            {
                                Put(
                                    string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/parameters/", param.Name),
                                    param.Value);
                            }
                            foreach (var stepXml in xml.Descendants("step").Select(x => x.ToString()))
                            {
                                Post(
                                    string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/steps"),
                                    stepXml.Replace("{0}", string.Concat(@"Source\", buildProject.Name, ".sln")));
                            }
                            foreach (var featureXml in xml.Descendants("feature").Select(x => x.ToString()))
                            {
                                Post(
                                    string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/features"),
                                    featureXml);
                            }
                            foreach (var triggerXml in xml.Descendants("trigger").Select(x => x.ToString()))
                            {
                                Post(
                                    string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/triggers"),
                                    triggerXml);
                            }
                            switch (buildConfig)
                            {
                                case "Build":
                                    if (buildProject.Artifacts.Any())
                                    {
                                        Put(
                                            string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/settings/artifactRules"),
                                            string.Format("{0}\nScripts => artifacts.zip!Scripts", string.Join("\n", buildProject.Artifacts.Select(x => string.Format("{0} => artifacts.zip!{1}", Path.Combine("Source", x.RelativePath), x.Name)))));
                                    }
                                    break;
                                case "Drop":
                                    foreach (var dependencyXml in xml.Descendants("artifact-dependency").Select(x => x.ToString()))
                                    {
                                        Post(
                                            string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/artifact-dependencies"),
                                            string.Format(dependencyXml,
                                                buildId.Replace("_Drop", "_Build"),
                                                "Build",
                                                tcp.Id,
                                                tcp.Name));
                                    }
                                    foreach (var dependencyXml in xml.Descendants("snapshot-dependency").Select(x => x.ToString()))
                                    {
                                        Post(
                                            string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/snapshot-dependencies"),
                                            string.Format(dependencyXml,
                                                buildId.Replace("_Drop", "_Build"),
                                                "Build",
                                                tcp.Id,
                                                tcp.Name));
                                    }
                                    break;
                                case "Release":
                                    foreach (var dependencyXml in xml.Descendants("artifact-dependency").Select(x => x.ToString()))
                                    {
                                        Post(
                                            string.Concat(ConfigurationManager.AppSettings.Get("TeamCityUrl").TrimEnd('/'), "/httpAuth/app/rest/buildTypes/", bc.Id, "/artifact-dependencies"),
                                            string.Format(dependencyXml,
                                                buildId.Replace("_Release", "_Drop"),
                                                "Drop",
                                                tcp.Id,
                                                tcp.Name));
                                    }
                                    foreach (var dependencyXml in xml.Descendants("snapshot-dependency").Select(x => x.ToString()))
                                    {
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

        static void Put(string url, string payload)
        {
            using (var client = new WebClient())
            {
                var up = string.Format("{0}:{1}", ConfigurationManager.AppSettings.Get("TeamCityUsername"), ConfigurationManager.AppSettings.Get("TeamCityPassword"));
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(up), Base64FormattingOptions.None);
                client.Headers[HttpRequestHeader.Authorization] = "Basic " + credentials;
                client.Headers[HttpRequestHeader.ContentType] = "text/plain";
                client.UploadString(url, "PUT", payload);
            }
        }
    }
}
