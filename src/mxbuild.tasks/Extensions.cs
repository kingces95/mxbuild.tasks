using System;
using System.IO;

namespace Mxbuild.Tasks {

    public static class Extensions {
        public static string NormalizeSlashes(this Uri path) {
            return path.ToString().NormalizeSlashes();
        }
        public static string NormalizeSlashes(this string path) {
            path = path.Replace('/', Path.DirectorySeparatorChar);
            path = path.Replace('\\', Path.DirectorySeparatorChar);
            return path;
        }
        public static string NormalizeDir(this string dir) {
            dir = dir.NormalizeSlashes();
            if (!dir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                dir = dir + Path.DirectorySeparatorChar;
            return dir;
        }
    }
}
