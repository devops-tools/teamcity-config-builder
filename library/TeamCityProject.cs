using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace TeamCityConfigBuilder.Library
{
    public class TeamCityProject
    {
        public TeamCityProject(XElement e)
        {
            Id = e.Attribute("id").Value;
            Name = e.Attribute("name").Value;
            Href = e.Attribute("href").Value;
            var p = e.Element("parentProject");
            if (p != null)
                Parent = new TeamCityProject(p);
            var c = e.Element("projects");
            if (c != null && c.HasElements)
                Children = new List<TeamCityProject>(c.Elements("project").Select(x => new TeamCityProject(x)));
            var b = e.Element("buildTypes");
            if (b != null && b.HasElements)
                BuildConfigurations = new List<TeamCityBuildConfiguration>(b.Elements("buildType").Select(x => new TeamCityBuildConfiguration(x, this)));
        }
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Href { get; private set; }
        public TeamCityProject Parent { get; private set; }
        public List<TeamCityProject> Children { get; private set; }
        public List<TeamCityBuildConfiguration> BuildConfigurations { get; private set; }
    }
    public class TeamCityBuildConfiguration
    {
        public TeamCityBuildConfiguration(XElement e, TeamCityProject project)
        {
            Id = e.Attribute("id").Value;
            Name = e.Attribute("name").Value;
            Project = project;
        }
        public string Id { get; private set; }
        public string Name { get; private set; }
        public TeamCityProject Project { get; private set; }
    }
}