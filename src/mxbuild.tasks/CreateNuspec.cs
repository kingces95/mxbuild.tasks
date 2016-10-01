﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;
using System.IO;
using System.Xml.Linq;
using System.Diagnostics;

namespace Xamarin.Forms.Build {
    public sealed class CreateNuspec : AbstractTask {
        private const string BuildTargetFramework = "build";
        private const string LibSubDir = "lib";
        private const string LibraryExtension = ".dll";

        private static MessageImportance DefaultMessageImportance = MessageImportance.Normal;

        private static class Meta {
            internal static string Version = nameof(Version);
            internal static string TargetFramework = nameof(TargetFramework);
            internal static string Extension = nameof(Extension);
        }
        private static class X {
            internal static XName Package = (CammelCase)nameof(Package);
            internal static XName Metadata = (CammelCase)nameof(Metadata);
            internal static XName Id = (CammelCase)nameof(Id);
            internal static XName Version = (CammelCase)nameof(Version);
            internal static XName Authors = (CammelCase)nameof(Authors);
            internal static XName Owners = (CammelCase)nameof(Owners);
            internal static XName Tags = (CammelCase)nameof(Tags);
            internal static XName LicenseUrl = (CammelCase)nameof(LicenseUrl);
            internal static XName IconUrl = (CammelCase)nameof(IconUrl);
            internal static XName ProjectUrl = (CammelCase)nameof(ProjectUrl);
            internal static XName RequireLicenseAcceptance = (CammelCase)nameof(RequireLicenseAcceptance);
            internal static XName Description = (CammelCase)nameof(Description);
            internal static XName Copyright = (CammelCase)nameof(Copyright);
            internal static XName Dependencies = (CammelCase)nameof(Dependencies);
            internal static XName Group = (CammelCase)nameof(Group);
            internal static XName Dependency = (CammelCase)nameof(Dependency);
            internal static XName References = (CammelCase)nameof(References);
            internal static XName Reference = (CammelCase)nameof(Reference);
            internal static XName Files = (CammelCase)nameof(Files);
            internal static XName File = (CammelCase)nameof(File);
            internal static XName TargetFramework = (CammelCase)nameof(TargetFramework);
            internal static XName Src = (CammelCase)nameof(Src);
            internal static XName Target = (CammelCase)nameof(Target);
            internal static XName ReleaseNotes = (CammelCase)nameof(ReleaseNotes);
        }

        public string Id { get; set; }
        public string Authors { get; set; }
        public string Version { get; set; }
        public string Owners { get; set; }
        public string Tags { get; set; }
        public string LicenseUrl { get; set; }
        public string IconUrl { get; set; }
        public string ProjectUrl { get; set; }
        public bool RequireLicenseAcceptance { get; set; }
        public string Description { get; set; }
        public string Copyright { get; set; }
        public string ReleaseNotes { get; set; }

        [Required]
        public string OutputPath { get; set; }

        public ITaskItem[] References { get; set; }
        public ITaskItem[] Dependencies { get; set; }

        protected override void Run() {
            var outputUir = new Uri(OutputPath);
            //Debugger.Break();

            if (Dependencies == null)
                Dependencies = new ITaskItem[0];

            if (References == null)
                References = new ITaskItem[0];

            if (ReleaseNotes == null)
                ReleaseNotes = string.Empty;

            var package = new XElement(X.Package,
                new XElement(X.Metadata,
                    new XElement(X.Id, Id),
                    new XElement(X.Version, Version),
                    new XElement(X.Description, Description),
                    new XElement(X.ReleaseNotes, new XCData(ReleaseNotes)),
                    new XElement(X.Authors, Authors),
                    new XElement(X.Owners, Owners),
                    new XElement(X.Copyright, Copyright),
                    new XElement(X.Tags, Tags),
                    new XElement(X.LicenseUrl, LicenseUrl),
                    new XElement(X.IconUrl, IconUrl),
                    new XElement(X.ProjectUrl, ProjectUrl),
                    new XElement(X.RequireLicenseAcceptance, RequireLicenseAcceptance),

                    new XElement(X.Dependencies,
                        from dependency in Dependencies
                        let targetFramework = dependency.GetMetadata(Meta.TargetFramework)
                        group dependency by targetFramework into grp
                        select new XElement(X.Group,
                            string.IsNullOrEmpty(grp.Key) ? null : new XAttribute(X.TargetFramework, grp.Key),
                            from member in grp
                            select new XElement(X.Dependency,
                                new XAttribute(X.Id, member.ItemSpec),
                                new XAttribute(X.Version, member.GetMetadata(Meta.Version))
                            )
                        )
                    ),

                    new XElement(X.References,
                        from reference in References
                        let targetFramework = reference.GetMetadata(Meta.TargetFramework)
                        where targetFramework != BuildTargetFramework
                        group reference by targetFramework into grp
                        select new XElement(X.Group,
                            string.IsNullOrEmpty(grp.Key) ? null : new XAttribute(X.TargetFramework, grp.Key),
                            from member in grp
                            select new XElement(X.Reference,
                                new XAttribute(X.File, Path.GetFileName(member.ItemSpec))
                            )
                        )
                    )
                ),
                new XElement(X.Files,
                    from reference in References
                    let targetFramework = reference.GetMetadata(Meta.TargetFramework)
                    let isBuild = targetFramework != BuildTargetFramework
                    let source = outputUir.MakeRelativeUri(new Uri(reference.ItemSpec)).NormalizeSlashes()
                    let target = GetNuspecTargetPath(outputUir, reference)
                    select new XElement(X.File,
                        new XAttribute(X.Src, source),
                        new XAttribute(X.Target, target)
                    )
                )
           );

            var fileName = Path.GetFileName(OutputPath);
            Log.LogMessage(DefaultMessageImportance, $"{fileName} -> {OutputPath}");

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));

            package.Save(OutputPath);
        }

        private IEnumerable<ITaskItem> LibraryReferences() {
            return References.Where(o => 
                string.Compare(o.GetMetadata(Meta.Extension), LibraryExtension, ignoreCase: true) == 0
            );
        }

        private string GetNuspecTargetPath(Uri outDir, ITaskItem reference) {
            var nuspecPath = outDir.MakeRelativeUri(new Uri(reference.ItemSpec)).NormalizeSlashes();

            var targetFramework = reference.GetMetadata(Meta.TargetFramework);
            var isBuild = targetFramework == BuildTargetFramework;
            if (!isBuild)
                nuspecPath = Path.Combine(LibSubDir, nuspecPath);

            return nuspecPath;
        }
    }
}
