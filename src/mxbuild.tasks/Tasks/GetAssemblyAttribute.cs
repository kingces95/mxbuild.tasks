using System;
using System.Reflection;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using Mono.Cecil;
using System.Text.RegularExpressions;
using Microsoft.Build.Execution;

namespace Mxbuild.Tasks {

    internal class Attribute {
        private AttributeInfo m_info;
        private Dictionary<string, AttributeValue> m_values;

        internal Attribute(CustomAttribute data) {
            var ctor = data.Constructor;
            m_info = new AttributeInfo(ctor.DeclaringType.Name);

            m_values = data.ConstructorArguments.Zip(ctor.Parameters, (a, p) => new {
                Name = p.Name.ToUpperFirst(),
                Type = a.Type,
                Value = a.Value
            }).Concat(data.Properties.Select(o => new {
                Name = o.Name,
                Type = o.Argument.Type,
                Value = o.Argument.Value
            })).Concat(data.Fields.Select(o => new {
                Name = o.Name,
                Type = o.Argument.Type,
                Value = o.Argument.Value
            })).ToDictionary(o => o.Name, o =>
                new AttributeValue(new AttributeProperty(m_info, o.Name), this, Type.GetType(o.Type.FullName), o.Value)
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
    internal struct TaskItemValueTemplate {
        private const string IdentityName = "Identity";

        internal static IEnumerable<TaskItemValueTemplate> CreateMany(ITaskItem[] items, string identityFormat = null) {
            foreach (var item in items) {
                var attribute = new AttributeInfo(item.GetMetadata("Identity"));

                if (identityFormat != null)
                    yield return new TaskItemValueTemplate(attribute, IdentityName, identityFormat);

                foreach (string name in item.CloneCustomMetadata().Keys) {
                    if (string.Equals(name, "Identity", StringComparison.CurrentCultureIgnoreCase))
                        continue;

                    var format = item.GetMetadata(name);
                    yield return new TaskItemValueTemplate(
                        attribute,
                        name,
                        format
                    );
                }
            }
        }

        private AttributeInfo m_attribute;
        private string m_name;
        private string m_value;

        internal TaskItemValueTemplate(AttributeInfo attribute, string name, string value) {
            m_attribute = attribute;
            m_name = name;
            m_value = value;
        }

        public AttributeInfo AttributeInfo => m_attribute;
        public string Value => m_value;
        public string Name => m_name;

        public string Expand(ITaskItem context, Attribute attribute) {
            var value = m_value;
            foreach (var pair in attribute.Values())
                value = Regex.Replace(value, $"[$]{{{pair.Name}}}", pair.Value, RegexOptions.IgnoreCase);
            foreach (var pair in context.MetadataNames.Cast<string>().Select(o => new { Name = o, Value = context.GetMetadata(o) }))
                value = Regex.Replace(value, $"%{{{pair.Name}}}", pair.Value, RegexOptions.IgnoreCase);
            return value;
        }

        public override string ToString() => $"{m_attribute}.{m_name} -> {m_value}";
    }

    internal static class AttributeExtensions {

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
            if (!dictionary.TryGetValue(Identity, out identity) || string.IsNullOrEmpty(identity))
                throw new Exception($"Cannot convert dictionary to TaskItem without 'Identity'.");

            dictionary = new Dictionary<string, string>(dictionary);
            dictionary.Remove(Identity);

            return new TaskItem(identity, dictionary);
        }
        internal static IEnumerable<Attribute> GetAssemblyAttributes(this ITaskItem item, HashSet<AttributeInfo> attributeInfos) {
            var path = item.GetMetadata(Identity);

            var assemblyDef = AssemblyDefinition.ReadAssembly(path);

            var result = (
                from attributeInfo in attributeInfos
                from attribute in assemblyDef.CustomAttributes
                where attributeInfos.Contains(new AttributeInfo(attribute.AttributeType.Name))
                select new Attribute(attribute)
            ).ToList();

            return result;
        }

        internal static void JoinAttributes(
            this ITaskItem[] assemblyItems,
            ITaskItem[] attributeItems,
            Action<ITaskItem, Attribute, TaskItemValueTemplate> action,
            string identityFormat = null) {

            var formats = TaskItemValueTemplate.CreateMany(attributeItems, identityFormat).ToList();
            var attributeInfos = new HashSet<AttributeInfo>(formats.GroupBy(o => o.AttributeInfo).Select(o => o.Key));

            foreach (var o in
                from assembly in assemblyItems
                from attribute in assembly.GetAssemblyAttributes(attributeInfos)
                join format in formats on attribute.Info equals format.AttributeInfo
                select new {
                    Assembly = assembly,
                    Attrbute = attribute,
                    Format = format,
                }
            ) {
                action(o.Assembly, o.Attrbute, o.Format);
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
            var results = Assemblies.ToDictionary(o => o, o => (ITaskItem)new TaskItem(o));

            Assemblies.JoinAttributes(Attributes,
                (assembly, attribute, template) => {
                    ITaskItem result;
                    if (!results.TryGetValue(assembly, out result))
                        results[assembly] = result = new TaskItem(assembly);

                    result.SetMetadata(template.Name, template.Expand(assembly, attribute));
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
            var results = new Dictionary<Attribute, Dictionary<string, string>>();

            if (Attribute.Length != 1)
                throw new Exception($"Expected a single attribute but received {Attribute.Length}.");

            Assemblies.JoinAttributes(Attribute, (assembly, attribute, template) => {

                // try get existing assembly clone to which to add/update values
                Dictionary<string, string> item;
                if (!results.TryGetValue(attribute, out item))
                    // clone assembly
                    results[attribute] = item = assembly.ToDictionary();

                item[template.Name] = template.Expand(assembly, attribute);
            }, Identity);

            Result = results.Values.Select(o => o.ToTaskItem()).ToArray();
        }

        [Required]
        public string Identity { get; set; }

        [Required]
        public ITaskItem[] Assemblies { get; set; }

        [Required]
        public ITaskItem[] Attribute { get; set; }

        [Output]
        public ITaskItem[] Result { get; set; }
    }
}
