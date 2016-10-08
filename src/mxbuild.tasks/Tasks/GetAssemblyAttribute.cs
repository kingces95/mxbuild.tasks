using System;
using System.Reflection;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Mxbuild.Tasks {

    public sealed class GetAssemblyAttribute : AbstractTask {

        protected override void Run() {
            Result = (
                from attr in Assembly.ReflectionOnlyLoadFrom(AssemblyPath).GetCustomAttributesData()
                let ctor = attr.Constructor
                where string.Equals(ctor.DeclaringType.Name, AttributeName, StringComparison.InvariantCultureIgnoreCase)
                select new TaskItem(attr.ToString(), (
                    from pair in attr.ConstructorArguments.Zip(ctor.GetParameters(), (a, p) => new {
                        Name = p.Name.ToUpperFirst(),
                        Type = a.ArgumentType,
                        Value = a.Value
                    }).Concat(attr.NamedArguments.Select(o => new {
                        Name = o.MemberInfo.Name,
                        Type = o.TypedValue.ArgumentType,
                        Value = o.TypedValue.Value
                    })) select pair
                ).ToDictionary(o => o.Name, o => Create(o.Type, o.Value)))
            ).ToArray();
        }

        private string Create(Type type, object value) {
            if (type.IsEnum)
                throw new Exception($"Attribute value type '{type}' is not supported.");

            if (type.IsArray)
                throw new Exception($"Attribute value type '{type}' is not supported.");

            return value.ToString();
        }

        [Required]
        public string AssemblyPath { get; set; }

        [Required]
        public string AttributeName { get; set; }

        [Output]
        public ITaskItem[] Result { get; set; }
    }
}
