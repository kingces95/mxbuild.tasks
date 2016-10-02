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

        static MessageImportance DefaultMessageImportance = MessageImportance.High;

        public string Header { get; set; }
        public ITaskItem[] Items { get; set; }
        public bool ExcludeDefaultMetadata { get; set; }
        public bool ExcludeEmptyValues { get; set; }

        protected override void Run() {
            Log.LogMessage(DefaultMessageImportance, Header);

            var indent = "";
            if (Header != null)
                indent = "    ";

            if (Items == null)
                return;

            var sc = StringComparer.InvariantCultureIgnoreCase;

            var items = Items;
            foreach (var item in items) {
                Log.LogMessage(DefaultMessageImportance, $"{indent}{item.GetMetadata("Identity")}");

                foreach (string name in item.MetadataNames) {
                    if (ExcludeDefaultMetadata && DefaultMetadataNames.Contains(name))
                        continue;

                    var value = item.GetMetadata(name);
                    if (ExcludeEmptyValues && string.IsNullOrEmpty(value))
                        continue;

                    Log.LogMessage(DefaultMessageImportance, $"{indent}{indent}{name}: {value}");
                }
            }
        }
    }
}
