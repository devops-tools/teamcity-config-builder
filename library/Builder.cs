using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace TeamCityConfigBuilder.Library
{
    public static class Builder
    {
        public static void Run(string discoveryFolder, string templateFolder, string teamCityUrl, string teamCityUsername, string teamCityPassword, bool overwrite, IMessageObserver observer)
        {
            var buildProjects = Directory.GetFiles(discoveryFolder, "*.sln", SearchOption.AllDirectories)
                .Select(x => new BuildProject(x));
            foreach (var buildProject in buildProjects)
            {
                TeamCityProject tcp = null;
                var projectTree = buildProject.Name.Split('.').ToList();
                projectTree.Add(buildProject.Branch.Replace(".", string.Empty));
                for (var i = 0; i < projectTree.Count; i++)
                {
                    var projectId = string.Join("_", projectTree.GetRange(0, i + 1));
                    tcp = GetProject(projectId, teamCityUrl, teamCityUsername, teamCityPassword);
                    if ((i == (projectTree.Count - 1)) && overwrite && tcp != null && tcp.BuildConfigurations != null && tcp.BuildConfigurations.Any())
                    {
                        DeleteProject(tcp.Id, teamCityUrl, teamCityUsername, teamCityPassword);
                        tcp = null;
                    }
                    if (tcp == null)
                    {
                        observer.Notify("CreateProject: {0}", projectId);
                        tcp = CreateProject(projectTree[i], projectId, string.Join("_", projectTree.GetRange(0, i)),
                            teamCityUrl, teamCityUsername, teamCityPassword);
                    }
                    else
                    {
                        observer.Notify("{0} exists", projectId);
                    }
                }
                if (tcp != null)
                {
                    foreach (var buildConfig in new[] { "Build", "Drop", "Release" })
                    {
                        var buildId = string.Concat(tcp.Id, "_", buildConfig);
                        if (overwrite || tcp.BuildConfigurations == null || !tcp.BuildConfigurations.Any(x => x.Id.Equals(buildId)))
                        {
                            observer.Notify("/{0}", buildId.Replace('_', '/'));
                            var bc = CreateBuildConfiguration(buildConfig, buildId, tcp.Id, teamCityUrl, teamCityUsername, teamCityPassword);
                            var xml = XDocument.Load(Path.Combine(templateFolder, string.Concat(buildConfig, ".xml"))).Root;
                            observer.Notify(" - Settings");
                            foreach (var setting in xml.Descendants("option").Select(x => new { Name = x.Attribute("name").Value, x.Attribute("value").Value }))
                            {
                                var url = string.Concat(teamCityUrl, "/httpAuth/app/rest/buildTypes/", bc.Id, "/settings/", setting.Name);
                                var payload = (setting.Name == "buildNumberPattern" && buildConfig != "Build")
                                    ? string.Format(setting.Value, buildConfig == "Drop" ? buildId.Replace("_Drop", "_Build") : buildId.Replace("_Release", "_Drop"))
                                    : setting.Value;
                                observer.Notify("  - Setting: {0}, value: {1}", setting.Name, payload);
                                Put(url, payload, teamCityUsername, teamCityPassword);
                            }
                            observer.Notify(" - Parameters");
                            foreach (var param in xml.Descendants("param").Select(x => new { Name = x.Attribute("name").Value, x.Attribute("value").Value }))
                            {
                                var url = string.Concat(teamCityUrl, "/httpAuth/app/rest/buildTypes/", bc.Id, "/parameters/", param.Name);
                                var payload = param.Value;
                                observer.Notify("  - Parameter: {0}, value: {1}", param.Name, payload);
                                Put(url, payload, teamCityUsername, teamCityPassword);
                            }
                            observer.Notify(" - Steps");
                            foreach (var stepXml in xml.Descendants("step"))
                            {
                                //build step special case: package web application for web deploy. repeat step for each web project in solution.
                                switch (stepXml.Attribute("id").Value)
                                {
                                    case "RUNNER_128":
                                        foreach (var sp in buildProject.Solution.Projects.Where(x => x.OutputClass == OutputClass.WebDeploy))
                                        {
                                            observer.Notify("  - Step: {0}", stepXml.Attribute("name").Value.Replace("{0}", sp.ProjectName));
                                            Post(
                                                string.Concat(teamCityUrl, "/httpAuth/app/rest/buildTypes/", bc.Id, "/steps"),
                                                stepXml.ToString()
                                                    .Replace("{0}", sp.ProjectName)
                                                    .Replace("{1}", Path.Combine("Source", sp.RelativePath)),
                                                teamCityUsername, teamCityPassword);
                                        }
                                        break;
                                    default:
                                        observer.Notify("  - Step: {0}", stepXml.Attribute("name").Value);
                                        Post(
                                            string.Concat(teamCityUrl, "/httpAuth/app/rest/buildTypes/", bc.Id, "/steps"),
                                            stepXml.ToString().Replace("{0}", string.Concat(@"Source\", buildProject.Name, ".sln")),
                                            teamCityUsername, teamCityPassword);
                                        break;
                                }
                            }
                            observer.Notify(" - Features");
                            foreach (var featureXml in xml.Descendants("feature").Select(x => x.ToString()))
                            {
                                Post(
                                    string.Concat(teamCityUrl, "/httpAuth/app/rest/buildTypes/", bc.Id, "/features"),
                                    featureXml,
                                    teamCityUsername, teamCityPassword);
                            }
                            observer.Notify(" - Triggers");
                            foreach (var triggerXml in xml.Descendants("trigger").Select(x => x.ToString()))
                            {
                                Post(
                                    string.Concat(teamCityUrl, "/httpAuth/app/rest/buildTypes/", bc.Id, "/triggers"),
                                    triggerXml,
                                    teamCityUsername, teamCityPassword);
                            }
                            switch (buildConfig)
                            {
                                case "Build":
                                    if (/*buildProject.Artifacts.Any()*/true)
                                    {
                                        observer.Notify(" - Artifacts");
                                        Put(
                                            string.Concat(teamCityUrl, "/httpAuth/app/rest/buildTypes/", bc.Id, "/settings/artifactRules"),
                                            string.Join("\n", buildProject.Solution.Projects.Where(x => x.Artifact != null).Select(x => string.Format("{0} => {1}.zip", x.Artifact.RelativePath, x.Artifact.Name))),
                                            teamCityUsername, teamCityPassword);
                                    }
                                    if (!string.IsNullOrWhiteSpace(buildProject.VcsRoot) && buildProject.VcsRoot.StartsWith("//depot/", StringComparison.InvariantCultureIgnoreCase))
                                    {
                                        observer.Notify(" - VcsRoots");
                                        Post(
                                            string.Concat(teamCityUrl, "/httpAuth/app/rest/vcs-roots"),
                                            string.Format(File.ReadAllText(Path.Combine(templateFolder, "vcs-p4.xml")),
                                                string.Concat(tcp.Id, "_Source"),
                                                "Source",
                                                tcp.Id,
                                                tcp.Name,
                                                buildProject.VcsRoot,
                                                "svc_tcbuild_dev",
                                                "Gti%xEkf&amp;8yj"),
                                            teamCityUsername, teamCityPassword);
                                        foreach (var vcsRootXml in xml.Descendants("vcs-root-entry").Select(x => x.ToString()))
                                        {
                                            observer.Notify("  - VcsRoot: {0}", string.Concat(tcp.Id, "_Source"));
                                            Post(
                                                string.Concat(teamCityUrl, "/httpAuth/app/rest/buildTypes/", bc.Id, "/vcs-root-entries"),
                                                string.Format(vcsRootXml,
                                                    string.Concat(tcp.Id, "_Source"),
                                                    "Source"),
                                                teamCityUsername, teamCityPassword);
                                        }

                                    }
                                    break;
                                case "Drop":
                                    observer.Notify(" - ArtifactDependencies");
                                    foreach (var dependencyXml in xml.Descendants("artifact-dependency").Select(x => x.ToString()))
                                    {
                                        observer.Notify("  - ArtifactDependency: {0}", buildId.Replace("_Drop", "_Build"));
                                        Post(
                                            string.Concat(teamCityUrl, "/httpAuth/app/rest/buildTypes/", bc.Id, "/artifact-dependencies"),
                                            string.Format(dependencyXml,
                                                buildId.Replace("_Drop", "_Build"),
                                                "Build",
                                                tcp.Id,
                                                tcp.Name,
                                                //test this dependency artifact rule when you get back from the pub!
                                                string.Join("&#xD;&#xA;", buildProject.Solution.Projects.Where(x => x.Artifact != null).Select(x => string.Format("+:{0}.zip!** =&gt; {0}", x.Artifact.Name)))),
                                            teamCityUsername, teamCityPassword);
                                    }
                                    observer.Notify(" - SnapshotDependencies");
                                    foreach (var dependencyXml in xml.Descendants("snapshot-dependency").Select(x => x.ToString()))
                                    {
                                        observer.Notify("  - SnapshotDependency: {0}", buildId.Replace("_Drop", "_Build"));
                                        Post(
                                            string.Concat(teamCityUrl, "/httpAuth/app/rest/buildTypes/", bc.Id, "/snapshot-dependencies"),
                                            string.Format(dependencyXml,
                                                buildId.Replace("_Drop", "_Build"),
                                                "Build",
                                                tcp.Id,
                                                tcp.Name),
                                            teamCityUsername, teamCityPassword);
                                    }
                                    observer.Notify(" - VcsRoots");
                                    Post(
                                            string.Concat(teamCityUrl, "/httpAuth/app/rest/vcs-roots"),
                                            string.Format(File.ReadAllText(Path.Combine(templateFolder, "vcs-robocopy.xml")),
                                                string.Concat(tcp.Id, "_RoboCopy"),
                                                "RoboCopy",
                                                tcp.Id,
                                                tcp.Name),
                                            teamCityUsername, teamCityPassword);
                                    foreach (var vcsRootXml in xml.Descendants("vcs-root-entry").Select(x => x.ToString()))
                                    {
                                        observer.Notify("  - VcsRoot: {0}", string.Concat(tcp.Id, "_RoboCopy"));
                                        Post(
                                            string.Concat(teamCityUrl, "/httpAuth/app/rest/buildTypes/", bc.Id, "/vcs-root-entries"),
                                            string.Format(vcsRootXml,
                                                string.Concat(tcp.Id, "_RoboCopy"),
                                                "RoboCopy"),
                                            teamCityUsername, teamCityPassword);
                                    }
                                    observer.Notify(" - Artifacts");
                                    Put(
                                        string.Concat(teamCityUrl, "/httpAuth/app/rest/buildTypes/", bc.Id, "/settings/artifactRules"),
                                        @"%drop_folder%\%build.number% => artifacts.zip",
                                        teamCityUsername, teamCityPassword);
                                    break;
                                case "Release":
                                    observer.Notify(" - ArtifactDependencies");
                                    foreach (var dependencyXml in xml.Descendants("artifact-dependency").Select(x => x.ToString()))
                                    {
                                        observer.Notify("  -ArtifactDependency: {0}", buildId.Replace("_Release", "_Drop"));
                                        Post(
                                            string.Concat(teamCityUrl, "/httpAuth/app/rest/buildTypes/", bc.Id, "/artifact-dependencies"),
                                            string.Format(dependencyXml,
                                                buildId.Replace("_Release", "_Drop"),
                                                "Drop",
                                                tcp.Id,
                                                tcp.Name),
                                            teamCityUsername, teamCityPassword);
                                    }
                                    observer.Notify(" - SnapshotDependencies");
                                    foreach (var dependencyXml in xml.Descendants("snapshot-dependency").Select(x => x.ToString()))
                                    {
                                        observer.Notify("  - SnapshotDependency: {0}", buildId.Replace("_Release", "_Drop"));
                                        Post(
                                            string.Concat(teamCityUrl, "/httpAuth/app/rest/buildTypes/", bc.Id, "/snapshot-dependencies"),
                                            string.Format(dependencyXml,
                                                buildId.Replace("_Release", "_Drop"),
                                                "Drop",
                                                tcp.Id,
                                                tcp.Name),
                                            teamCityUsername, teamCityPassword);
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
        }

        static TeamCityProject GetProject(string id, string teamCityUrl, string teamCityUsername, string teamCityPassword)
        {
            try
            {
                using (var client = new WebClient())
                {
                    var up = string.Format("{0}:{1}", teamCityUsername, teamCityPassword);
                    var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(up), Base64FormattingOptions.None);
                    client.Headers[HttpRequestHeader.Authorization] = "Basic " + credentials;
                    var e = XDocument.Load(client.OpenRead(string.Concat(teamCityUrl, "/httpAuth/app/rest/projects/", id))).Root;
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

        static TeamCityProject CreateProject(string name, string id, string parentId, string teamCityUrl, string teamCityUsername, string teamCityPassword)
        {
            var url = string.Concat(teamCityUrl, "/httpAuth/app/rest/projects");
            var payload = string.Format("<newProjectDescription name='{0}' id='{1}'><parentProject locator='id:{2}'/></newProjectDescription>", name, id, parentId);
            Post(url, payload, teamCityUsername, teamCityPassword);
            return GetProject(id, teamCityUrl, teamCityUsername, teamCityPassword);
        }

        static TeamCityBuildConfiguration GetBuildConfiguration(string id, string teamCityUrl, string teamCityUsername, string teamCityPassword)
        {
            try
            {
                using (var client = new WebClient())
                {
                    var up = string.Format("{0}:{1}", teamCityUsername, teamCityPassword);
                    var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(up), Base64FormattingOptions.None);
                    client.Headers[HttpRequestHeader.Authorization] = "Basic " + credentials;
                    var e = XDocument.Load(client.OpenRead(string.Concat(teamCityUrl, "/httpAuth/app/rest/buildTypes/", id))).Root;
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

        static TeamCityBuildConfiguration CreateBuildConfiguration(string name, string id, string projectId, string teamCityUrl, string teamCityUsername, string teamCityPassword)
        {
            var url = string.Concat(teamCityUrl, "/httpAuth/app/rest/projects/", projectId, "/buildTypes");
            var payload = string.Format("<newBuildTypeDescription name='{0}' id='{1}' />", name, id);
            Post(url, payload, teamCityUsername, teamCityPassword);
            return GetBuildConfiguration(id, teamCityUrl, teamCityUsername, teamCityPassword);
        }

        static void DeleteProject(string id, string teamCityUrl, string teamCityUsername, string teamCityPassword)
        {
            string response;
            using (var client = new WebClient())
            {
                var up = string.Format("{0}:{1}", teamCityUsername, teamCityPassword);
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(up), Base64FormattingOptions.None);
                client.Headers[HttpRequestHeader.Authorization] = "Basic " + credentials;
                response = client.UploadString(string.Concat(teamCityUrl, "/httpAuth/app/rest/projects/id:", id), "DELETE", string.Empty);
            }
        }

        static void Post(string url, string payload, string username, string password)
        {
            using (var client = new WebClient())
            {
                var up = string.Format("{0}:{1}", username, password);
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(up), Base64FormattingOptions.None);
                client.Headers[HttpRequestHeader.Authorization] = "Basic " + credentials;
                client.Headers[HttpRequestHeader.ContentType] = "application/xml";
                var response = client.UploadString(url, payload);
            }
        }

        static void Put(string url, string payload, string username, string password)
        {
            using (var client = new WebClient())
            {
                var up = string.Format("{0}:{1}", username, password);
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(up), Base64FormattingOptions.None);
                client.Headers[HttpRequestHeader.Authorization] = "Basic " + credentials;
                client.Headers[HttpRequestHeader.ContentType] = "text/plain";
                var response = client.UploadString(url, "PUT", payload);
            }
        }
    }
}
