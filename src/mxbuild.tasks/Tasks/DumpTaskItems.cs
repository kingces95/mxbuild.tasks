using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;
using System.IO;
using System.Xml.Linq;
using System.Diagnostics;

namespace Mxbuild.Tasks {
    public sealed class DumpTaskItems : AbstractTask {
        private const string DefaultMessageImportance = "high";
        private const string Indent = "  ";

        public static string[] DefaultMetadataNames = new[] {
            "FullPath",
            "RootDir",
            "Filename",
            "Extension",
            "RelativeDir",
            "RecursiveDir",
            "Identity",
            "ModifiedTime",
            "CreatedTime",
            "AccessedTime",
            "Directory",
            "DefiningProjectFullPath",
            "DefiningProjectDirectory",
            "DefiningProjectName",
            "DefiningProjectExtension",
            "OriginalItemSpec",
            "MSBuildSourceProjectFile",
            "MSBuildSourceTargetName",
        };

        public string Header { get; set; }
        public ITaskItem[] Items { get; set; }
        public bool ExcludeDefaultMetadata { get; set; }
        public bool ExcludeEmptyValues { get; set; }
        public string Importance { get; set; }

        protected override void Run() {
            if (Importance == null)
                Importance = DefaultMessageImportance;
            var importance = (MessageImportance)Enum.Parse(typeof(MessageImportance), Importance, ignoreCase: true);

            if (Header == null)
                Header = string.Empty;

            Log.LogMessage(importance, Header);

            var indent = "";
            if (!string.IsNullOrEmpty(Header))
                indent = Indent;

            if (Items == null)
                return;

            var sc = StringComparer.InvariantCultureIgnoreCase;

            var items = Items;
            foreach (var item in items) {
                Log.LogMessage(importance, $"{indent}{item.GetMetadata("Identity")}");

                foreach (string name in item.MetadataNames) {
                    if (ExcludeDefaultMetadata && DefaultMetadataNames.Contains(name))
                        continue;

                    var value = item.GetMetadata(name);
                    if (ExcludeEmptyValues && string.IsNullOrEmpty(value))
                        continue;

                    Log.LogMessage(importance, $"{indent}{Indent}{name}: {value}");
                }
            }
        }
    }
}
