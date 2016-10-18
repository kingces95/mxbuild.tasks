using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using NUnit.Framework;
using System.Reflection;
using System;
using Mxbuild.Tasks;

[assembly: GrabBag("MyStr", "MyObj", 42, IProp = 40, ObjProp = "MyObjProp", StrProp = "MyStrProp")]
[assembly: GrabBag2("gb2")]
[assembly: NugetReference("Foo", "1", TargetFramework = "x")]
[assembly: NugetReference("Bar", "2", TargetFramework = "y")]
[assembly: NugetReference("Baz", "3", TargetFramework = "z")]

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal class NugetReferenceAttribute : Attribute {
    public NugetReferenceAttribute(string id, string version) { }
    public string TargetFramework;
}
internal class GrabBagAttribute : Attribute {
    public GrabBagAttribute(string str, object obj, int i) { }
    public string StrProp;
    public object ObjProp;
    public int IProp;
}
internal class GrabBag2Attribute : Attribute {
    public GrabBag2Attribute(string other) { }
}

namespace Xamarin.Forms.Build {

    public class Msbuild {
        LoggerVerbosity m_verbosity;
        string m_path;

        public Msbuild(string path, LoggerVerbosity verbosity = LoggerVerbosity.Normal) {
            m_verbosity = verbosity;
            m_path = path;
        }

        ILogger CreateLogger(StringBuilder builder) {
            var writer = new StringWriter(builder);
            WriteHandler handler = (x) => writer.WriteLine(x);

            var consoleLogger = new ConsoleLogger(
                m_verbosity,
                write: handler,
                colorSet: null,
                colorReset: null
            );

            return consoleLogger;
        }

        public string Build(string target, Dictionary<string, string> properties = null) {
            var builder = new StringBuilder();

            var loggers = new List<ILogger>();

            // capture log output to string for test assertions
            loggers.Add(CreateLogger(builder));

            // capture log output for display by nunit test results
            loggers.Add(new ConsoleLogger(m_verbosity));

            var projectCollection = new ProjectCollection();
            projectCollection.RegisterLoggers(loggers);

            var project = projectCollection.LoadProject(m_path);

            if (properties != null) {
                foreach (var property in properties)
                    project.SetProperty(property.Key, property.Value);
            }

            try {
                project.Build(target);
            } finally {
                projectCollection.UnregisterAllLoggers();
            }

            return builder.ToString();
        }
    }

    [TestFixture]
    public class TaskTest {
        static string ProjectFile = "test.targets";
        static string Success = "Build succeeded.";

        [Test]
        public void Empty() {
            var result = new Msbuild(ProjectFile).Build(nameof(Empty));
            StringAssert.Contains(Success, result);
        }

        [Test]
        public void AddAssemblyAttribute() {

            var thisAssembly = Assembly.GetExecutingAssembly().Location;

            var result = new Msbuild(ProjectFile).Build(
                target: nameof(AddAssemblyAttribute),
                properties: new Dictionary<string, string>() {
                    ["Assembly"] = $"{thisAssembly};{thisAssembly}",
                }
            );
        }

        [Test]
        public void GetAssemblyAttribute() {

            var thisAssembly = Assembly.GetExecutingAssembly().Location;

            var result = new Msbuild(ProjectFile).Build(
                target: nameof(GetAssemblyAttribute),
                properties: new Dictionary<string, string>() {
                    ["Assembly"] = $"{thisAssembly};{thisAssembly}",
                }
            );
        }

        [Test]
        public void ExpandTemplateLineByLine() {
            var actual = ExpandTemplateXml(
                "template.expected.txt", 
                type: ExpandTemplateType.LineByLine, 
                sort: false, 
                token: "Items0:"
            );
            var expected = File.ReadAllText("template.expected.txt");
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void ExpandTemplateXml() {
            var actual = ExpandTemplateXml(
                "template.expected.xml", 
                sort: false
            );
            var expected = File.ReadAllText("template.expected.xml");
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void ExpandGroupTemplateXml() {
            var actual = ExpandTemplateXml(
                "groupTemplate.xml", 
                sort: true
            );
        }

        private string ExpandTemplateXml(
            string path,
            ExpandTemplateType type = ExpandTemplateType.Xml,
            bool sort = false, 
            string token = "<") {

            var actual = new Msbuild(ProjectFile, LoggerVerbosity.Minimal).Build(
                target: "ExpandTemplate",
                properties: new Dictionary<string, string>() {
                    ["TemplatePath"] = $"{path}",
                    ["TemplateType"] = $"{type}",
                    ["TemplateSort"] = $"{sort}",
                }
            );
            return actual.Substring(actual.IndexOf(token)).Trim();
        }

        [Test]
        public void DiffSame() {
            var properties = new Dictionary<string, string>() {
                ["SourceFiles"] = @"Diff\Source\Same.txt;Diff\Source\SameContent.txt;",
                ["TargetFolder"] = @"Diff\Target\",
            };

            var msbuild = new Msbuild(ProjectFile, LoggerVerbosity.Detailed);
            var actual = msbuild.Build("DiffFiles", properties);
        }

        [Test]
        public void DiffContent() {
            var properties = new Dictionary<string, string>() {
                ["SourceFiles"] = @"Diff\Source\SameContent.txt",
                ["TargetFiles"] = @"Diff\Target\SameContent.txt",
                ["IgnoreLastWriteTime"] = @"true",
            };

            var msbuild = new Msbuild(ProjectFile, LoggerVerbosity.Detailed);
            var actual = msbuild.Build("DiffFiles", properties);
        }
    }
}
