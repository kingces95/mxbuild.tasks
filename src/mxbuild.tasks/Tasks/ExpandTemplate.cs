using System.Linq;
using Microsoft.Build.Framework;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Xml;
using System;

namespace Mxbuild.Tasks {
    internal class EmptyItemsException : Exception { }

    internal enum ExpandTemplateType {
        Xml,
        LineByLine,
    }

    public sealed class ExpandTemplate : AbstractTask {
        private const string Name = "__Name";

        protected override void Run() {
            var items = Items.ToLookup(o => o.GetMetadata(Name), StringComparer.InvariantCultureIgnoreCase);

            var type = (ExpandTemplateType)Enum.Parse(typeof(ExpandTemplateType), Type, ignoreCase: true);

            if (type == ExpandTemplateType.Xml)
                Result = new ExpandTemplateXml(items).Run(Input);

            else if (type == ExpandTemplateType.LineByLine)
                Result = new ExpandTemplateLineByLine(items).Run(Input);

            else
                throw new Exception($"Type '{Type}' is unrecognized.");
        }

        [Required]
        public string Input { get; set; }

        [Required]
        public string Type { get; set; }

        [Required]
        public ITaskItem[] Items { get; set; }

        [Output]
        public string Result { get; set; }
    }

    internal abstract class ExpandTemplateBase {
        private const string ItemMetadataRegex = @"%[(](?<first>\w*)([.](?<second>\w*))?[)]";

        private ILookup<string, ITaskItem> m_items;
        private string m_itemName;
        private ITaskItem m_item;
        private Queue<ITaskItem> m_itemQueue;
        private Stack<object> m_debug = new Stack<object>();

        internal ExpandTemplateBase(ILookup<string, ITaskItem> items) {
            m_items = items;
        }

        private string Expand(Match match) {
            var first = match.Groups["first"].Value;
            var second = match.Groups["second"].Value;

            // %(metadataName)
            var metadataName = first;

            // %(itemName.metadataName)
            if (!string.IsNullOrEmpty(second)) {
                var itemName = first;
                metadataName = second;

                // first iteration of an item
                if (m_itemName == null) {
                    m_itemName = first;

                    m_itemQueue = new Queue<ITaskItem>();
                    foreach (var o in m_items[itemName])
                        m_itemQueue.Enqueue(o);

                    if (!m_itemQueue.Any())
                        throw new EmptyItemsException();

                    m_item = m_itemQueue.Dequeue();
                }

                // detected nested item names
                else if (m_itemName != itemName)
                    throw new Exception(
                        $"While expanding '{m_itemName}' encountered nested expansion '{itemName}'.");
            }

            // substitute variable with metadata
            return m_item.GetMetadata(metadataName);
        }

        internal string Expand(string value) => Regex.Replace(value, ItemMetadataRegex, Expand);
        internal IEnumerable<object> ExpandMany(object template) {
            m_debug.Push(template);

            var isExpanding = m_itemQueue != null;

            while (true) {
                object result;

                try {
                    result = Expand(template);
                } catch (EmptyItemsException) {
                    break;
                }

                yield return result;

                if (isExpanding || m_itemQueue == null || !m_itemQueue.Any())
                    break;

                m_item = m_itemQueue.Dequeue();
            }

            if (!isExpanding) {
                m_itemQueue = null;
                m_item = null;
                m_itemName = null;
            }

            m_debug.Pop();
        }

        internal abstract object Expand(object template);
    }

    internal sealed class ExpandTemplateLineByLine : ExpandTemplateBase {

        internal ExpandTemplateLineByLine(ILookup<string, ITaskItem> items) : base(items) { }

        internal string Run(string input) {
            var lines = Regex.Split(input, Environment.NewLine).SelectMany(o => ExpandMany(o)).Cast<string>();
            return string.Join(Environment.NewLine, lines.ToArray());
        }

        internal override object Expand(object line) => Expand((string)line);
    }

    internal sealed class ExpandTemplateXml : ExpandTemplateBase {

        internal ExpandTemplateXml(ILookup<string, ITaskItem> items) : base(items) { }

        internal string Run(string input) {
            var doc = Expand(XDocument.Parse(input));
            return doc.Declaration.ToString() + Environment.NewLine + doc.ToString();
        }

        internal override object Expand(object template) {
            var element = (XElement)template;

            return new XElement(element.Name,
                new object[] {
                    Expand(element.Attributes()),
                    Expand(element.Nodes())
                }
            );
        }

        private object Expand(XObject node) {
            var type = node.NodeType;

            switch (type) {
                case XmlNodeType.Element:
                    return ExpandMany(node);

                case XmlNodeType.Attribute:
                    return Expand((XAttribute)node);

                case XmlNodeType.Document:
                    return Expand((XDocument)node);

                case XmlNodeType.Text:
                    return Expand((XText)node);

                case XmlNodeType.CDATA:
                    return Expand((XCData)node);

                case XmlNodeType.Comment:
                    return Expand((XComment)node);

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
        private object Expand(IEnumerable<XObject> nodes) => nodes.Select(o => Expand(o));
        private XDocument Expand(XDocument document) => new XDocument(document.Declaration, Expand(document.Nodes()));
        private XComment Expand(XComment comment) => new XComment(Expand(comment.Value));
        private XText Expand(XText text) => new XText(Expand(text.Value));
        private XCData Expand(XCData cdata) => new XCData(Expand(cdata.Value));
        private XAttribute Expand(XAttribute attribute) => new XAttribute(attribute.Name, Expand(attribute.Value));
    }
}
