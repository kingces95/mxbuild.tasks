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

    public sealed class Diff : AbstractTask {

        protected override void Run() {
            if (TargetFiles != null && TargetFolder != null)
                throw new Exception(
                    $"Both '{nameof(TargetFiles)}' and '{nameof(TargetFolder)}' cannot be supplied.");

            if (TargetFiles == null && TargetFolder == null)
                throw new Exception(
                    $"Either '{nameof(TargetFiles)}' or '{nameof(TargetFolder)}' must be supplied.");

            LogLow($"{nameof(IgnoreAttributes)}: {IgnoreAttributes}");
            LogLow($"{nameof(IgnoreLastWriteTime)}: {IgnoreLastWriteTime}");
            LogLow($"{nameof(IgnoreContent)}: {IgnoreContent}");

            if (TargetFolder != null)
                TargetFiles = SourceFiles.Select(o => GetTargetFile(o)).ToArray();

            var differentSourceFiles = new List<ITaskItem>();
            var differentTargetFiles = new List<ITaskItem>();
            var sameSourceFiles = new List<ITaskItem>();
            var sameTargetFiles = new List<ITaskItem>();

            var zip = SourceFiles.Zip(TargetFiles, (s, t) => new { Source = s, Target = t });
            foreach (var pair in zip) {
                if (Equals(pair.Source, pair.Target)) {
                    LogLow($"{pair.Source} == {pair.Target}");
                    sameSourceFiles.Add(pair.Source);
                    sameTargetFiles.Add(pair.Target);
                    continue;
                }

                differentSourceFiles.Add(pair.Source);
                differentTargetFiles.Add(pair.Target);
                IsDifferent = true;
            }

            DifferentSourceFiles = differentSourceFiles.ToArray();
            DifferentTargetFiles = differentTargetFiles.ToArray();
            SameSourceFiles = sameSourceFiles.ToArray();
            SameTargetFiles = sameTargetFiles.ToArray();
        }

        private void LogLow(string message) => Log.LogMessage(MessageImportance.Low, message);
        private void LogDifference(string source, string target, string valueName, object sourceValue, object targetValue) {
            LogLow($"{source} != {target}, {valueName}: '{sourceValue}' != '{targetValue}'");
        }
        private bool Equals(ITaskItem sourceItem, ITaskItem targetItem) {
            var source = sourceItem.ItemSpec;
            var target = targetItem.ItemSpec;

            if (!File.Exists(source))
                throw new Exception($"Source file '{source}' does not exist.");

            if (!File.Exists(target)) {
                LogLow($"{source} != {target}, Target does not exist");
                return false;
            }

            var sourceInfo = new FileInfo(source);
            var targetInfo = new FileInfo(target);

            if (!IgnoreAttributes && sourceInfo.Attributes != targetInfo.Attributes) {
                LogDifference(source, target, "attributes", sourceInfo.Attributes, targetInfo.Attributes);
                return false;
            }

            if (!IgnoreLastWriteTime && !sourceInfo.LastWriteTimeUtc.Equals(targetInfo.LastWriteTimeUtc)) {
                LogDifference(source, target, "last write time (utc)",
                    $"{sourceInfo.LastWriteTimeUtc}, {sourceInfo.LastWriteTimeUtc.Millisecond}",
                    $"{targetInfo.LastWriteTimeUtc}, {targetInfo.LastWriteTimeUtc.Millisecond}"
                );
                return false;
            }

            if (sourceInfo.Length != targetInfo.Length) {
                LogDifference(source, target, "length", sourceInfo.Length, targetInfo.Length);
                return false;
            }

            if (!IgnoreContent) {
                using (var sourceContent = File.OpenRead(source)) {
                    using (var targetContent = File.OpenRead(target)) {
                        var length = sourceContent.Length;
                        for (int i = 0; i < length; i++) {
                            var sourceByte = sourceContent.ReadByte();
                            var targetByte = targetContent.ReadByte();
                            if (sourceByte != targetByte) {
                                LogDifference(source, target, $"content at {i} (byte)", sourceByte, targetByte);
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }
        private TaskItem GetTargetFile(ITaskItem sourceFile) {
            var recursiveDir = sourceFile.GetMetadata("RecursiveDir");
            var fileName = sourceFile.GetMetadata("FileName") + sourceFile.GetMetadata("Extension");
            var targetPath = Path.Combine(TargetFolder.ItemSpec,
                Path.Combine(recursiveDir, fileName)
            );

            return new TaskItem(targetPath);
        }

        [Output]
        public ITaskItem[] DifferentSourceFiles { get; set; }

        [Output]
        public ITaskItem[] DifferentTargetFiles { get; set; }

        [Output]
        public ITaskItem[] SameSourceFiles { get; set; }

        [Output]
        public ITaskItem[] SameTargetFiles { get; set; }

        [Output]
        public bool IsDifferent { get; set; }

        public ITaskItem[] SourceFiles { get; set; }
        public ITaskItem[] TargetFiles { get; set; }
        public ITaskItem TargetFolder { get; set; }
        public bool IgnoreAttributes { get; set; }
        public bool IgnoreLastWriteTime { get; set; }
        public bool IgnoreContent { get; set; }
    }
}
