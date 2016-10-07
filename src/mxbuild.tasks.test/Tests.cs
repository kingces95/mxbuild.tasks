using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using NUnit.Framework;

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
        public void ExpandTemplateLineByLine() {
            var actual = new Msbuild(ProjectFile, LoggerVerbosity.Minimal).Build(nameof(ExpandTemplateLineByLine));
            actual = actual.Substring(actual.IndexOf("Items0:")).Trim();
            var expected = File.ReadAllText("template.expected.txt");
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void ExpandTemplateXml() {
            var actual = new Msbuild(ProjectFile, LoggerVerbosity.Minimal).Build(nameof(ExpandTemplateXml));
            actual = actual.Substring(actual.IndexOf("<")).Trim();
            var expected = File.ReadAllText("template.expected.xml");
            Assert.AreEqual(expected, actual);
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
