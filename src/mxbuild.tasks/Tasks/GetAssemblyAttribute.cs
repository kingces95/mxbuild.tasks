using System;
using System.Reflection;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Collections.Generic;
using System.IO;

namespace Mxbuild.Tasks {

    internal class Attribute {
        private AttributeInfo m_info;
        private Dictionary<string, AttributeValue> m_values;

        internal Attribute(CustomAttributeData data) {
            var ctor = data.Constructor;
            m_info = new AttributeInfo(ctor.DeclaringType.Name);

            m_values = data.ConstructorArguments.Zip(ctor.GetParameters(), (a, p) => new {
                Name = p.Name.ToUpperFirst(),
                Type = a.ArgumentType,
                Value = a.Value
            }).Concat(data.NamedArguments.Select(o => new {
                Name = o.MemberInfo.Name,
                Type = o.TypedValue.ArgumentType,
                Value = o.TypedValue.Value
            })).ToDictionary(o => o.Name, o =>
                new AttributeValue(new AttributeProperty(m_info, o.Name), this, o.Type, o.Value)
            );
        }

        public AttributeInfo Info => m_info;
        public string Name => m_info.Name;
        public IEnumerable<AttributeValue> Values() => m_values.Values;

        public override string ToString() => Name;
    }
    internal class AttributeInfo : IEquatable<AttributeInfo> {
        private string m_name;

        internal AttributeInfo(string name) {
            m_name = name;
        }

        public string Name => m_name;

        public override int GetHashCode() => m_name.ToLower().GetHashCode();
        public override bool Equals(object obj) => Equals(obj as AttributeInfo);
        public bool Equals(AttributeInfo other) {
            if (other == null)
                return false;

            return string.Equals(m_name, other.m_name, StringComparison.InvariantCultureIgnoreCase);
        }
        public override string ToString() => m_name;
    }
    internal class AttributeProperty : IEquatable<AttributeProperty> {
        private AttributeInfo m_info;
        private string m_name;

        internal AttributeProperty(AttributeInfo info, string name) {
            m_info = info;
            m_name = name;
        }

        public AttributeInfo Info => m_info;
        public string Name => m_name;

        public override string ToString() => $"{Info}.{Name}";
        public override int GetHashCode() => m_name.ToLower().GetHashCode() ^ m_info.GetHashCode();
        public bool Equals(AttributeProperty other) {
            if (!string.Equals(m_name, other.m_name, StringComparison.InvariantCultureIgnoreCase))
                return false;

            return m_info.Equals(other.m_info);
        }
        public override bool Equals(object obj) => obj is AttributeProperty ? Equals((AttributeProperty)obj) : false;
    }
    internal struct AttributeValue {
        private static string Create(Type type, object value) {
            if (type == typeof(object)) {
                if (value == null)
                    return null;

                return Create(value.GetType(), value);
            }

            if (type.IsEnum) {
                return (
                    from field in type.GetFields()
                    where field.IsLiteral
                    let literal = field.GetValue(null)
                    where literal.Equals(value)
                    select field.Name
                ).FirstOrDefault();
            }

            if (type.IsArray) {
                var array = (object[])value;
                var elementType = type.GetElementType();
                return string.Join(";", array.Select(o => Create(elementType, o)));
            }

            return value.ToString();
        }

        private Attribute m_attribute;
        private AttributeProperty m_property;
        private Type m_type;
        private string m_value;

        internal AttributeValue(AttributeProperty property, Attribute attribute, Type type, object value) {
            m_attribute = attribute;
            m_property = property;
            m_type = type;
            m_value = Create(type, value);
        }

        public AttributeProperty Property => m_property;
        public Attribute Attribute => m_attribute;
        public string Name => m_property.Name;
        public Type Type => m_type;
        public string Value => m_value;

        public override string ToString() => $"{Property}={Value}";
    }
    internal struct AttributeAlias {
        internal static IEnumerable<AttributeAlias> CreateMany(ITaskItem[] items) {
            foreach (var item in items) {
                var info = new AttributeInfo(item.GetMetadata("Identity"));

                foreach (string name in item.MetadataNames) {
                    if (string.Equals(name, "Identity", StringComparison.CurrentCultureIgnoreCase))
                        continue;

                    yield return new AttributeAlias(
                        new AttributeProperty(info, name),
                        item.GetMetadata(name)
                    );
                }
            }
        }

        private AttributeProperty m_property;
        private string m_alias;

        internal AttributeAlias(AttributeProperty property, string alias) {
            m_property = property;
            m_alias = alias;
        }

