using System;
using Microsoft.Build.Utilities;
using System.Diagnostics;

namespace Mxbuild.Tasks {

    public abstract class AbstractTask : Task {
        public static void BreakOnType(Type type) {
            var envVar = Environment.GetEnvironmentVariable("MXBUILD_BREAK");
            if (string.IsNullOrEmpty(envVar))
                return;

            var name = type.Name;
            if (string.Compare(name, envVar, ignoreCase: true) != 0)
                return;

            Debugger.Launch();
        }

        protected abstract void Run();

        public override bool Execute() {
            try {
                AbstractTask.BreakOnType(GetType());
                Run();
            } catch (Exception e) {
                Log.LogError(e.ToString());
            }

            return !Log.HasLoggedErrors;
        }
    }
    public abstract class AppDomainIsolatedAbstractTask : AppDomainIsolatedTask {
        protected abstract void Run();

        public override bool Execute() {
            try {
                AbstractTask.BreakOnType(GetType());
                Run();
            } catch (Exception e) {
                Log.LogError(e.ToString());
            }

            return !Log.HasLoggedErrors;
        }
    }
}
