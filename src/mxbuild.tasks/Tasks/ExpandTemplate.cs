using System.Linq;
using Microsoft.Build.Framework;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Xml;
using System;

namespace Mxbuild.Tasks {
    using TaskItemGroup = IGrouping<GroupingTuple, ITaskItem>;

    internal sealed class GroupingTuple : 
        IComparable<GroupingTuple>,
        IEquatable<GroupingTuple> {

        internal static readonly GroupingTuple Empty = new GroupingTuple(false);
        internal static readonly GroupingTuple EmptyCaseInsensitive = new GroupingTuple(true);

        private interface IDimension : IComparable, IEquatable<IDimension> {
            object Value { get; }
        }
        private struct Dimension<T> :
            IDimension, 
            IEquatable<Dimension<T>>,
            IComparable<Dimension<T>> {

            private string m_name;
            private IEqualityComparer<string> m_keyEqualityComparer;

            private T m_value;
            private IComparer<T> m_valueComparer;
            private IEqualityComparer<T> m_valueEqualityComparer;

            public Dimension(
                T value, 
                string name,
                IEqualityComparer<string> keyEqualityComparer,
                IComparer<T> valueComparer, 
                IEqualityComparer<T> valueEqualityComparer) {

                m_value = value;
                m_name = name;
                m_keyEqualityComparer = keyEqualityComparer;
                m_valueComparer = valueComparer;
                m_valueEqualityComparer = valueEqualityComparer;
            }

            public object Value => m_value;

            public int CompareTo(Dimension<T> other) {
                if (other.m_keyEqualityComparer != m_keyEqualityComparer)
                    throw new InvalidOperationException("Key equality comparerer mismatch.");

                if (other.m_valueEqualityComparer != m_valueEqualityComparer)
                    throw new InvalidOperationException("Value equality comparer mismatch.");

                if (other.m_valueComparer != m_valueComparer)
                    throw new InvalidOperationException("Value comparer mismatch.");

                if (!m_keyEqualityComparer.Equals(m_name, other.m_name))
                    throw new InvalidOperationException($"Key mismatch; Expected '{m_name}'=='{other.m_name}'.");

                return m_valueComparer.Compare(m_value, other.m_value);
            }
            public int CompareTo(object obj) {
                if (!(obj is Dimension<T>))
                    throw new InvalidOperationException();

                return CompareTo((Dimension<T>)obj);
            }
            public override bool Equals(object obj) => obj is Dimension<T> ? Equals((Dimension<T>)obj) : false;
            public bool Equals(Dimension<T> other) => CompareTo(other) == 0;
            public bool Equals(IDimension other) {
                if (!(other is Dimension<T>))
                    return false;

                return Equals((Dimension<T>)other);
            }
            public override int GetHashCode() => m_valueEqualityComparer.GetHashCode(m_value);
            public override string ToString() => $"{m_name}={m_value}";
        }

        private List<IDimension> m_dimensions;
        private Dictionary<string, int> m_dimensionByName;

        private GroupingTuple() { }
        private GroupingTuple(bool caseInsensitive) {
            m_dimensions = new List<IDimension>();
            m_dimensionByName = new Dictionary<string, int>(
                caseInsensitive ?
                    (IEqualityComparer<string>)StringComparer.InvariantCultureIgnoreCase :
                    EqualityComparer<string>.Default
            );
        }

        public GroupingTuple AddDimension(
            string value,
            string name,
            bool caseInsensitive = false) {

            var valueComparer = caseInsensitive ?
                StringComparer.InvariantCultureIgnoreCase :
                StringComparer.InvariantCulture;

            var valueEqualityComparer = caseInsensitive ?
                (IEqualityComparer<string>)StringComparer.InvariantCultureIgnoreCase :
                EqualityComparer<string>.Default;

            return AddDimension(value, name, valueComparer, valueEqualityComparer);
        }
        public GroupingTuple AddDimension<T>(
            T value, 
            string name, 
            IComparer<T> valueComparer = null,
            IEqualityComparer<T> valueEqualityComparer = null) {

            var result = new GroupingTuple {
                m_dimensions = new List<IDimension>(m_dimensions),
                m_dimensionByName = new Dictionary<string, int>(
                    m_dimensionByName, 
                    m_dimensionByName.Comparer
                )
            };

            if (result.m_dimensionByName.ContainsKey(name))
                throw new Exception($"Tuple already contains dimension named '{name}'.");

            result.m_dimensionByName[name] = result.m_dimensions.Count;
            result.m_dimensions.Add(
                new Dimension<T>(
                    value, 
                    name, 
                    m_dimensionByName.Comparer, 
                    valueComparer ?? Comparer<T>.Default,
                    valueEqualityComparer ?? EqualityComparer<T>.Default
                )
            );

            return result;
        }
        public int Count => m_dimensions.Count;
        public bool ContainsKey(string name) => m_dimensionByName.ContainsKey(name);
        public object this[int index] => m_dimensions[index]?.Value;
        public object this[string name] => this[m_dimensionByName[name]];

        public int CompareTo(GroupingTuple other) {
            var zip = m_dimensions.Zip(other.m_dimensions, (X, Y) => new { X, Y });
            foreach (var pair in zip) {
                var x = pair.X;
                var y = pair.Y;

                var result = x.CompareTo(y);
                if (result != 0)
                    return result;
            }

            if (Count == other.Count)
                return 0;

            return Count < other.Count ? -1 : 1;
        }
        public bool Equals(GroupingTuple other) {
            if (other == null)
                return false;

            return CompareTo(other) == 0;
        }
        public override bool Equals(object obj) => Equals(obj as GroupingTuple);
        public override int GetHashCode() => m_dimensions.Aggregate(0, (a, o) => a ^ o.GetHashCode());
        public override string ToString() {
            return $"<{string.Join(",", m_dimensions)}>";
        }
    }