        public AttributeInfo Info => m_property.Info;
        public AttributeProperty Property => m_property;
        public bool HasAlias => !string.IsNullOrEmpty(Alias);
        public string Alias => m_alias;
        public string Name => HasAlias ? m_alias : m_property.Name;

        public override string ToString() => HasAlias ? $"{m_property} -> {Alias}" : $"m_info";
    }

    internal static class AttributeExtensions {
        static AttributeExtensions() {
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += ReflectionOnlyAssemblyResolve;
        }
        private static Assembly ReflectionOnlyAssemblyResolve(object sender, ResolveEventArgs args) {
            var name = args.Name;
            try { return Assembly.LoadFrom(name); } 
            catch { return null; }
        }

        private const string Identity = nameof(Identity);

        internal static Dictionary<string, string> ToDictionary(this ITaskItem item) {
            return item.MetadataNames.Cast<string>().ToDictionary(
                keySelector: name => name, 
                elementSelector: name => item.GetMetadata(name),
                comparer: StringComparer.InvariantCultureIgnoreCase
            );
        }
        internal static ITaskItem ToTaskItem(this Dictionary<string, string> dictionary) {
            string identity;
            if (!dictionary.TryGetValue(Identity, out identity))
                throw new Exception($"Cannot convert dictionary to TaskItem without identity.");

            dictionary = new Dictionary<string, string>(dictionary);
            dictionary.Remove(Identity);

            return new TaskItem(identity, dictionary);
        }
        internal static IEnumerable<Attribute> GetAssemblyAttributes(this ITaskItem item) {
            var path = item.GetMetadata(Identity);
            var assembly = Assembly.LoadFile(path);

            return assembly
                .GetCustomAttributesData().Select(o => new Attribute(o))
                .ToArray();
        }

        internal static void JoinAttributes(
            this ITaskItem[] assemblyItems,
            ITaskItem[] attributeItems,
            Action<ITaskItem, Attribute, string, string> action) {

            foreach (var o in
                from assembly in assemblyItems
                from attribute in assembly.GetAssemblyAttributes()
                from value in attribute.Values()
                join alias in AttributeAlias.CreateMany(attributeItems) 
                    on value.Property equals alias.Property
                select new {
                    Assembly = assembly,
                    Attrbute = attribute,
                    alias.Name,
                    value.Value
                }
            ) {
                action(o.Assembly, o.Attrbute, o.Name, o.Value);
            }
        }
    }

    /// <summary>
    /// Return a TaskItem for each assembly. Resulting TaskItems start off as clones of
    /// the assembly. The Attribute input contains a list of TaskItem whose identities
    /// are the name of attributes to find on the assemblies. Each metadata name is a 
    /// property on the attribute whose value will be added to the result with a name 
    /// equal to the value of the metadata.
    /// </summary>
    public sealed class AddAssemblyAttribute : AbstractTask {

        protected override void Run() {
            var results = new Dictionary<object, ITaskItem>();

            Assemblies.JoinAttributes(Attributes,
                (assembly, attribute, name, value) => {
                    ITaskItem result;
                    if (!results.TryGetValue(assembly, out result))
                        results[assembly] = result = new TaskItem(assembly);

                    result.SetMetadata(name, value);
                }
            );

            Result = results.Values.ToArray();
        }

        [Required]
        public ITaskItem[] Assemblies { get; set; }

        [Required]
        public ITaskItem[] Attributes { get; set; }

        [Output]
        public ITaskItem[] Result { get; set; }
    }

    /// <summary>
    /// Return a TaskItem for each attribute applied to to each assembly. Resulting
    /// TaskItems start off as clones of the assembly in which the attribute was discovered.
    /// The Attribute input contains a TaskItem whose identity is the attribute to find 
    /// and each metadata name is a property on the attribute whose value will be added 
    /// to the result with a name equal to the value of the metadata.
    /// </summary>
    public sealed class GetAssemblyAttribute : AbstractTask {

        protected override void Run() {
            var results = new Dictionary<object, Dictionary<string, string>>();

            if (Attribute.Length != 1)
                throw new Exception($"Expected a single attribute but received {Attribute.Length}.");

            Assemblies.JoinAttributes(Attribute, (assembly, attribute, name, value) => {

                // try get existing result to which to add name/value
                Dictionary<string, string> result;
                if (!results.TryGetValue(attribute, out result))
                    // add name/value pairs to cloned assembly
                    results[attribute] = result = assembly.ToDictionary();

                result[name] = value;
            });

            Result = results.Values.Select(o => o.ToTaskItem()).ToArray();
        }


        [Required]
        public ITaskItem[] Assemblies { get; set; }

        [Required]
        public ITaskItem[] Attribute { get; set; }

        [Output]
        public ITaskItem[] Result { get; set; }
    }
}
