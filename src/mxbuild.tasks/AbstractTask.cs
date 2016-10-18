using System;
using Microsoft.Build.Utilities;
using System.Diagnostics;

namespace Mxbuild.Tasks {

    public abstract class AbstractTask : Task {
        protected abstract void Run();

        public override bool Execute() {
            try {
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
                Run();
            } catch (Exception e) {
                Log.LogError(e.ToString());
            }

            return !Log.HasLoggedErrors;
        }
    }
}