    public enum ExpandTemplateType {
        Xml,
        LineByLine,
    }

    public sealed class ExpandTemplate : AbstractTask {
        private const string Name = "__Name";

        protected override void Run() {
            var items = Items.ToLookup(o => o.GetMetadata(Name), StringComparer.InvariantCultureIgnoreCase);

            var type = (ExpandTemplateType)Enum.Parse(typeof(ExpandTemplateType), Type, ignoreCase: true);

            if (type == ExpandTemplateType.Xml)
                Result = new ExpandTemplateXml(items, Sort).Run(Input);

            else if (type == ExpandTemplateType.LineByLine)
                Result = new ExpandTemplateLineByLine(items, Sort).Run(Input);

            else
                throw new Exception($"Type '{Type}' is unrecognized.");
        }

        [Required]
        public string Input { get; set; }

        [Required]
        public string Type { get; set; }

        [Required]
        public ITaskItem[] Items { get; set; }

        public bool Sort { get; set; }

        [Output]
        public string Result { get; set; }
    }

    internal abstract class ExpandTemplateBase {
        private const string ItemMetadataRegex = @"%[(](?<first>\w*)([.](?<second>\w*))?[)]";
        private struct RegexMatch {
            internal readonly string First;
            internal readonly string Second;

            internal RegexMatch(Match match) {
                First = match.Groups["first"].Value;
                Second = match.Groups["second"].Value;

                if (string.IsNullOrEmpty(Second))
                    Second = null;
            }

            public override string ToString() {
                if (Second == null)
                    return $"{First}";

                return $"{First}.{Second}";
            }
        }

        private static TaskItemGroup[] DefaultGroup = new ITaskItem[] { null }.GroupBy(o => GroupingTuple.EmptyCaseInsensitive).ToArray();
        internal static TaskItemGroup DefaultGroupSingleton = DefaultGroup.Single();

        private ILookup<string, ITaskItem> m_itemsByName;
        private bool m_sort;

        internal ExpandTemplateBase(ILookup<string, ITaskItem> itemsByName, bool sort) {
            m_itemsByName = itemsByName;
            m_sort = sort;
        }

        internal string Expand(GroupingTuple tuple, string input) {
            return Regex.Replace(input, ItemMetadataRegex, o => {
                var match = new RegexMatch(o);

                var name = match.Second ?? match.First;
                var value = tuple[name];

                return $"{value}";
            });
        }
        internal IEnumerable<TaskItemGroup> Group(TaskItemGroup grouping, IEnumerable<string> inputs) {

            // find all %(itemGroup.metadata) and %(metadata) patterns
            var matches = (
                from input in inputs
                from Match match in Regex.Matches(input, ItemMetadataRegex)
                select new RegexMatch(match)
            ).ToList();

            // any patterns found?
            if (matches.Count == 0)
                return DefaultGroup;

            // group items in grouping into sub groups by similar metadata values
            if (grouping != DefaultGroupSingleton) {

                var group = grouping.GroupBy(item => {
                    var tuple = grouping.Key;
                    foreach (var match in matches) {
                        var metadataName = match.Second ?? match.First;
                        var metadataValue = item.GetMetadata(metadataName);
                        tuple = tuple.AddDimension(metadataValue, metadataName, caseInsensitive: true);
                    }
                    return tuple;
                });

                if (m_sort)
                    group = group.OrderBy(o => o.Key);

                return group.ToList();
            }

            // more than one ItemGroup name specified? e.g. "%(name0.metadata) %(name1.metadata)"
            var groups = matches.Where(o => o.Second != null).GroupBy(o => o.First).ToList();
            if (groups.Count == 0)
                throw new Exception(
                    $"No ItemGroup specified (e.g. \"%(name.metadata)\"). Only the following metadata names: {string.Join(", ", matches.Select(o => $"%({o.First})"))}");
            if (groups.Count > 1)
                throw new Exception(
                    $"Unexpected nesting of ItemGroup names: {string.Join(" > ", groups.Select(o => o.Key))}");
            var itemGroupName = groups.Single().Key;

            // named ItemGroup exists?
            if (!m_itemsByName.Contains(itemGroupName))
                return Enumerable.Empty<TaskItemGroup>();
            var items = m_itemsByName[itemGroupName];

            // group items in ItemGroup by similar metadata values
            return Group(
                grouping: items.GroupBy(o => GroupingTuple.EmptyCaseInsensitive).Single(),
                inputs: inputs
            );
        } 
    }

    internal sealed class ExpandTemplateLineByLine : ExpandTemplateBase {

        internal ExpandTemplateLineByLine(ILookup<string, ITaskItem> items, bool sort) : base(items, sort) { }

        internal string Run(string input) {
            var lines =
                from line in Regex.Split(input, Environment.NewLine)
                from grp in Group(DefaultGroupSingleton, new[] { line })
                select Expand(grp?.Key, (string)line);

            return string.Join(Environment.NewLine, lines.ToArray());
        }
    }

    internal sealed class ExpandTemplateXml : ExpandTemplateBase {

        internal ExpandTemplateXml(ILookup<string, ITaskItem> items, bool sort) : base(items, sort) { }

        internal string Run(string input) {
            var doc = Expand(DefaultGroupSingleton, XDocument.Parse(input));
            return doc.Declaration.ToString() + Environment.NewLine + doc.ToString();
        }

        private object Expand(TaskItemGroup items, XObject node) {
            var type = node.NodeType;

            switch (type) {
                case XmlNodeType.Element:
                    return Expand(items, (XElement)node);

                case XmlNodeType.Attribute:
                    return Expand(items, (XAttribute)node);

                case XmlNodeType.Document:
                    return Expand(items, (XDocument)node);

                case XmlNodeType.Text:
                    return Expand(items, (XText)node);

                case XmlNodeType.CDATA:
                    return Expand(items, (XCData)node);

                case XmlNodeType.Comment:
                    return Expand(items, (XComment)node);

                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    return node;

                case XmlNodeType.EntityReference:
                case XmlNodeType.Entity:
                case XmlNodeType.ProcessingInstruction:
                case XmlNodeType.DocumentType:
                case XmlNodeType.DocumentFragment:
                case XmlNodeType.Notation:
                case XmlNodeType.XmlDeclaration:
                case XmlNodeType.EndElement:
                case XmlNodeType.EndEntity:
                default:
                    throw new Exception(
                        $"Unexpected node type '{type}' encountered during expansion.");
            }
        }
        private IEnumerable<string> ElementText(XElement element) {
            foreach (var attribute in element.Attributes())
                yield return attribute.Value;

            foreach (var text in element.Nodes().OfType<XText>())
                yield return text.Value;

            foreach (var comment in element.Nodes().OfType<XComment>())
                yield return comment.Value;
        }
        private IEnumerable<object> Expand(TaskItemGroup items, XElement element) {
            return Group(items, ElementText(element)).Select(group => 
                new XElement(element.Name,
                    new object[] {
                        Expand(group, element.Attributes()),
                        Expand(group, element.Nodes())
                    }
                )
            );
        }
        private object Expand(TaskItemGroup items, IEnumerable<XObject> nodes) {
            return nodes.Select(o => Expand(items, o));
        }
        private XDocument Expand(TaskItemGroup items, XDocument document) {
            return new XDocument(document.Declaration, Expand(items, document.Nodes()));
        }
        private XComment Expand(TaskItemGroup items, XComment comment) {
            return new XComment(Expand(items?.Key, comment.Value));
        }
        private XText Expand(TaskItemGroup items, XText text) {
            return new XText(Expand(items?.Key, text.Value));
        }
        private XCData Expand(TaskItemGroup items, XCData cdata) {
            return new XCData(Expand(items?.Key, cdata.Value));
        }
        private XAttribute Expand(TaskItemGroup items, XAttribute attribute) {
            return new XAttribute(attribute.Name, Expand(items?.Key, attribute.Value));
        }
    }
}
